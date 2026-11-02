using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DevPostIt.Storage;

public sealed record BackupSummary(int Notes, int Files);
public sealed class InvalidBackupException(string message) : IOException(message);

// 메모, 첨부 이미지, 휴지통, 복구 기록, 마인드맵, 할 일, 달력을 한 파일로 묶습니다. 검색 DB는 다시 만들 수 있어 넣지 않습니다.
public static class NoteBackup
{
    private const int FormatVersion = 1, MaxFiles = 100_000;
    private const long MaxBytes = 4L * 1024 * 1024 * 1024;
    private const string ManifestName = "devpostit-backup.json", ReadyMarker = ".restore-ready";
    private static readonly string[] Included = ["notes", ".hunmin/trash", ".hunmin/recovery", ".hunmin/preferences", ".hunmin/tools"];
    private sealed record ManifestFile(string Path, long Size, string Sha256);
    private sealed record Manifest(int Format, string App, DateTimeOffset Created, List<ManifestFile> Files);

    public static async Task<BackupSummary> CreateAsync(string root, string destination, string appVersion, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(root);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var files = new List<ManifestFile>();
            var notes = 0;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var folder in Included)
                {
                    var directory = Path.Combine(full, folder.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(directory)) continue;
                    foreach (var file in Walk(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var bytes = await ReadSharedAsync(file, cancellationToken);
                        var relative = Path.GetRelativePath(full, file).Replace(Path.DirectorySeparatorChar, '/');
                        var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                        await using (var output = entry.Open()) await output.WriteAsync(bytes, cancellationToken);
                        files.Add(new(relative, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
                        if (IsNoteFile(relative)) notes++;
                    }
                }
                var manifest = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                await using var writer = manifest.Open();
                await JsonSerializer.SerializeAsync(writer, new Manifest(FormatVersion, appVersion, DateTimeOffset.UtcNow, files), cancellationToken: cancellationToken);
            }
            using (var check = ZipFile.OpenRead(temporary))
                if (check.Entries.Count != files.Count + 1) throw new IOException("백업 파일을 확인하지 못했습니다.");
            File.Move(temporary, destination, overwrite: true);
            return new(notes, files.Count);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // 백업을 검증해 옆 폴더에 풀어 둡니다. 현재 데이터는 다음 시작 때 ApplyPending이 바꿉니다.
    public static async Task<BackupSummary> PrepareRestoreAsync(string backup, string root, CancellationToken cancellationToken)
    {
        var staging = StagingPath(root);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        try
        {
            using var archive = OpenArchive(backup);
            var manifest = ReadManifest(archive);
            var expected = manifest.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
            var entries = archive.Entries.Where(entry => entry.FullName != ManifestName).ToArray();
            if (entries.Length != expected.Count || expected.Count > MaxFiles || manifest.Files.Sum(file => file.Size) > MaxBytes)
                throw new InvalidBackupException("백업의 파일 목록이 맞지 않거나 너무 큽니다.");
            var codec = new FrontmatterCodec();
            var ids = new HashSet<Guid>();
            var stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging)) + Path.DirectorySeparatorChar;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!expected.TryGetValue(entry.FullName, out var file) || !IsSafePath(entry.FullName) || entry.Length != file.Size)
                    throw new InvalidBackupException($"백업에 허용하지 않는 항목이 있습니다: {entry.FullName}");
                var target = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidBackupException($"백업에 허용하지 않는 경로가 있습니다: {entry.FullName}");
                var bytes = new byte[file.Size];
                await using (var input = entry.Open()) await input.ReadExactlyAsync(bytes, cancellationToken);
                if (Convert.ToHexString(SHA256.HashData(bytes)) != file.Sha256)
                    throw new InvalidBackupException($"백업 파일이 손상되었습니다: {entry.FullName}");
                if (IsNoteFile(entry.FullName)) ValidateNote(codec, bytes, entry.FullName, ids);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, cancellationToken);
            }
            if (ids.Count == 0) throw new InvalidBackupException("백업에 복원할 메모가 없습니다.");
            await File.WriteAllTextAsync(Path.Combine(staging, ReadyMarker), "ready", cancellationToken);
            return new(ids.Count, entries.Length);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            throw;
        }
    }

    // 현재 데이터를 지우지 않고 옆으로 옮긴 뒤 교체합니다. 돌려준 경로에 복원 전 데이터가 남습니다.
    public static string? ApplyPending(string root)
    {
        var staging = StagingPath(root);
        if (!Directory.Exists(staging)) return null;
        var marker = Path.Combine(staging, ReadyMarker);
        if (!File.Exists(marker))
        {
            Directory.Delete(staging, true);
            return null;
        }
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string? previous = null;
        if (Directory.Exists(full))
        {
            using (new DataDirectoryLease(full)) { }
            previous = $"{full}.before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            Directory.Move(full, previous);
        }
        try
        {
            File.Delete(marker);
            Directory.Move(staging, full);
        }
        catch
        {
            if (previous is not null && !Directory.Exists(full)) Directory.Move(previous, full);
            throw;
        }
        return previous;
    }

    private static string StagingPath(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + ".restoring";
    private static bool IsNoteFile(string relative) => relative.StartsWith("notes/", StringComparison.Ordinal) && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    private static bool IsSafePath(string path)
    {
        if (path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) return false;
        if (path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(NoteFileName.InvalidCharacters) >= 0)) return false;
        return Included.Any(folder => path.StartsWith(folder + "/", StringComparison.Ordinal));
    }
    private static ZipArchive OpenArchive(string backup)
    {
        try { return ZipFile.OpenRead(backup); }
        catch (InvalidDataException error) { throw new InvalidBackupException($"백업 파일을 읽지 못했습니다: {error.Message}"); }
    }
    private static Manifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestName) ?? throw new InvalidBackupException("DevPostIt 백업 파일이 아닙니다.");
        try
        {
            using var stream = entry.Open();
            var manifest = JsonSerializer.Deserialize<Manifest>(stream) ?? throw new InvalidBackupException("백업 정보를 읽지 못했습니다.");
            if (manifest.Format != FormatVersion) throw new InvalidBackupException("지원하지 않는 백업 형식입니다.");
            return manifest;
        }
        catch (JsonException) { throw new InvalidBackupException("백업 정보를 읽지 못했습니다."); }
        catch (ArgumentNullException) { throw new InvalidBackupException("백업 정보를 읽지 못했습니다."); }
    }
    private static void ValidateNote(FrontmatterCodec codec, byte[] bytes, string relative, HashSet<Guid> ids)
    {
        try
        {
            var note = codec.Parse(new System.Text.UTF8Encoding(false, true).GetString(bytes), relative["notes/".Length..]);
            if (!ids.Add(note.Id)) throw new InvalidBackupException($"백업에 같은 메모 ID가 있습니다: {relative}");
        }
        catch (MissingNoteIdException) { /* 앱이 메모로 읽지 않는 파일도 그대로 보존합니다. */ }
        catch (FormatException error) { throw new InvalidBackupException($"백업의 메모를 읽지 못했습니다: {relative}\n{error.Message}"); }
        catch (System.Text.DecoderFallbackException) { throw new InvalidBackupException($"백업의 메모를 읽지 못했습니다: {relative}"); }
    }
    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"링크 또는 클라우드 전용 항목은 백업할 수 없습니다: {entry.FullName}");
            if (entry is DirectoryInfo) { foreach (var file in Walk(entry.FullName)) yield return file; }
            else if (!entry.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) yield return entry.FullName;
        }
    }
    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }
}
