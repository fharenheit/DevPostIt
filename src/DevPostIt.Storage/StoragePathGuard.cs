namespace DevPostIt.Storage;

internal static class StoragePathGuard
{
    internal static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0 && !IsSystemLink(current))
                throw new IOException("연결된 파일·디렉터리는 저장소에서 허용하지 않습니다.");
    }
    // macOS의 /var, /tmp, /etc는 운영체제가 /private 아래로 둔 링크라 사용자가 만든 연결이 아닙니다.
    private static bool IsSystemLink(string path) => OperatingSystem.IsMacOS() && path is "/var" or "/tmp" or "/etc" &&
        new FileInfo(path).LinkTarget?.TrimStart('/') == "private" + path;
}
