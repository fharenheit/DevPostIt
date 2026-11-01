using System.Security.Cryptography;

namespace DevPostIt.Storage;

// 기본 데이터는 Windows가 사용자별 앱 데이터용으로 정한 %LOCALAPPDATA%에 둡니다.
public static class NoteDataLocation
{
    public static string DefaultRoot(string localApplicationData) => Path.Combine(localApplicationData, "DevPostIt", "Data");
    public static string LegacyRoot(string documents) => Path.Combine(documents, "DevPostIt");

    // 이전 위치의 메모를 복사하고 검증한 뒤 한 번에 새 위치로 옮깁니다. 실패하면 새 위치를 만들지 않으며 원본은 항상 그대로 둡니다.
    public static bool MigrateLegacy(string legacyRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot) || !Directory.Exists(Path.Combine(legacyRoot, "notes"))) return false;
        using var lease = new DataDirectoryLease(legacyRoot);
        var staging = targetRoot + ".migrating";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        try
        {
            foreach (var name in new[] { "notes", ".hunmin" })
            {
                var source = Path.Combine(legacyRoot, name);
                if (Directory.Exists(source)) CopyVerified(source, Path.Combine(staging, name));
            }
            Directory.Move(staging, targetRoot);
            return true;
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            throw;
        }
    }

    private static void CopyVerified(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"링크 또는 클라우드 전용 항목은 옮길 수 없습니다: {entry.FullName}");
            var target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo) { CopyVerified(entry.FullName, target); continue; }
            if (entry.Name == "session.lock") continue;
            File.Copy(entry.FullName, target);
            if (!Hash(entry.FullName).AsSpan().SequenceEqual(Hash(target)))
                throw new IOException($"복사한 파일이 원본과 다릅니다: {entry.FullName}");
        }
    }

    private static byte[] Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
