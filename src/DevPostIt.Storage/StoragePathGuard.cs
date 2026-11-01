namespace DevPostIt.Storage;

internal static class StoragePathGuard
{
    internal static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("연결된 파일·디렉터리는 저장소에서 허용하지 않습니다.");
    }
}
