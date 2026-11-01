namespace DevPostIt.EditorHost;

// 미리보기에서 앱 밖으로 열 수 있는 주소입니다. 파일, 실행 파일, 앱 프로토콜은 열지 않습니다.
public static class PreviewLink
{
    private const int MaxLength = 2048;
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaxLength || url.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeMailto) return true;
        return uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    }
}
