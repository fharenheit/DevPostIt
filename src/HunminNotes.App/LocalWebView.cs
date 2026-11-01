using HunminNotes.EditorHost;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HunminNotes.App;

// 메모와 설정 창이 같은 프로필과 외부 리소스 차단 정책을 쓰도록 WebView2 설정을 한곳에 둡니다.
internal static class LocalWebView
{
    public static async Task<CoreWebView2> ConfigureAsync(WebView2 view, Func<string, bool> isAllowedPage)
    {
        var assets = System.IO.Path.Combine(AppContext.BaseDirectory, "editor-web");
        if (!System.IO.File.Exists(System.IO.Path.Combine(assets, "index.html")))
            throw new InvalidOperationException("편집기 파일이 없습니다. 프로젝트를 다시 빌드하세요.");
        var profile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPostIt", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
        await view.EnsureCoreWebView2Async(environment);
        var core = view.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.SetVirtualHostNameToFolderMapping("devpostit.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, args) => args.Cancel = !isAllowedPage(args.Uri);
        core.FrameNavigationStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!EditorOrigin.IsLocalResource(args.Request.Uri)) args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
        return core;
    }
}
