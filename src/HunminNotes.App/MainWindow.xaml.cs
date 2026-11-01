using System.Text.Json;
using System.Windows;
using HunminNotes.EditorHost;
using Microsoft.Web.WebView2.Core;

namespace HunminNotes.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Guid noteId = Guid.NewGuid();
    private EditorBridge? bridge;
    private string markdown = "# DevPostIt\n\n아이디어와 할 일을 Markdown으로 적어 보세요.\n\n- [ ] 오늘의 생각 정리하기\n\n> 글자 크기와 글꼴 설정은 앱을 다시 열어도 유지됩니다.\n";

    public MainWindow() => InitializeComponent();

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
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
                if (!EditorOrigin.IsLocalResource(args.Request.Uri))
                    args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            bridge = new EditorBridge(noteId, 0);
            core.WebMessageReceived += MessageReceived;
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) LoadingText.Text = $"편집기를 열지 못했습니다: {args.WebErrorStatus}";
            };
            core.Navigate(EditorOrigin.Page);
        }
        catch (Exception error) { LoadingText.Text = $"DevPostIt을 시작하지 못했습니다.\n{error.Message}"; }
    }

    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (bridge is null) return;
        try
        {
            var result = await bridge.ReceiveAsync(args.WebMessageAsJson, args.Source, CancellationToken.None);
            if (result.Change is not null) markdown = result.Change.Markdown;
            if (result.Message.Type == "editor.ready")
            {
                Send("note.load", new { markdown }, result.Message.RequestId);
                Loading.Visibility = Visibility.Collapsed;
            }
            else if (result.Message.Type == "note.saveRequested")
                Send("note.conflict", new { message = "파일 저장 기능은 아직 연결 전입니다. 변경 내용은 이 창에서 유지됩니다." }, result.Message.RequestId);
        }
        catch (EditorProtocolException) { /* 검증에 실패한 메시지는 상태를 바꾸지 않습니다. */ }
        catch (InvalidOperationException) { /* 창 종료와 겹친 WebView 이벤트는 무시합니다. */ }
    }

    private void Send(string type, object payload, string requestId)
    {
        var message = new EditorMessage(1, type, requestId, noteId, bridge!.Revision, JsonSerializer.SerializeToElement(payload));
        EditorView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private void WindowClosed(object? sender, EventArgs e) => EditorView.Dispose();
}
