namespace DevPostIt.EditorHost;

public static class EditorOrigin
{
    public const string Page = "https://devpostit.local/index.html";
    public const string SettingsPage = "https://devpostit.local/settings.html";
    public const string SearchPage = "https://devpostit.local/search.html";
    public const string AssetHost = "devpostit-assets.local";
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

    public static bool IsNoteAsset(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == AssetHost && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/[0-9a-f]{32}/[0-9a-f]{32}\\.(png|drawio\\.svg)$");

    public static bool IsDiagramFile(string? file) => file is not null && System.Text.RegularExpressions.Regex.IsMatch(file, "^[0-9a-f]{32}/[0-9a-f]{32}\\.drawio\\.svg\\z");

    public static bool IsLocalResource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == "devpostit.local" && uri.Port == 443 && uri.UserInfo.Length == 0;
}
