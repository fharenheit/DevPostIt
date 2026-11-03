using CoreGraphics;
using DevPostIt.Desktop;
using DevPostIt.EditorHost;
using DevPostIt.Storage;
using UniformTypeIdentifiers;
using WebKit;

namespace DevPostIt.Mac;

internal sealed class MacNoteWindow : NSWindow, IWebSurface, INoteWindow, INoteDialogs, INoteShell
{
    public const double DefaultWidth = 480, DefaultHeight = 640, MinimumWidth = 320, MinimumHeight = 360;
    private readonly NoteSession session;
    private readonly string assetsRoot;
    private readonly NSView container;
    private readonly NSTextField loading = NSTextField.CreateLabel("편집기를 여는 중입니다…");
    private readonly InputBlocker blocker = new();
    private readonly NSObject bridge;
    private readonly WebDelegate webDelegate;
    private WKWebView? view;
    private Func<string, string, Task>? onMessage;
    private bool closedOnce;

    public Guid NoteId => session.NoteId;
    public NoteSession Session => session;
    public string NoteColor { get; private set; } = NoteColors.Default;
    public bool Topmost { get; private set; }
    public event Action? CollapseRequested;
    public event Action? FrameChanged;
    public event Action? WindowClosed;
    public event Action<MacNoteWindow>? BecameKey;
    // 시험은 대화 상자 대신 답을 정합니다.
    internal Func<bool>? TrashConfirmation { get; set; }
    internal Func<string?>? PdfPathPicker { get; set; }

    public MacNoteWindow(NoteWorkspace workspace, Guid noteId)
        : base(new CGRect(0, 0, DefaultWidth, DefaultHeight), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
    {
        assetsRoot = workspace.AssetsRoot;
        session = new NoteSession(workspace, noteId, EditorOrigin.IsTrustedMacPage, this, this, this, this);
        session.CollapseRequested += () => CollapseRequested?.Invoke();
        ReleaseWhenClosed(false);
        Title = "Post It for Developer";
        TitlebarAppearsTransparent = true;
        MinSize = new CGSize(MinimumWidth, MinimumHeight);
        container = new NSView(ContentView!.Bounds) { AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable };
        ContentView = container;
        loading.Alignment = NSTextAlignment.Center;
        loading.Frame = container.Bounds;
        loading.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        container.AddSubview(loading);
        bridge = new BridgeHandler(this);
        webDelegate = new WebDelegate(this);
        Delegate = new NoteWindowDelegate(this);
        ApplyColor();
    }

    private void ApplyColor()
    {
        var caption = NotePalette.Caption(NoteColor);
        BackgroundColor = NSColor.FromRgb(caption.R, caption.G, caption.B);
    }

    public void ApplyAppearance(string color, bool topmost)
    {
        NoteColor = color;
        Topmost = topmost;
        ApplyColor();
        Level = topmost ? NSWindowLevel.Floating : NSWindowLevel.Normal;
    }

    // 노란 단추, ⌘M, 제목 표시줄 더블클릭은 Dock으로 내리지 않고 접습니다.
    public override void PerformMiniaturize(NSObject? sender) => Collapse();
    [Export("miniaturize:")] public void CollapseInsteadOfMiniaturize(NSObject? sender) => Collapse();
    private void Collapse() { if (session.CanCollapse) CollapseRequested?.Invoke(); }

    public override void SendEvent(NSEvent theEvent)
    {
        if (theEvent.Type == NSEventType.LeftMouseDown && theEvent.ClickCount == 2 && theEvent.LocationInWindow.Y >= ContentLayoutRect.GetMaxY())
        {
            Collapse();
            return;
        }
        base.SendEvent(theEvent);
    }

    async Task IWebSurface.PrepareAsync()
    {
        view = await LocalWebKit.CreateAsync(assetsRoot, (IWKScriptMessageHandler)bridge, webDelegate, webDelegate);
        view.Frame = container.Bounds;
        container.AddSubview(view, NSWindowOrderingMode.Below, loading);
    }

    void IWebSurface.Start(Func<string, string, Task> handler)
    {
        onMessage = handler;
        Navigate();
    }

    private void Navigate() => view!.LoadRequest(new NSUrlRequest(new NSUrl(EditorOrigin.MacPage)));

    void IWebSurface.PostMessage(string json)
    {
        if (view is null) return;
        var arguments = new NSDictionary<NSString, NSObject>(new NSString("json"), new NSString(json));
        view.CallAsyncJavaScript("window.__devpostitDeliver(json)", arguments, null, WKContentWorld.Page, null);
    }

    Task<string> IWebSurface.EvaluateAsync(string expression) => EvaluateAsync(expression);

    internal Task<string> EvaluateAsync(string expression) => LocalWebKit.EvaluateAsync(view!, expression);

    Task<bool> IWebSurface.PrintToPdfAsync(string path) => PrintToPdfAsync(path);

    // createPDF는 쪽을 나누지 않아 인쇄 작업으로 저장합니다. A4, 여백 0.6인치입니다.
    internal Task<bool> PrintToPdfAsync(string path)
    {
        const double A4Width = 8.27 * 72, A4Height = 11.69 * 72, Margin = 0.6 * 72;
        using var info = new NSPrintInfo
        {
            PaperSize = new CGSize(A4Width, A4Height), TopMargin = (nfloat)Margin, BottomMargin = (nfloat)Margin, LeftMargin = (nfloat)Margin, RightMargin = (nfloat)Margin,
            HorizontalPagination = NSPrintingPaginationMode.Fit, VerticalPagination = NSPrintingPaginationMode.Auto, JobDisposition = "NSPrintSaveJob",
        };
        info.Dictionary[new NSString("NSJobSavingURL")] = NSUrl.CreateFileUrl(path, false, null);
        var operation = view!.GetPrintOperation(info);
        operation.ShowsPrintPanel = false;
        operation.ShowsProgressPanel = false;
        operation.View!.Frame = new CGRect(0, 0, A4Width - 2 * Margin, A4Height - 2 * Margin);
        var done = new PrintCompletion();
        operation.RunOperationModal(this, done, new ObjCRuntime.Selector(PrintCompletion.Selector), IntPtr.Zero);
        return done.Finished.Task;
    }

    void INoteWindow.SetNoteTitle(string title) => MiniWindowTitle = title;
    void INoteWindow.HideLoading() => loading.RemoveFromSuperview();
    void INoteWindow.ShowStartupError(string message)
    {
        loading.StringValue = message;
        if (loading.Superview is null) container.AddSubview(loading);
    }
    void INoteWindow.SetInputEnabled(bool enabled)
    {
        if (enabled)
        {
            blocker.RemoveFromSuperview();
            if (view is not null) MakeFirstResponder(view);
            return;
        }
        blocker.Frame = container.Bounds;
        container.AddSubview(blocker);
        MakeFirstResponder(blocker);
    }
    public void BringToFront()
    {
        NSApplication.SharedApplication.Activate();
        MakeKeyAndOrderFront(this);
    }
    void INoteWindow.Close()
    {
        if (session.AllowClose) { Close(); return; }
        _ = session.HandleCloseRequestAsync();
    }
    void INoteWindow.Post(Action action) => BeginInvokeOnMainThread(action);

    bool INoteDialogs.ConfirmTrash() => TrashConfirmation?.Invoke() ?? MacDialogs.Ask("메모를 휴지통으로 옮기시겠습니까?", "메뉴 막대의 ‘휴지통’에서 복원할 수 있습니다.", "아니요", "예") == 1;
    RecoveryChoice INoteDialogs.AskRecovery() => MacDialogs.Ask("저장되지 않은 복구 초안이 있습니다.", "복원: 초안을 복원합니다.\n버리기: 현재 파일을 유지하고 초안을 버립니다.\n닫기: 복구 기록을 보존하고 닫습니다.", "복원", "버리기", "닫기") switch
    {
        0 => RecoveryChoice.Restore,
        1 => RecoveryChoice.Discard,
        _ => RecoveryChoice.KeepAndClose,
    };
    bool INoteDialogs.ReportCloseFailure(bool uncaptured, string error)
    {
        if (uncaptured)
        {
            MacDialogs.Ask("화면의 최신 입력을 안전하게 보존하지 못해 종료를 취소했습니다.", $"내용을 별도로 보관하거나 분량을 줄인 뒤 다시 시도하세요.\n{error}", "확인");
            return false;
        }
        return MacDialogs.Ask("저장을 완료하지 못했습니다.", $"{error}\n복구 기록을 보존한 채 종료할까요?", "예", "아니요") == 0;
    }
    Task INoteDialogs.ReportTrashFailureAsync(string error)
    {
        MacDialogs.Ask("메모를 휴지통으로 옮기지 못했습니다.", error, "확인");
        return Task.CompletedTask;
    }
    bool INoteDialogs.ConfirmLink(string url) => MacDialogs.Ask("이 링크를 기본 브라우저로 열까요?", url, "아니요", "예") == 1;

    void INoteShell.OpenLink(string url) => NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url));
    void INoteShell.OpenFolder(string path) => NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.CreateFileUrl(path, true, null));
    string? INoteShell.FindDiagramApp() => MacDiagramApp.Find();
    void INoteShell.OpenDiagram(string application, string path) => MacDiagramApp.Open(application, path);
    void INoteShell.ReportDiagramAppMissing()
    {
        if (MacDialogs.Ask("다이어그램을 편집하려면 draw.io Desktop이 필요합니다.", "내려받기 페이지를 기본 브라우저로 열까요?", "아니요", "예") == 1)
            NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(MacDiagramApp.DownloadPage));
    }
    byte[]? INoteShell.ReadClipboardPng() => MacClipboard.ReadPng(NSPasteboard.GeneralPasteboard);
    string? INoteShell.PickPdfPath(string title)
    {
        if (PdfPathPicker is { } picker) return picker();
        var safe = string.Concat(title.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return MacDialogs.PickSavePath("PDF로 저장", (safe.Length == 0 ? "메모" : safe) + ".pdf", "pdf");
    }

    private sealed class PrintCompletion : NSObject
    {
        public const string Selector = "printOperationDidRun:success:contextInfo:";
        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        [Export(Selector)] public void DidRun(NSPrintOperation operation, bool success, IntPtr context) => Finished.TrySetResult(success);
    }

    private sealed class NoteWindowDelegate(MacNoteWindow owner) : NSWindowDelegate
    {
        // 빨간 단추와 ⌘W는 휴지통 이동을 묻는 기존 흐름으로 들어갑니다.
        public override bool WindowShouldClose(NSObject sender)
        {
            owner.session.MarkUserClose();
            _ = owner.session.HandleCloseRequestAsync();
            return false;
        }
        public override void DidResize(NSNotification notification) => owner.FrameChanged?.Invoke();
        public override void DidMove(NSNotification notification) => owner.FrameChanged?.Invoke();
        public override void DidBecomeKey(NSNotification notification)
        {
            owner.BecameKey?.Invoke(owner);
            // draw.io에서 저장하고 돌아오면 그림을 다시 읽습니다.
            owner.session.NotifyAssetsChanged();
        }
        public override void WillClose(NSNotification notification)
        {
            if (owner.closedOnce) return;
            owner.closedOnce = true;
            owner.session.OnWindowClosed();
            if (owner.view is { } view) LocalWebKit.Detach(view);
            owner.WindowClosed?.Invoke();
        }
    }

    private sealed class BridgeHandler(MacNoteWindow owner) : NSObject, IWKScriptMessageHandler
    {
        public async void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            if (message.Body is not NSString body || LocalWebKit.TrustedSource(message) is not { } source || owner.onMessage is not { } handler) return;
            await handler(body.ToString(), source);
        }
    }

    private sealed class WebDelegate(MacNoteWindow owner) : WKNavigationDelegate, IWKUIDelegate
    {
        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            var allowed = navigationAction.TargetFrame?.MainFrame == true && EditorOrigin.IsTrustedMacPage(navigationAction.Request.Url?.AbsoluteString ?? "");
            decisionHandler(allowed ? WKNavigationActionPolicy.Allow : WKNavigationActionPolicy.Cancel);
        }
        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error) =>
            ((INoteWindow)owner).ShowStartupError($"편집기를 열지 못했습니다: {error.LocalizedDescription}");
        // 입력은 저장 쪽에 모두 전달되어 있으므로 최신 초안으로 다시 불러옵니다.
        public override void ContentProcessDidTerminate(WKWebView webView)
        {
            owner.Navigate();
            MacDialogs.Ask("편집기가 예기치 않게 종료되어 다시 불러왔습니다.", "마지막으로 전달된 입력까지 보존되어 있습니다.", "확인");
        }
    }

    private sealed class InputBlocker : NSView
    {
        public override bool AcceptsFirstResponder() => true;
        public override void KeyDown(NSEvent theEvent) { }
        public override void MouseDown(NSEvent theEvent) { }
    }
}

internal static class MacDialogs
{
    // 첫 번째 단추가 기본값입니다. 누른 단추의 순번을 돌려줍니다.
    public static int Ask(string message, string detail, params string[] buttons)
    {
        NSApplication.SharedApplication.Activate();
        using var alert = new NSAlert { MessageText = message, InformativeText = detail };
        foreach (var button in buttons) alert.AddButton(button);
        return (int)alert.RunModal() - 1000;
    }

    public static string? PickSavePath(string title, string name, string extension)
    {
        NSApplication.SharedApplication.Activate();
        var panel = NSSavePanel.SavePanel;
        panel.Title = title;
        panel.NameFieldStringValue = name;
        panel.AllowedContentTypes = [UTType.CreateFromExtension(extension)!];
        return panel.RunModal() == 1 ? panel.Url?.Path : null;
    }

    public static string[] PickOpenPaths(string title, bool multiple, string? extension = null)
    {
        NSApplication.SharedApplication.Activate();
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.AllowsMultipleSelection = multiple;
        if (extension is not null) panel.AllowedContentTypes = [UTType.CreateFromExtension(extension)!];
        return panel.RunModal() == 1 ? [.. panel.Urls.Select(url => url.Path).OfType<string>()] : [];
    }

    public static string? PickFolder(string title, string start)
    {
        NSApplication.SharedApplication.Activate();
        var panel = NSOpenPanel.OpenPanel;
        panel.Message = title;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.CanCreateDirectories = true;
        panel.DirectoryUrl = NSUrl.CreateFileUrl(start, true, null);
        return panel.RunModal() == 1 ? panel.Url?.Path : null;
    }
}
