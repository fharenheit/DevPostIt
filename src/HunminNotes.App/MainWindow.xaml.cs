using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using MessageBox = System.Windows.MessageBox;
using HunminNotes.Core;
using HunminNotes.EditorHost;
using HunminNotes.Storage;
using Microsoft.Web.WebView2.Core;

namespace HunminNotes.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Guid noteId;
    private readonly NoteWorkspace workspace;
    private readonly SemaphoreSlim messages = new(1, 1);
    private EditorBridge? bridge;
    private NoteService service => workspace.Service;

    private Task<NoteSaveState>? startup;
    private Task? initialization;
    private bool closing;
    private bool confirming;
    private readonly Queue<(string Json, string Source)> deferredMessages = new();
    private bool allowClose;
    private bool closed;
    private bool opened;

    private TaskCompletionSource<bool>? closeCompletion;
    private bool settingsRequested;
    private bool preserveOnClose = true;
    internal Func<MessageBoxResult>? DeleteConfirmation { get; set; }
    internal Func<Task>? InitializationDelay { get; set; }
    internal Func<string, Task>? DeleteFailureDialog { get; set; }
    internal Task<bool> RequestDeleteAsync() => BeginClose(false);
    internal Func<bool, string, MessageBoxResult>? CloseFailurePrompt { get; set; }
    private MessageBoxResult ReportCloseFailure(bool uncaptured, string error)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (CloseFailurePrompt is { } prompt) return prompt(uncaptured, error);
        return uncaptured
            ? MessageBox.Show(this, $"화면의 최신 입력을 안전하게 보존하지 못해 종료를 취소했습니다.\n내용을 별도로 보관하거나 분량을 줄인 뒤 다시 시도하세요.\n{error}", "미보존 초안", MessageBoxButton.OK, MessageBoxImage.Warning)
            : MessageBox.Show(this, $"저장을 완료하지 못했습니다.\n{error}\n복구 기록을 보존한 채 종료할까요?", "저장 오류", MessageBoxButton.YesNo, MessageBoxImage.Warning);
    }
    public Guid NoteId => noteId;
    public bool CanCollapse => !closed && !closing;
    public event Action<MainWindow>? CollapseRequested;
    public event Action? NewNoteRequested;
    public MainWindow(NoteWorkspace workspace, Guid noteId)
    {
        this.workspace = workspace; this.noteId = noteId;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
            ApplyTitleBarColors(handle);
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && CanCollapse) CollapseRequested?.Invoke(this); };
    }
    // 네이티브 제목 표시줄을 메모지 색에 맞춥니다. 지원하지 않는 Windows 10에서는 호출만 실패하고 기본 색을 유지합니다. 값은 0x00BBGGRR입니다.
    private static void ApplyTitleBarColors(IntPtr handle)
    {
        foreach (var (attribute, color) in new[] { (34, 0x00BDDFE8), (35, 0x00BFF3FF), (36, 0x001F2A2F) })
        {
            var value = color;
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF060 && !closing && !confirming) preserveOnClose = false;
        if (message == 0x00A3 && wParam.ToInt64() == 2 && CanCollapse)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => { if (CanCollapse) WindowState = WindowState.Minimized; }));
        }
        return IntPtr.Zero;
    }
    public Task<bool> RequestCloseAsync() => BeginClose(true);
    private Task<bool> BeginClose(bool preserve)
    {
        if (closed) return Task.FromResult(true);
        if (closeCompletion is not null) return closeCompletion.Task;
        var completion = closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (closing || confirming) return completion.Task;
        preserveOnClose = preserve;
        Close();
        return completion.Task;
    }
    private void CancelClose()
    {
        closing = false; EditorView.IsEnabled = true;
        preserveOnClose = true;
        closeCompletion?.TrySetResult(false); closeCompletion = null;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (initialization?.IsCompleted == true && bridge is null) initialization = InitializeEditorAsync();
        var pending = deferredMessages.ToArray();
        deferredMessages.Clear();
        foreach (var message in pending) _ = ProcessMessageAsync(message.Json, message.Source);
    }
    public void ShowSettings()
    {
        if (bridge is null || Loading.Visibility != Visibility.Collapsed) { settingsRequested = true; return; }
        Send("ui.openSettings", new { }, bridge.Revision);
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) => initialization ??= InitializeEditorAsync();

    private async Task InitializeEditorAsync()
    {
        try
        {
            if (InitializationDelay is { } delay) await delay();
            startup = Task.Run(OpenNotesAsync);
            var state = await startup;
            if (closing || closed) return;
            opened = true;
            Title = state.Note.Title;
            if (state.RecoveryPending)
            {
                var choice = MessageBox.Show(this, "저장되지 않은 복구 초안이 있습니다.\n예: 초안을 복원합니다.\n아니요: 현재 파일을 유지하고 초안을 버립니다.\n취소: 복구 기록을 보존하고 닫습니다.", "메모 복구", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel)
                {
                    allowClose = true; Close(); return;
                }
                state = await Task.Run(() => service!.ResolveRecoveryAsync(noteId, choice == MessageBoxResult.Yes, CancellationToken.None));
            }
            if (!state.HasUnsavedChanges)
            {
                try { state = await Task.Run(() => service!.FlushAsync(noteId, CancellationToken.None)); }
                catch (Exception) { state = await service!.GetStateAsync(noteId, CancellationToken.None); }
            }
            var assets = System.IO.Path.Combine(AppContext.BaseDirectory, "editor-web");
            if (!System.IO.File.Exists(System.IO.Path.Combine(assets, "index.html")))
                throw new InvalidOperationException("편집기 파일이 없습니다. 프로젝트를 다시 빌드하세요.");
            var profile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPostIt", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            await EditorView.EnsureCoreWebView2Async(environment);
            var core = EditorView.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.SetVirtualHostNameToFolderMapping("devpostit.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) => args.Cancel = !EditorOrigin.IsTrustedPage(args.Uri);
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!EditorOrigin.IsLocalResource(args.Request.Uri)) args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            bridge = new EditorBridge(noteId, state.Revision);
            service!.StateChanged += OnNoteStateChanged;
            core.WebMessageReceived += MessageReceived;
            core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) LoadingText.Text = $"편집기를 열지 못했습니다: {args.WebErrorStatus}"; };
            core.Navigate(EditorOrigin.Page);
        }
        catch (Exception error) { LoadingText.Text = $"메모장을 시작하지 못했습니다.\n{error.Message}"; }
    }

    private Task<NoteSaveState> OpenNotesAsync() => workspace.OpenNoteAsync(noteId, CancellationToken.None);

    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        await ProcessMessageAsync(args.WebMessageAsJson, args.Source);
    }
    private async Task ProcessMessageAsync(string json, string source)
    {
        if (bridge is null || closed) return;
        if (closing) { deferredMessages.Enqueue((json, source)); return; }
        await messages.WaitAsync();
        try
        {
            if (closed) return;
            if (closing) { deferredMessages.Enqueue((json, source)); return; }
            var result = await bridge.ReceiveAsync(json, source, CancellationToken.None);
            if (result.Change is not null) await Task.Run(() => service.ApplyChangeAsync(result.Change, CancellationToken.None));
            else if (result.Message.Type == "editor.ready")
            {
                var state = await service.GetStateAsync(noteId, CancellationToken.None);
                Send("note.load", new { markdown = state.Note.Markdown, title = state.Note.Title }, state.Revision);
                if (state.Error is not null) Send("note.conflict", new { message = state.Error }, state.Revision);
                else if (state.HasUnsavedChanges) Send("note.conflict", new { message = "복구 초안을 자동 저장하는 중입니다." }, state.Revision);
                Loading.Visibility = Visibility.Collapsed;
                if (settingsRequested) { settingsRequested = false; ShowSettings(); }
            }
            else if (result.Message.Type == "window.collapseRequested") CollapseRequested?.Invoke(this);
            else if (result.Message.Type == "note.newRequested") NewNoteRequested?.Invoke();
            else if (result.Message.Type == "note.saveRequested")
            {
                var state = await service.GetStateAsync(noteId, CancellationToken.None);
                if (state.Note.Markdown != result.Message.Payload.GetProperty("markdown").GetString() || (EditorBridge.ReadTitle(result.Message.Payload) is { } requestedTitle && requestedTitle != state.Note.Title))
                    throw new EditorProtocolException("저장 요청 본문이 현재 초안과 일치하지 않습니다.");
                state = await Task.Run(() => service.FlushAsync(noteId, CancellationToken.None));
                Send("note.saved", new { }, state.SavedRevision);
            }
        }
        catch (EditorProtocolException) { /* 잘못된 메시지는 저장에 사용하지 않습니다. */ }
        catch (Exception error)
        {
            if (!closed) Send("note.conflict", new { message = error.Message }, bridge.Revision);
        }
        finally { messages.Release(); }
    }

    private void OnNoteStateChanged(NoteSaveState state)
    {
        if (closed || state.Note.Id != noteId) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closed || closing || bridge is null) return;
            Title = state.Note.Title;
            if (!state.HasUnsavedChanges && !state.RecoveryPending) Send("note.saved", new { }, state.SavedRevision);
            if (state.Error is not null) Send("note.conflict", new { message = state.Error }, state.Revision);
        }));
    }

    private void Send(string type, object payload, long revision)
    {
        if (closed) return;
        var message = new EditorMessage(1, type, Guid.NewGuid().ToString(), noteId, revision, JsonSerializer.SerializeToElement(payload));
        EditorView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (service is null && startup is null) return;
        e.Cancel = true;
        if (closing || confirming) return;
        if (!preserveOnClose)
        {
            confirming = true;
            try
            {
                if (!IsVisible) Show();
                Activate();
                var answer = DeleteConfirmation?.Invoke() ?? MessageBox.Show(this, "메모를 영구 삭제하시겠습니까?", "메모 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) { CancelClose(); return; }
            }
            finally { confirming = false; }
        }
        closing = true;
        EditorView.IsEnabled = false;
        await messages.WaitAsync();
        var snapshotCaptured = true;
        try
        {
            if (initialization is not null) { try { await initialization; } catch (Exception) { } }
            if (!preserveOnClose)
            {
                await Task.Run(() => workspace.DeleteAsync(noteId, CancellationToken.None));
                allowClose = true; Close(); return;
            }
            if (opened)
            {
                if (bridge is not null && EditorView.CoreWebView2 is not null && Loading.Visibility == Visibility.Collapsed)
                {
                    snapshotCaptured = false;
                    var json = await EditorView.ExecuteScriptAsync("window.hunminEditor ? ({ ...window.hunminEditor.getDraft(), title: window.hunminEditor.getTitle() }) : null");
                    var draft = EditorDraftSnapshot.Parse(json);
                    var state = await service!.GetStateAsync(noteId, CancellationToken.None);
                    if (draft.Revision < state.Revision || draft.Revision == state.Revision && (draft.Markdown != state.Note.Markdown || draft.Title is not null && draft.Title != state.Note.Title))
                        throw new EditorProtocolException("화면과 저장 초안이 일치하지 않습니다.");
                    if (draft.Revision > state.Revision)
                    {
                        var message = new EditorMessage(1, "note.changed", Guid.NewGuid().ToString(), noteId, draft.Revision, JsonSerializer.SerializeToElement(new { markdown = draft.Markdown, title = draft.Title }));
                        var change = await bridge.HandleAsync(message, CancellationToken.None);
                        if (change is not null) await Task.Run(() => service!.ApplyChangeAsync(change, CancellationToken.None));
                    }
                    snapshotCaptured = true;
                }
                await Task.Run(() => service!.FlushAsync(noteId, CancellationToken.None));
            }
        }
        catch (Exception error)
        {
            if (!preserveOnClose)
            {
                if (DeleteFailureDialog is { } dialog) await dialog(error.Message);
                else MessageBox.Show(this, $"메모를 삭제하지 못했습니다.\n{error.Message}", "삭제 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                CancelClose(); return;
            }
            if (!snapshotCaptured)
            {
                ReportCloseFailure(true, error.Message);
                CancelClose(); return;
            }
            var state = opened ? await service!.GetStateAsync(noteId, CancellationToken.None) : null;
            if (state?.HasUnsavedChanges == true && ReportCloseFailure(false, error.Message) != MessageBoxResult.Yes)
            {
                CancelClose(); return;
            }
        }
        finally { messages.Release(); }

        allowClose = true;
        Close();
    }

    private void WindowClosed(object? sender, EventArgs e) { closed = true; deferredMessages.Clear(); service.StateChanged -= OnNoteStateChanged; EditorView.Dispose(); closeCompletion?.TrySetResult(true); }
}
