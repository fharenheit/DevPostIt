using DevPostIt.Core;
using static DevPostIt.Storage.StoragePathGuard;

namespace DevPostIt.Storage;

public sealed class DuplicateNoteIdException(Guid id) : IOException($"중복 메모 ID: {id}");

public sealed class MarkdownNoteRepository : INoteDeletionRepository
{
    private readonly string rootPath;
    private readonly FrontmatterCodec codec = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly AtomicFileWriter writer = new();

    public MarkdownNoteRepository(string root)
    {
        rootPath = Path.GetFullPath(root);
        RejectLinks(rootPath);
    }

    public async Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return (await ScanAsync(cancellationToken)).SingleOrDefault(note => note.Id == id); }
        finally { gate.Release(); }
    }

    public Task SaveAsync(Note note, CancellationToken cancellationToken) => SaveCoreAsync(note, cancellationToken, false);
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var note = (await ScanAsync(cancellationToken)).SingleOrDefault(note => note.Id == id);
            if (note is null) return;
            var (_, full) = Resolve(note.RelativePath);
            RejectLinks(full);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(full);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ScanAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    private async Task SaveCoreAsync(Note note, CancellationToken cancellationToken, bool importing)
    {
        var (relative, full) = Resolve(note.RelativePath);
        var content = codec.Serialize(note);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (importing && File.Exists(full))
                throw new IOException("가져오기 대상 파일이 이미 있습니다.");
            var notes = await ScanAsync(cancellationToken);
            var sameId = notes.SingleOrDefault(existing => existing.Id == note.Id);
            var samePath = notes.SingleOrDefault(existing => string.Equals(existing.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
            if (samePath is not null && samePath.Id != note.Id)
                throw new IOException("다른 메모가 사용하는 경로입니다.");
            if (samePath is null && File.Exists(full))
                throw new IOException("기존 파일을 덮어쓸 수 없습니다.");
            if (sameId is not null && !string.Equals(sameId.RelativePath, relative, StringComparison.OrdinalIgnoreCase))
                throw new DuplicateNoteIdException(note.Id);
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(full);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (samePath is null) await writer.WriteNewAsync(full, content, cancellationToken);
            else await writer.WriteAsync(full, content, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<Note> ImportAsync(string raw, string relativePath, CancellationToken cancellationToken)
    {
        var (relative, _) = Resolve(relativePath);
        var note = codec.Import(raw, relative);
        await SaveCoreAsync(note, cancellationToken, true);
        return note;
    }

    private async Task<List<Note>> ScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectLinks(rootPath);
        var notes = new List<Note>();
        var ids = new HashSet<Guid>();
        if (!Directory.Exists(rootPath)) return notes;
        foreach (var file in Files(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (relative, _) = Resolve(Path.GetRelativePath(rootPath, file));
            Note note;
            try { note = codec.Parse(await File.ReadAllTextAsync(file, cancellationToken), relative); }
            catch (MissingNoteIdException) { continue; }
            if (!ids.Add(note.Id)) throw new DuplicateNoteIdException(note.Id);
            notes.Add(note);
        }
        return notes;
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("연결된 파일·디렉터리는 저장소에서 허용하지 않습니다.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in Files(entry)) yield return file;
            }
            else if (string.Equals(Path.GetExtension(entry), ".md", StringComparison.OrdinalIgnoreCase))
                yield return entry;
        }
    }

    private (string Relative, string Full) Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("루트 기준 상대 경로가 필요합니다.", nameof(relativePath));
        var relative = relativePath.Replace('\\', '/');
        foreach (var part in relative.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new ArgumentException("안전하지 않은 파일 경로입니다.", nameof(relativePath));
        }
        if (!string.Equals(Path.GetExtension(relative), ".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Markdown 파일 경로가 필요합니다.", nameof(relativePath));
        var full = Path.GetFullPath(Path.Combine(rootPath, relative));
        var prefix = Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("저장 루트 밖의 경로입니다.", nameof(relativePath));
        RejectLinks(full);
        return (relative, full);
    }

}
