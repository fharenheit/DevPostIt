namespace DevPostIt.EditorHost;

public static class EditorOrigin
{
    public const string Page = "https://devpostit.local/index.html";
    public const string SettingsPage = "https://devpostit.local/settings.html";
    public static bool IsTrustedPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/index.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    // 설정 페이지는 메모 브리지를 쓰지 않으므로 IsTrustedPage와 분리합니다.
    public static bool IsSettingsPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/settings.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsLocalResource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == "devpostit.local" && uri.Port == 443 && uri.UserInfo.Length == 0;
}
