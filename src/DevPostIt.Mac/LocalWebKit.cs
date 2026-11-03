using DevPostIt.EditorHost;
using WebKit;

namespace DevPostIt.Mac;

// 메모 창이 같은 저장소와 외부 리소스 차단 정책을 쓰도록 WKWebView 설정을 한곳에 둡니다.
internal static class LocalWebKit
{
    private const string BridgeName = "bridge";
    // 웹 코드는 WebView2의 chrome.webview만 압니다. 메시지는 항상 JSON 문자열로 넘겨 EditorBridge의 검사를 그대로 씁니다.
    private const string Shim = """
        (() => {
          const handlers = [];
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            postMessage(message) { window.webkit.messageHandlers.bridge.postMessage(JSON.stringify(message)); },
            addEventListener(type, handler) { if (type === 'message') handlers.push(handler); },
            removeEventListener(type, handler) { const index = handlers.indexOf(handler); if (index >= 0) handlers.splice(index, 1); },
          };
          window.__devpostitDeliver = json => { const data = JSON.parse(json); handlers.forEach(handler => handler({ data })); };
        })();
        """;
    // 두 스킴 외의 요청은 페이지에 도달하기 전에 차단합니다.
    private const string BlockRules = """
        [{"trigger":{"url-filter":".*"},"action":{"type":"block"}},
         {"trigger":{"url-filter":"^devpostit(-assets)?://"},"action":{"type":"ignore-previous-rules"}}]
        """;
    private static readonly NSUuid ProfileId = new("8C3F5A52-6A1D-4E57-9B7C-2D4E1F0A6B11");
    private static Task<WKContentRuleList>? rules;
    private static WKWebsiteDataStore? store;

    // 시험은 별도 UUID를 받아 사용자의 웹 저장소와 섞이지 않게 합니다.
    private static WKWebsiteDataStore Store => store ??= WKWebsiteDataStore.Create(
        Environment.GetEnvironmentVariable("DEVPOSTIT_WEBKIT_STORE") is { Length: > 0 } id ? new NSUuid(id) : ProfileId);

    public static async Task<WKWebView> CreateAsync(string? assetsRoot, IWKScriptMessageHandler bridge, IWKNavigationDelegate navigation, IWKUIDelegate ui)
    {
        var editorRoot = Path.Combine(NSBundle.MainBundle.ResourcePath ?? "", "editor-web");
        if (!File.Exists(Path.Combine(editorRoot, "index.html"))) throw new InvalidOperationException("편집기 파일이 없습니다. 프로젝트를 다시 빌드하세요.");
        var compiled = await (rules ??= WKContentRuleListStore.DefaultStore.CompileContentRuleListAsync("devpostit-block-external", BlockRules));
        var configuration = new WKWebViewConfiguration { WebsiteDataStore = Store };
        configuration.SetUrlSchemeHandler(new EditorSchemeHandler(editorRoot), EditorOrigin.MacScheme);
        if (assetsRoot is not null) configuration.SetUrlSchemeHandler(new AssetSchemeHandler(assetsRoot), EditorOrigin.MacAssetScheme);
        configuration.UserContentController.AddContentRuleList(compiled);
        configuration.UserContentController.AddScriptMessageHandler(bridge, BridgeName);
        configuration.UserContentController.AddUserScript(new WKUserScript(new NSString(Shim), WKUserScriptInjectionTime.AtDocumentStart, true));
        return new WKWebView(CoreGraphics.CGRect.Empty, configuration)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            NavigationDelegate = navigation,
            UIDelegate = ui,
        };
    }

    // WebView2의 ExecuteScriptAsync처럼 결과를 JSON 문자열로 돌려줍니다.
    public static Task<string> EvaluateAsync(WKWebView view, string expression)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CallAsyncJavaScript($"return JSON.stringify(await ({expression})) ?? 'null';", null, null, WKContentWorld.Page, (result, error) =>
        {
            if (error is not null) completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
            else completion.TrySetResult((result as NSString)?.ToString() ?? "null");
        });
        return completion.Task;
    }

    public static void Detach(WKWebView view)
    {
        view.StopLoading();
        view.Configuration.UserContentController.RemoveScriptMessageHandler(BridgeName);
        view.RemoveFromSuperview();
    }

    // 메시지를 보낸 프레임이 주 프레임이고 출처가 앱 페이지일 때만 출처 문자열을 돌려줍니다.
    public static string? TrustedSource(WKScriptMessage message)
    {
        var frame = message.FrameInfo;
        if (!frame.MainFrame || frame.SecurityOrigin.Protocol != EditorOrigin.MacScheme || frame.SecurityOrigin.Host != "app") return null;
        return frame.Request.Url?.AbsoluteString;
    }

    internal static void Respond(IWKUrlSchemeTask task, NSUrl url, int status, string? contentType = null, byte[]? body = null)
    {
        var headers = new NSMutableDictionary<NSString, NSString> { [new NSString("X-Content-Type-Options")] = new NSString("nosniff") };
        if (contentType is not null) headers[new NSString("Content-Type")] = new NSString(contentType);
        if (body is not null) headers[new NSString("Content-Length")] = new NSString(body.Length.ToString());
        using var response = new NSHttpUrlResponse(url, status, "HTTP/1.1", NSDictionary.FromObjectsAndKeys(headers.Values, headers.Keys));
        task.DidReceiveResponse(response);
        if (body is not null) { using var data = NSData.FromArray(body); task.DidReceiveData(data); }
        task.DidFinish();
    }
}

internal sealed class EditorSchemeHandler(string root) : NSObject, IWKUrlSchemeHandler
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8", [".json"] = "application/json", [".wasm"] = "application/wasm", [".svg"] = "image/svg+xml",
        [".png"] = "image/png", [".ttf"] = "font/ttf", [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".txt"] = "text/plain; charset=utf-8",
    };
    private readonly string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var url = urlSchemeTask.Request.Url;
        var source = url.AbsoluteString ?? "";
        if (!EditorOrigin.IsLocalMacResource(source)) { LocalWebKit.Respond(urlSchemeTask, url, 403); return; }
        var path = Path.GetFullPath(Path.Combine(prefix, Uri.UnescapeDataString(new Uri(source).AbsolutePath).TrimStart('/')));
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(path) || !ContentTypes.TryGetValue(Path.GetExtension(path), out var type))
        {
            LocalWebKit.Respond(urlSchemeTask, url, 404);
            return;
        }
        LocalWebKit.Respond(urlSchemeTask, url, 200, type, File.ReadAllBytes(path));
    }

    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }
}

// 다른 출처이므로 img 표시만 되고 스크립트는 내용을 읽지 못합니다. CORS 헤더를 주지 않습니다.
internal sealed class AssetSchemeHandler(string root) : NSObject, IWKUrlSchemeHandler
{
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var url = urlSchemeTask.Request.Url;
        var source = url.AbsoluteString ?? "";
        if (!EditorOrigin.IsMacNoteAsset(source)) { LocalWebKit.Respond(urlSchemeTask, url, 403); return; }
        var path = Path.Combine(root, new Uri(source).AbsolutePath.TrimStart('/'));
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Directory?.LinkTarget is not null) { LocalWebKit.Respond(urlSchemeTask, url, 404); return; }
        LocalWebKit.Respond(urlSchemeTask, url, 200, source.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/svg+xml", File.ReadAllBytes(path));
    }

    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }
}
