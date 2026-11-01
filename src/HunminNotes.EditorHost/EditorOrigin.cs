namespace HunminNotes.EditorHost;

public static class EditorOrigin
{
    public const string Page = "https://devpostit.local/index.html";
    public static bool IsTrustedPage(string source) => IsLocalResource(source) &&
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/index.html" &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0;

    public static bool IsLocalResource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host == "devpostit.local" && uri.Port == 443 && uri.UserInfo.Length == 0;
}
