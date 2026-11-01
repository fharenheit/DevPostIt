using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using HunminNotes.Core;
using HunminNotes.EditorHost;
using HunminNotes.Storage;
using Microsoft.Web.WebView2.Core;

namespace HunminNotes.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid FirstNoteId = Guid.Parse("a8187628-1101-4030-8060-a48a3b704c60");
    private readonly SemaphoreSlim messages = new(1, 1);
    private EditorBridge? bridge;
    private NoteService? service;
    private DataDirectoryLease? dataLease;
    private Task<NoteSaveState>? startup;
    private bool closing;
    private bool allowClose;
    private bool closed;
    private bool opened;

    public MainWindow() => InitializeComponent();

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            startup = Task.Run(OpenNotesAsync);
            var state = await startup;
            if (closing || closed) return;
            opened = true;
            Title = $"{state.Note.Title} — DevPostIt";
            if (state.RecoveryPending)
            {
                var choice = MessageBox.Show(this, "저장되지 않은 복구 초안이 있습니다.\n예: 초안을 복원합니다.\n아니요: 현재 파일을 유지하고 초안을 버립니다.\n취소: 복구 기록을 보존하고 닫습니다.", "DevPostIt 복구", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel)
                {
                    await service!.DisposeAsync(); allowClose = true; Close(); return;
                }
                state = await Task.Run(() => service!.ResolveRecoveryAsync(FirstNoteId, choice == MessageBoxResult.Yes, CancellationToken.None));
            }
            if (!state.HasUnsavedChanges)
            {
                try { state = await Task.Run(() => service!.FlushAsync(FirstNoteId, CancellationToken.None)); }
                catch (Exception) { state = await service!.GetStateAsync(FirstNoteId, CancellationToken.None); }
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
            bridge = new EditorBridge(FirstNoteId, state.Revision);
            service!.StateChanged += OnNoteStateChanged;
            core.WebMessageReceived += MessageReceived;
            core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) LoadingText.Text = $"편집기를 열지 못했습니다: {args.WebErrorStatus}"; };
            core.Navigate(EditorOrigin.Page);
        }
        catch (Exception error) { LoadingText.Text = $"DevPostIt을 시작하지 못했습니다.\n{error.Message}"; }
    }

    private async Task<NoteSaveState> OpenNotesAsync()
    {
        var root = Environment.GetEnvironmentVariable("DEVPOSTIT_DATA_DIR") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DevPostIt");
        dataLease = new DataDirectoryLease(root);
        var repository = new MarkdownNoteRepository(System.IO.Path.Combine(root, "notes"));
        if (await repository.GetAsync(FirstNoteId, CancellationToken.None) is null)
        {
            var now = DateTimeOffset.UtcNow;
            var note = new Note(FirstNoteId, "Inbox/첫메모.md", "첫 메모", "# DevPostIt\n\n아이디어와 할 일을 Markdown으로 적어 보세요.\n\n- [ ] 오늘의 생각 정리하기\n\n> 변경 내용은 자동 저장됩니다.\n", [], now, now);
            await repository.SaveAsync(note, CancellationToken.None);
        }
        service = new NoteService(repository,
            new FileRecoveryJournal(System.IO.Path.Combine(root, ".hunmin", "recovery")),
            new SqliteNoteIndex(System.IO.Path.Combine(root, ".hunmin", "note-index.db")));
        return await service.OpenAsync(FirstNoteId, CancellationToken.None);
    }

    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (bridge is null || service is null || closing) return;
        var json = args.WebMessageAsJson;
        var source = args.Source;
        await messages.WaitAsync();
        try
        {
            if (closing) return;
            var result = await bridge.ReceiveAsync(json, source, CancellationToken.None);
            if (result.Change is not null) await Task.Run(() => service.ApplyChangeAsync(result.Change, CancellationToken.None));
            else if (result.Message.Type == "editor.ready")
            {
                var state = await service.GetStateAsync(FirstNoteId, CancellationToken.None);
                Send("note.load", new { markdown = state.Note.Markdown, title = state.Note.Title }, state.Revision);
                if (state.Error is not null) Send("note.conflict", new { message = state.Error }, state.Revision);
                else if (state.HasUnsavedChanges) Send("note.conflict", new { message = "복구 초안을 자동 저장하는 중입니다." }, state.Revision);
                Loading.Visibility = Visibility.Collapsed;
            }
            else if (result.Message.Type == "note.saveRequested")
            {
                var state = await service.GetStateAsync(FirstNoteId, CancellationToken.None);
                if (state.Note.Markdown != result.Message.Payload.GetProperty("markdown").GetString() || (EditorBridge.ReadTitle(result.Message.Payload) is { } requestedTitle && requestedTitle != state.Note.Title))
                    throw new EditorProtocolException("저장 요청 본문이 현재 초안과 일치하지 않습니다.");
                state = await Task.Run(() => service.FlushAsync(FirstNoteId, CancellationToken.None));
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
        if (closed) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closed || closing || bridge is null) return;
            Title = $"{state.Note.Title} — DevPostIt";
            if (!state.HasUnsavedChanges && !state.RecoveryPending) Send("note.saved", new { }, state.SavedRevision);
            if (state.Error is not null) Send("note.conflict", new { message = state.Error }, state.Revision);
        }));
    }

    private void Send(string type, object payload, long revision)
    {
        if (closed) return;
        var message = new EditorMessage(1, type, Guid.NewGuid().ToString(), FirstNoteId, revision, JsonSerializer.SerializeToElement(payload));
        EditorView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (service is null && startup is null) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        EditorView.IsEnabled = false;
        await messages.WaitAsync();
        var snapshotCaptured = true;
        try
        {
            if (startup is not null) { try { await startup; } catch (Exception) { } }
            if (opened)
            {
                if (bridge is not null && EditorView.CoreWebView2 is not null && Loading.Visibility == Visibility.Collapsed)
                {
                    snapshotCaptured = false;
                    var json = await EditorView.ExecuteScriptAsync("window.hunminEditor ? ({ ...window.hunminEditor.getDraft(), title: window.hunminEditor.getTitle() }) : null");
                    var draft = EditorDraftSnapshot.Parse(json);
                    var state = await service!.GetStateAsync(FirstNoteId, CancellationToken.None);
                    if (draft.Revision < state.Revision || draft.Revision == state.Revision && (draft.Markdown != state.Note.Markdown || draft.Title is not null && draft.Title != state.Note.Title))
                        throw new EditorProtocolException("화면과 저장 초안이 일치하지 않습니다.");
                    if (draft.Revision > state.Revision)
                    {
                        var message = new EditorMessage(1, "note.changed", Guid.NewGuid().ToString(), FirstNoteId, draft.Revision, JsonSerializer.SerializeToElement(new { markdown = draft.Markdown, title = draft.Title }));
                        var change = await bridge.HandleAsync(message, CancellationToken.None);
                        if (change is not null) await Task.Run(() => service!.ApplyChangeAsync(change, CancellationToken.None));
                    }
                    snapshotCaptured = true;
                }
                await Task.Run(() => service!.FlushAsync(FirstNoteId, CancellationToken.None));
            }
        }
        catch (Exception error)
        {
            if (!snapshotCaptured)
            {
                MessageBox.Show(this, $"화면의 최신 입력을 안전하게 보존하지 못해 종료를 취소했습니다.\n내용을 별도로 보관하거나 분량을 줄인 뒤 다시 시도하세요.\n{error.Message}", "DevPostIt 미보존 초안", MessageBoxButton.OK, MessageBoxImage.Warning);
                closing = false; EditorView.IsEnabled = true; return;
            }
            var state = opened ? await service!.GetStateAsync(FirstNoteId, CancellationToken.None) : null;
            if (state?.HasUnsavedChanges == true && MessageBox.Show(this, $"저장을 완료하지 못했습니다.\n{error.Message}\n복구 기록을 보존한 채 종료할까요?", "DevPostIt 저장 오류", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                closing = false; EditorView.IsEnabled = true; return;
            }
        }
        finally { messages.Release(); }
        if (service is not null) await service.DisposeAsync();
        allowClose = true;
        Close();
    }

    private void WindowClosed(object? sender, EventArgs e) { closed = true; EditorView.Dispose(); dataLease?.Dispose(); }
}
