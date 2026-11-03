using System.Text.Json;
using CoreGraphics;
using DevPostIt.EditorHost;
using WebKit;

namespace DevPostIt.Mac;

// 메모와 별개인 창의 공통 부분입니다. 메모와 같은 웹 저장소와 외부 리소스 차단 정책을 쓰고, 정해진 페이지만 엽니다.
internal abstract class MacWebWindow : NSWindow
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string page;
    private readonly Func<string, bool> isAllowed;
    private readonly NSObject bridge;
    private readonly PageDelegate pageDelegate;
    private readonly TaskCompletionSource closedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool flushed;
    private bool closedOnce;

    public WKWebView? View { get; private set; }
    public event Action? PageLoaded;
    public event Action? WindowClosed;

    protected MacWebWindow(string title, string page, Func<string, bool> isAllowed, double width, double height, double minimumWidth, double minimumHeight)
        : base(new CGRect(0, 0, width, height), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
    {
        this.page = page;
        this.isAllowed = isAllowed;
        ReleaseWhenClosed(false);
        Title = title;
        MinSize = new CGSize(minimumWidth, minimumHeight);
        BackgroundColor = NSColor.FromRgb(255, 253, 245);
        bridge = new BridgeHandler(this);
        pageDelegate = new PageDelegate(this);
        Delegate = new WindowDelegate(this);
        Center();
    }

    public async Task OpenAsync()
    {
        try
        {
            var view = await LocalWebKit.CreateAsync(null, (IWKScriptMessageHandler)bridge, pageDelegate, pageDelegate);
            view.Frame = ContentView!.Bounds;
            ContentView.AddSubview(view);
            View = view;
            view.LoadRequest(new NSUrlRequest(new NSUrl(EditorOrigin.MacAddress(page))));
        }
        catch (Exception error) when (error is InvalidOperationException or NSErrorException) { ShowFailure(error.Message); }
    }

    private void ShowFailure(string message)
    {
        flushed = true;
        var label = NSTextField.CreateLabel($"창을 열지 못했습니다.\n{message}");
        label.Frame = ContentView!.Bounds.Inset(16, 16);
        label.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        ContentView.AddSubview(label);
    }

    protected abstract Task ReceiveAsync(string json, string source);
    protected virtual Task SavePendingAsync(string json) => Task.CompletedTask;
    protected virtual void OnPageLoaded() { }
    protected virtual bool HasPending => false;

    public void Post(object message)
    {
        if (closedOnce || View is not { } view) return;
        var arguments = new NSDictionary<NSString, NSObject>(new NSString("json"), new NSString(JsonSerializer.Serialize(message, Json)));
        view.CallAsyncJavaScript("window.__devpostitDeliver(json)", arguments, null, WKContentWorld.Page, null);
    }

    public Task<string> EvaluateAsync(string expression) => LocalWebKit.EvaluateAsync(View!, expression);

    public void BringToFront()
    {
        NSApplication.SharedApplication.Activate();
        MakeKeyAndOrderFront(this);
    }

    // 닫기 전에 페이지가 아직 보내지 않은 내용을 받아 저장합니다.
    public async Task FlushAndCloseAsync()
    {
        await FlushAsync();
        Close();
        await closedCompletion.Task;
    }

    private async Task FlushAsync()
    {
        if (flushed || View is null) { flushed = true; return; }
        flushed = true;
        if (!HasPending) return;
        try
        {
            var result = await EvaluateAsync("window.devpostitPending ? window.devpostitPending() : null");
            if (JsonSerializer.Deserialize<string?>(result) is { } pending) await SavePendingAsync(pending);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MacDialogs.Ask("마지막 변경을 저장하지 못했습니다.", error.Message, "확인");
        }
    }

    private sealed class WindowDelegate(MacWebWindow owner) : NSWindowDelegate
    {
        public override bool WindowShouldClose(NSObject sender)
        {
            if (owner.flushed || !owner.HasPending || owner.View is null) return true;
            _ = FinishAsync();
            return false;
        }
        private async Task FinishAsync()
        {
            await owner.FlushAsync();
            owner.Close();
        }
        public override void WillClose(NSNotification notification)
        {
            if (owner.closedOnce) return;
            owner.closedOnce = true;
            if (owner.View is { } view) LocalWebKit.Detach(view);
            owner.closedCompletion.TrySetResult();
            owner.WindowClosed?.Invoke();
        }
    }

    private sealed class BridgeHandler(MacWebWindow owner) : NSObject, IWKScriptMessageHandler
    {
        public async void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            if (message.Body is not NSString body || LocalWebKit.TrustedSource(message) is not { } address || EditorOrigin.CanonicalMacSource(address) is not { } source) return;
            await owner.ReceiveAsync(body.ToString(), source);
        }
    }

    private sealed class PageDelegate(MacWebWindow owner) : WKNavigationDelegate, IWKUIDelegate
    {
        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            var allowed = navigationAction.TargetFrame?.MainFrame == true &&
                EditorOrigin.CanonicalMacSource(navigationAction.Request.Url?.AbsoluteString ?? "") is { } source && owner.isAllowed(source);
            decisionHandler(allowed ? WKNavigationActionPolicy.Allow : WKNavigationActionPolicy.Cancel);
        }
        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            owner.OnPageLoaded();
            owner.PageLoaded?.Invoke();
        }
        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error) => owner.ShowFailure(error.LocalizedDescription);
        public override void ContentProcessDidTerminate(WKWebView webView) => webView.Reload();
    }
}

internal sealed class MacFormatterWindow() : MacWebWindow("소스코드 포맷터", EditorOrigin.FormatterPage, EditorOrigin.IsFormatterPage, 760, 680, 420, 320)
{
    protected override Task ReceiveAsync(string json, string source) => Task.CompletedTask;
}

// 페이지는 검색어와 메모 ID만 보내며 파일 경로는 다루지 않습니다.
internal sealed class MacSearchWindow(DevPostIt.Storage.NoteWorkspace workspace, Action<Guid> open) : MacWebWindow("메모 검색", EditorOrigin.SearchPage, EditorOrigin.IsSearchPage, 480, 560, 320, 240)
{
    private const int MaxResults = 50;
    private long latestQuery;

    protected override async Task ReceiveAsync(string json, string source)
    {
        if (SearchMessage.TryReadOpen(json, source, out var id)) { open(id); return; }
        if (!SearchMessage.TryReadQuery(json, source, out var query)) return;
        var ticket = ++latestQuery;
        object message;
        try
        {
            var found = await Task.Run(() => workspace.SearchAsync(query, MaxResults, CancellationToken.None));
            message = new { type = "search.results", query, results = found.Select(item => new { id = item.Note.Id, title = item.Note.Title, excerpt = item.Excerpt, updated = item.Note.Updated }) };
        }
        catch (Exception error) { message = new { type = "search.failed", query, message = error.Message }; }
        if (ticket == latestQuery) Post(message);
    }
}
