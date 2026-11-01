using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using MessageBox = System.Windows.MessageBox;
using DevPostIt.Core;
using DevPostIt.EditorHost;
using DevPostIt.Storage;
using Microsoft.Web.WebView2.Core;

namespace DevPostIt.App;

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
    public event Action? SettingsRequested;
    public event Action? SearchRequested;
    public event Action? CaptureRequested;
    internal Func<string, string?> PdfPathPicker { get; set; } = PickPdfPath;
    internal Action<string> ImageFolderOpener { get; set; } = path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    internal Func<string, bool> LinkConfirmation { get; set; }
    internal Action<string> LinkOpener { get; set; } = url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    internal string? PendingPreferences { get; set; }
    internal Func<byte[]?> ClipboardImageReader { get; set; } = ClipboardPng;
    public event Action<string, bool>? AppearanceChanged;
    public string NoteColor { get; private set; } = NoteColors.Default;
    public void ApplyAppearance(string color, bool topmost)
    {
        NoteColor = color;
        Topmost = topmost;
        if (new WindowInteropHelper(this).Handle is var handle && handle != IntPtr.Zero) ApplyTitleBarColors(handle, color);
    }
    public MainWindow(NoteWorkspace workspace, Guid noteId)
    {
        this.workspace = workspace; this.noteId = noteId;
        LinkConfirmation = url => MessageBox.Show(this, $"이 링크를 기본 브라우저로 열까요?\n\n{url}", "링크 열기", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        InitializeComponent();
        Icon = NoteIcon.WindowImage;
        // 창 제목은 모든 메모가 같으므로 작업 표시줄 미리보기 설명으로 메모 이름을 구분합니다.
        TaskbarItemInfo = new TaskbarItemInfo();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
            ApplyTitleBarColors(handle, NoteColor);
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && CanCollapse) CollapseRequested?.Invoke(this); };
    }
    // 네이티브 제목 표시줄을 메모지 색에 맞춥니다. 지원하지 않는 Windows 10에서는 호출만 실패하고 기본 색을 유지합니다. 값은 0x00BBGGRR입니다.
    internal static void ApplyTitleBarColors(IntPtr handle, string? noteColor = null)
    {
        foreach (var (attribute, color) in new[] { (34, 0x00BDDFE8), (35, NoteAppearance.CaptionColor(noteColor)), (36, 0x001F2A2F) })
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
            var core = await LocalWebView.ConfigureAsync(EditorView, EditorOrigin.IsTrustedPage, workspace.AssetsRoot);
            TaskbarItemInfo.Description = state.Note.Title;
            service!.ExternalChanged += OnExternalChanged;
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
                Send("ui.appearance", new { color = NoteColor, topmost = Topmost }, state.Revision);
                if (PendingPreferences is { } preferences)
                {
                    PendingPreferences = null;
                    Send("ui.preferences", new { value = preferences }, state.Revision);
                }
                Loading.Visibility = Visibility.Collapsed;
            }
            else if (result.Message.Type == "window.collapseRequested") CollapseRequested?.Invoke(this);
            else if (result.Message.Type == "note.newRequested") NewNoteRequested?.Invoke();
            else if (result.Message.Type == "window.settingsRequested") SettingsRequested?.Invoke();
            else if (result.Message.Type == "window.searchRequested") SearchRequested?.Invoke();
            else if (result.Message.Type == "window.captureRequested") CaptureRequested?.Invoke();
            else if (result.Message.Type == "note.exportPdfRequested") await ExportPdfAsync();
            else if (result.Message.Type == "note.openLinkRequested")
            {
                var url = result.Message.Payload.GetProperty("url").GetString()!;
                if (LinkConfirmation(url)) LinkOpener(url);
            }
            else if (result.Message.Type == "note.openImagesRequested") ImageFolderOpener(await Task.Run(() => workspace.EnsureImageFolder(noteId)));
            else if (result.Message.Type == "window.appearanceChanged")
            {
                var color = result.Message.Payload.GetProperty("color").GetString()!;
                var topmost = result.Message.Payload.GetProperty("topmost").GetBoolean();
                ApplyAppearance(color, topmost);
                AppearanceChanged?.Invoke(color, topmost);
            }
            else if (result.Message.Type == "note.imagePasteRequested")
            {
                var png = ClipboardImageReader() ?? throw new InvalidOperationException("클립보드에서 이미지를 찾지 못했습니다.");
                var markdown = await Task.Run(() => workspace.SaveImageAsync(noteId, png, CancellationToken.None));
                Send("note.imageSaved", new { markdown }, bridge.Revision);
            }
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

    internal bool IsEditorReady => bridge is not null && !closed && !closing && Loading.Visibility == Visibility.Collapsed;
    // 캡처한 이미지를 커서 위치에 넣습니다. 미리보기 중이면 소스 편집으로 돌아옵니다.
    internal async Task InsertImageAsync(byte[] png)
    {
        if (!IsEditorReady) throw new InvalidOperationException("메모가 아직 준비되지 않았습니다.");
        var markdown = await Task.Run(() => workspace.SaveImageAsync(noteId, png, CancellationToken.None));
        await EditorView.ExecuteScriptAsync("window.hunminEditor.setMode('source')");
        Send("note.imageSaved", new { markdown }, bridge!.Revision);
    }
    private static string? PickPdfPath(string title)
    {
        var name = string.Concat(title.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "PDF로 저장", Filter = "PDF 파일 (*.pdf)|*.pdf", FileName = (name.Length == 0 ? "메모" : name) + ".pdf" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    // 화면은 페이지가 미리보기로 바꿔 둔 상태입니다. 끝나면 항상 원래 보기로 돌리도록 알립니다.
    private async Task ExportPdfAsync()
    {
        try
        {
            var state = await service.GetStateAsync(noteId, CancellationToken.None);
            if (PdfPathPicker(state.Note.Title) is not { } path) return;
            var core = EditorView.CoreWebView2;
            var settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.PageWidth = 8.27; settings.PageHeight = 11.69;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0.6;
            if (!await core.PrintToPdfAsync(path, settings)) Send("note.conflict", new { message = "PDF로 저장하지 못했습니다." }, bridge!.Revision);
        }
        finally { if (!closed) Send("note.pdfFinished", new { }, bridge!.Revision); }
    }
    private static byte[]? ClipboardPng()
    {
        // PNG 형식이 있으면 앱이 넣은 원본을 그대로 씁니다.
        if (System.Windows.Clipboard.GetData("PNG") is System.IO.MemoryStream png && IsDecodable(png)) return png.ToArray();
        if (System.Windows.Clipboard.ContainsImage() && System.Windows.Clipboard.GetImage() is { } image) return EncodeClipboardImage(image);
        // 탐색기에서 이미지 파일을 복사한 경우입니다.
        if (System.Windows.Clipboard.ContainsFileDropList() && System.Windows.Clipboard.GetFileDropList() is { Count: 1 } files) return ImageFileToPng(files[0]!);
        return null;
    }
    private static bool IsDecodable(System.IO.MemoryStream png)
    {
        try
        {
            png.Position = 0;
            return BitmapDecoder.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Count > 0;
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or System.IO.IOException) { return false; }
    }
    // Windows 클립보드의 비트맵은 알파값이 모두 0인 경우가 많습니다. 그대로 저장하면 투명해지므로 그때만 불투명으로 바꿉니다.
    internal static byte[] EncodeClipboardImage(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var allTransparent = true;
        for (var index = 3; index < pixels.Length && allTransparent; index += 4) allTransparent = pixels[index] == 0;
        if (!allTransparent) return EncodePng(image);
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        return EncodePng(BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, image.DpiX, image.DpiY, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride));
    }
    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
    internal static byte[]? ImageFileToPng(string path)
    {
        const long maxBytes = 20 * 1024 * 1024;
        if (System.IO.Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp")) return null;
        try
        {
            if (new System.IO.FileInfo(path).Length > maxBytes) throw new InvalidOperationException("이미지는 20MB까지 붙여넣을 수 있습니다.");
            using var stream = System.IO.File.OpenRead(path);
            return EncodePng(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { return null; }
    }
    private void OnExternalChanged(ExternalChange change)
    {
        if (closed || change.NoteId != noteId) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closed || closing || bridge is null) return;
            if (change.Kind == ExternalChangeKind.Reloaded)
            {
                bridge.SyncRevision(change.State.Revision);
                Send("note.load", new { markdown = change.State.Note.Markdown, title = change.State.Note.Title }, change.State.Revision);
            }
            else if (change.Message is not null) Send("note.conflict", new { message = change.Message }, bridge.Revision);
        }));
    }
    private void OnNoteStateChanged(NoteSaveState state)
    {
        if (closed || state.Note.Id != noteId) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closed || closing || bridge is null) return;
            TaskbarItemInfo.Description = state.Note.Title;
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
                var answer = DeleteConfirmation?.Invoke() ?? MessageBox.Show(this, "메모를 휴지통으로 옮기시겠습니까?\n트레이의 ‘휴지통’에서 복원할 수 있습니다.", "메모 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
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
            // 화면의 최신 입력까지 저장한 뒤 휴지통으로 옮깁니다.
            if (!preserveOnClose)
            {
                await Task.Run(() => workspace.TrashAsync(noteId, CancellationToken.None));
                allowClose = true; Close(); return;
            }
        }
        catch (Exception error)
        {
            if (!preserveOnClose)
            {
                if (DeleteFailureDialog is { } dialog) await dialog(error.Message);
                else MessageBox.Show(this, $"메모를 휴지통으로 옮기지 못했습니다.\n{error.Message}", "삭제 오류", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void WindowClosed(object? sender, EventArgs e) { closed = true; deferredMessages.Clear(); service.StateChanged -= OnNoteStateChanged; service.ExternalChanged -= OnExternalChanged; EditorView.Dispose(); closeCompletion?.TrySetResult(true); }
}
