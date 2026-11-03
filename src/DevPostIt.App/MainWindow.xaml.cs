using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using MessageBox = System.Windows.MessageBox;
using DevPostIt.Desktop;
using DevPostIt.EditorHost;
using DevPostIt.Storage;
using Microsoft.Web.WebView2.Core;

namespace DevPostIt.App;

public partial class MainWindow : Window, IWebSurface, INoteWindow, INoteDialogs, INoteShell
{
    private readonly NoteSession session;
    private readonly string assetsRoot;
    private int repaintRun;

    internal Func<MessageBoxResult>? DeleteConfirmation { get; set; }
    internal Func<Task>? InitializationDelay { get => session.InitializationDelay; set => session.InitializationDelay = value; }
    internal Func<string, Task>? DeleteFailureDialog { get; set; }
    internal Task<bool> RequestDeleteAsync() => session.RequestDeleteAsync();
    internal Func<bool, string, MessageBoxResult>? CloseFailurePrompt { get; set; }
    public Guid NoteId => session.NoteId;
    public bool CanCollapse => session.CanCollapse;
    public event Action<MainWindow>? CollapseRequested;
    public event Action? NewNoteRequested;
    public event Action? SettingsRequested;
    public event Action? SearchRequested;
    public event Action? CaptureRequested;
    internal Func<string, string?> PdfPathPicker { get; set; } = PickPdfPathDialog;
    internal Action<string> ImageFolderOpener { get; set; } = path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    internal Func<string, bool> LinkConfirmation { get; set; }
    internal Action<string> LinkOpener { get; set; } = url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    internal Func<string?> DiagramAppFinder { get; set; } = DiagramApp.Find;
    internal Action<string, string> DiagramAppOpener { get; set; } = DiagramApp.Open;
    internal Action DiagramAppMissing { get; set; }
    internal string? PendingPreferences { get => session.PendingPreferences; set => session.PendingPreferences = value; }
    internal int RepaintCount { get; private set; }
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
        assetsRoot = workspace.AssetsRoot;
        session = new NoteSession(workspace, noteId, EditorOrigin.IsTrustedPage, this, this, this, this);
        session.CollapseRequested += () => CollapseRequested?.Invoke(this);
        session.NewNoteRequested += () => NewNoteRequested?.Invoke();
        session.SettingsRequested += () => SettingsRequested?.Invoke();
        session.SearchRequested += () => SearchRequested?.Invoke();
        session.CaptureRequested += () => CaptureRequested?.Invoke();
        session.AppearanceChanged += (color, topmost) => AppearanceChanged?.Invoke(color, topmost);
        DiagramAppMissing = () => DiagramApp.OfferDownload(this);
        // draw.io에서 저장하고 돌아오면 그림을 다시 읽습니다.
        Activated += (_, _) => session.NotifyAssetsChanged();
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
        EditorView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245);
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) RepaintAfterShow(); };
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
        if (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF060) session.MarkUserClose();
        if (message == 0x00A3 && wParam.ToInt64() == 2 && CanCollapse)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => { if (CanCollapse) WindowState = WindowState.Minimized; }));
        }
        return IntPtr.Zero;
    }
    public Task<bool> RequestCloseAsync() => session.RequestCloseAsync();

    // 숨겼다가 다시 보이면 WebView2가 화면을 그리지 않아 하얗게 남을 때가 있습니다. 창 크기를 바꿀 때처럼 편집기 크기를 잠깐 바꿔 다시 그리게 합니다.
    private async void RepaintAfterShow()
    {
        var run = ++repaintRun;
        await Task.Delay(150);
        if (run != repaintRun || !session.HasEditor || !IsVisible) return;
        EditorView.Margin = new Thickness(0, 0, 1, 0);
        await Task.Delay(60);
        if (!session.HasEditor) return;
        EditorView.Margin = new Thickness(0);
        RepaintCount++;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) => _ = session.EnsureInitializedAsync();

    internal bool IsEditorReady => session.IsEditorReady;
    internal Task InsertImageAsync(byte[] png) => session.InsertImageAsync(png);

    Task IWebSurface.PrepareAsync() => LocalWebView.ConfigureAsync(EditorView, EditorOrigin.IsTrustedPage, assetsRoot);
    void IWebSurface.Start(Func<string, string, Task> onMessage)
    {
        var core = EditorView.CoreWebView2;
        core.WebMessageReceived += async (_, args) => await onMessage(args.WebMessageAsJson, args.Source);
        core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) LoadingText.Text = $"편집기를 열지 못했습니다: {args.WebErrorStatus}"; };
        core.Navigate(EditorOrigin.Page);
    }
    void IWebSurface.PostMessage(string json) => EditorView.CoreWebView2.PostWebMessageAsJson(json);
    Task<string> IWebSurface.EvaluateAsync(string expression) => EditorView.ExecuteScriptAsync(expression);
    Task<bool> IWebSurface.PrintToPdfAsync(string path)
    {
        var core = EditorView.CoreWebView2;
        var settings = core.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.PageWidth = 8.27; settings.PageHeight = 11.69;
        settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0.6;
        return core.PrintToPdfAsync(path, settings);
    }

    void INoteWindow.SetNoteTitle(string title) => TaskbarItemInfo.Description = title;
    void INoteWindow.HideLoading() => Loading.Visibility = Visibility.Collapsed;
    void INoteWindow.ShowStartupError(string message) => LoadingText.Text = message;
    void INoteWindow.SetInputEnabled(bool enabled) => EditorView.IsEnabled = enabled;
    void INoteWindow.BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    void INoteWindow.Post(Action action) => Dispatcher.BeginInvoke(action);

    bool INoteDialogs.ConfirmTrash() => (DeleteConfirmation?.Invoke() ?? MessageBox.Show(this, "메모를 휴지통으로 옮기시겠습니까?\n트레이의 ‘휴지통’에서 복원할 수 있습니다.", "메모 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)) == MessageBoxResult.Yes;
    RecoveryChoice INoteDialogs.AskRecovery() => MessageBox.Show(this, "저장되지 않은 복구 초안이 있습니다.\n예: 초안을 복원합니다.\n아니요: 현재 파일을 유지하고 초안을 버립니다.\n취소: 복구 기록을 보존하고 닫습니다.", "메모 복구", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
    {
        MessageBoxResult.Yes => RecoveryChoice.Restore,
        MessageBoxResult.No => RecoveryChoice.Discard,
        _ => RecoveryChoice.KeepAndClose,
    };
    bool INoteDialogs.ReportCloseFailure(bool uncaptured, string error)
    {
        if (CloseFailurePrompt is { } prompt) return prompt(uncaptured, error) == MessageBoxResult.Yes;
        return (uncaptured
            ? MessageBox.Show(this, $"화면의 최신 입력을 안전하게 보존하지 못해 종료를 취소했습니다.\n내용을 별도로 보관하거나 분량을 줄인 뒤 다시 시도하세요.\n{error}", "미보존 초안", MessageBoxButton.OK, MessageBoxImage.Warning)
            : MessageBox.Show(this, $"저장을 완료하지 못했습니다.\n{error}\n복구 기록을 보존한 채 종료할까요?", "저장 오류", MessageBoxButton.YesNo, MessageBoxImage.Warning)) == MessageBoxResult.Yes;
    }
    Task INoteDialogs.ReportTrashFailureAsync(string error)
    {
        if (DeleteFailureDialog is { } dialog) return dialog(error);
        MessageBox.Show(this, $"메모를 휴지통으로 옮기지 못했습니다.\n{error}", "삭제 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }
    bool INoteDialogs.ConfirmLink(string url) => LinkConfirmation(url);

    void INoteShell.OpenLink(string url) => LinkOpener(url);
    void INoteShell.OpenFolder(string path) => ImageFolderOpener(path);
    string? INoteShell.FindDiagramApp() => DiagramAppFinder();
    void INoteShell.OpenDiagram(string application, string path) => DiagramAppOpener(application, path);
    void INoteShell.ReportDiagramAppMissing() => DiagramAppMissing();
    byte[]? INoteShell.ReadClipboardPng() => ClipboardImageReader();
    string? INoteShell.PickPdfPath(string title) => PdfPathPicker(title);

    private static string? PickPdfPathDialog(string title)
    {
        var name = string.Concat(title.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "PDF로 저장", Filter = "PDF 파일 (*.pdf)|*.pdf", FileName = (name.Length == 0 ? "메모" : name) + ".pdf" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
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
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (session.AllowClose) return;
        e.Cancel = true;
        await session.HandleCloseRequestAsync();
    }

    private void WindowClosed(object? sender, EventArgs e) { session.OnWindowClosed(); EditorView.Dispose(); }
}
