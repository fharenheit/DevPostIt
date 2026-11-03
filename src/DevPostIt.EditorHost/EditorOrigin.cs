namespace DevPostIt.EditorHost;

public static class EditorOrigin
{
    public const string Page = "https://devpostit.local/index.html";
    public const string SettingsPage = "https://devpostit.local/settings.html";
    public const string SearchPage = "https://devpostit.local/search.html";
    public const string FormatterPage = "https://devpostit.local/formatter.html";
    public const string MindMapPage = "https://devpostit.local/mindmap.html";
    public const string TodoPage = "https://devpostit.local/todo.html";
    public const string CalendarPage = "https://devpostit.local/calendar.html";
    public const string AssetHost = "devpostit-assets.local";
    public const string MacScheme = "devpostit";
    public const string MacAssetScheme = "devpostit-assets";
    public const string MacPage = "devpostit://app/index.html";
    public static bool IsTrustedPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/index.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    // 설정 페이지는 메모 브리지를 쓰지 않으므로 IsTrustedPage와 분리합니다.
    public static bool IsSettingsPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/settings.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsSearchPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/search.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsFormatterPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/formatter.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsMindMapPage(string source) => IsLocalPage(source, "/mindmap.html");
    public static bool IsTodoPage(string source) => IsLocalPage(source, "/todo.html");
    public static bool IsCalendarPage(string source) => IsLocalPage(source, "/calendar.html");
    private static bool IsLocalPage(string source, string path) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == path && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsNoteAsset(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == AssetHost && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/[0-9a-f]{32}/[0-9a-f]{32}\\.(png|drawio\\.svg)$");

    public static bool IsDiagramFile(string? file) => file is not null && System.Text.RegularExpressions.Regex.IsMatch(file, "^[0-9a-f]{32}/[0-9a-f]{32}\\.drawio\\.svg\\z");

    // macOS의 WKWebView는 https 처리기를 등록할 수 없어 사용자 지정 스킴을 씁니다.
    public static bool IsLocalMacResource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == MacScheme && uri.Host == "app" && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    public static bool IsTrustedMacPage(string source) => IsLocalMacResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/index.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsMacNoteAsset(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == MacAssetScheme && uri.Host == "notes" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/[0-9a-f]{32}/[0-9a-f]{32}\\.(png|drawio\\.svg)\\z");

    public static bool IsLocalResource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == "devpostit.local" && uri.Port == 443 && uri.UserInfo.Length == 0;
}
