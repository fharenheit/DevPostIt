using System.Collections.Concurrent;
using DevPostIt.Core;

namespace DevPostIt.Storage;

public record TrashedNote(Guid Id, string Title, string RelativePath, DateTimeOffset TrashedAt);
public record NoteSearchResult(Note Note, string Excerpt);
public record UnusedImage(string Path, long Size);

public sealed class NoteWorkspace : IAsyncDisposable
{
    private readonly DataDirectoryLease lease;
    private readonly MarkdownNoteRepository repository;
    private readonly ConcurrentDictionary<Guid, Note> notes = new();
    private readonly object notesGate = new();
    private readonly HashSet<Guid> deleted = [];
    private readonly SemaphoreSlim creation = new(1, 1);
    private readonly SemaphoreSlim syncing = new(1, 1);
    private readonly SqliteNoteIndex index;
    private readonly FrontmatterCodec codec = new();
    private readonly AtomicFileWriter writer = new();
    private readonly string trashRoot;
    private readonly string assetsRoot;
    private FileSystemWatcher? watcher;
    private Timer? syncTimer;
    private bool disposing;
    public event Action<string>? ExternalSyncFailed;
    public string AssetsRoot => assetsRoot;
    public NoteService Service { get; }
    public string Root { get; }
    public IReadOnlyList<Note> Notes => notes.Values.OrderByDescending(note => note.Updated).ThenBy(note => note.Id).ToArray();

    private NoteWorkspace(string root)
    {
        Root = Path.GetFullPath(root);
        lease = new DataDirectoryLease(root);
        try
        {
            repository = new MarkdownNoteRepository(Path.Combine(root, "notes"));
            index = new SqliteNoteIndex(Path.Combine(root, ".hunmin", "note-index.db"));
            trashRoot = Path.Combine(Root, ".hunmin", "trash");
            assetsRoot = Path.Combine(Root, "notes", "assets");
            Service = new NoteService(repository, new FileRecoveryJournal(Path.Combine(root, ".hunmin", "recovery")), index);
            Service.StateChanged += state => { lock (notesGate) { if (!deleted.Contains(state.Note.Id)) notes[state.Note.Id] = state.Note; } };
        }
        catch { lease.Dispose(); throw; }
    }
    public static async Task<NoteWorkspace> OpenAsync(string root, CancellationToken cancellationToken)
    {
        var workspace = new NoteWorkspace(root);
        try
        {
            foreach (var note in await workspace.repository.ListAsync(cancellationToken)) workspace.notes[note.Id] = note;
            if (workspace.notes.IsEmpty)
            {
                var now = DateTimeOffset.UtcNow;
                var note = new Note(Guid.NewGuid(), "Inbox/첫메모.md", "첫 메모", "# 메모\n\n아이디어와 할 일을 Markdown으로 적어 보세요.\n\n> 변경 내용은 자동 저장됩니다.\n", [], now, now);
                await workspace.repository.SaveAsync(note, cancellationToken);
                workspace.notes[note.Id] = note;
            }
            // 한 번도 열지 않은 메모도 검색되게 합니다. 인덱스는 파생 데이터이므로 실패해도 시작을 막지 않습니다.
            try { await workspace.index.SynchronizeAsync(workspace.notes.Values.ToArray(), cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException) { }
            return workspace;
        }
        catch { await workspace.DisposeAsync(); throw; }
    }
    public async Task<NoteSaveState> OpenNoteAsync(Guid id, CancellationToken cancellationToken)
    {
        var state = await Service.OpenAsync(id, cancellationToken);
        lock (notesGate) { if (!deleted.Contains(id)) notes[id] = state.Note; }
        return state;
    }
    public async Task<Note> CreateAsync(CancellationToken cancellationToken)
    {
        await creation.WaitAsync(cancellationToken);
        try
        {
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var note = new Note(id, $"Inbox/{id:N}.md", $"새 메모 {notes.Count + 1}", "", [], now, now);
            await repository.SaveAsync(note, cancellationToken);
            notes[id] = note;
            await OpenNoteAsync(id, cancellationToken);
            return note;
        }
        finally { creation.Release(); }
    }
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await OpenNoteAsync(id, cancellationToken);
        await Service.DeleteAsync(id, cancellationToken);
        lock (notesGate) { deleted.Add(id); notes.TryRemove(id, out _); }
    }
    // 글꼴·편집기 설정은 WebView 저장소에 있어 백업과 복원을 위해 데이터 폴더에 사본을 둡니다.
    public const int MaxPreferencesLength = 8192;
    public string PreferencesPath => Path.Combine(Root, ".hunmin", "preferences", "editor.json");
    public async Task SavePreferencesAsync(string? value, CancellationToken cancellationToken)
    {
        if (value is null || value.Length > MaxPreferencesLength) return;
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        await writer.WriteAsync(PreferencesPath, value, cancellationToken);
    }
    public static string? ReadPreferences(string root)
    {
        try
        {
            var path = Path.Combine(root, ".hunmin", "preferences", "editor.json");
            if (!File.Exists(path)) return null;
            var value = File.ReadAllText(path);
            return value.Length <= MaxPreferencesLength ? value : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    // 백업 전에 열린 메모를 파일에 맞춥니다. 저장하지 못한 입력은 복구 기록으로 백업에 들어갑니다.
    public async Task FlushOpenNotesAsync(CancellationToken cancellationToken)
    {
        foreach (var note in Notes)
        {
            if (!Service.IsOpen(note.Id)) continue;
            try { await Service.FlushAsync(note.Id, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException) { }
        }
    }

    public async Task<NoteSearchResult[]> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        if (text.Length == 0) return Notes.Take(limit).Select(note => new NoteSearchResult(note, Excerpt(note, ""))).ToArray();
        var ids = await index.SearchAsync(text, limit, cancellationToken);
        return ids.Select(id => notes.TryGetValue(id, out var note) ? note : null).OfType<Note>().Select(note => new NoteSearchResult(note, Excerpt(note, text))).ToArray();
    }
    private static string Excerpt(Note note, string query)
    {
        var body = System.Text.RegularExpressions.Regex.Replace(note.Markdown, @"\s+", " ").Trim();
        var at = query.Length == 0 ? -1 : body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, at - 30);
        var length = Math.Min(body.Length - start, 120);
        return (start > 0 ? "…" : "") + body.Substring(start, length) + (start + length < body.Length ? "…" : "");
    }

    // X는 영구 삭제가 아니라 휴지통 이동입니다. 사본을 먼저 쓰므로 어느 단계에서 실패해도 메모가 사라지지 않습니다.
    public async Task TrashAsync(Guid id, CancellationToken cancellationToken)
    {
        await OpenNoteAsync(id, cancellationToken);
        var state = await Service.FlushAsync(id, cancellationToken);
        Directory.CreateDirectory(trashRoot);
        var (file, record) = TrashPaths(id);
        await writer.WriteAsync(file, codec.Serialize(state.Note), cancellationToken);
        await writer.WriteAsync(record, System.Text.Json.JsonSerializer.Serialize(new TrashedNote(id, state.Note.Title, state.Note.RelativePath, DateTimeOffset.UtcNow)), cancellationToken);
        try { await Service.DeleteAsync(id, cancellationToken); }
        catch
        {
            File.Delete(record); File.Delete(file);
            throw;
        }
        lock (notesGate) { deleted.Add(id); notes.TryRemove(id, out _); }
    }
    public IReadOnlyList<TrashedNote> TrashedNotes
    {
        get
        {
            if (!Directory.Exists(trashRoot)) return [];
            var result = new List<TrashedNote>();
            foreach (var file in Directory.EnumerateFiles(trashRoot, "*.json"))
                try { if (System.Text.Json.JsonSerializer.Deserialize<TrashedNote>(File.ReadAllText(file)) is { } item) result.Add(item); }
                catch (Exception error) when (error is System.Text.Json.JsonException or IOException) { /* 손상된 기록은 목록에서만 뺍니다. */ }
            return result.OrderByDescending(item => item.TrashedAt).ToArray();
        }
    }
    public async Task<Note> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var (file, record) = TrashPaths(id);
        var item = System.Text.Json.JsonSerializer.Deserialize<TrashedNote>(await File.ReadAllTextAsync(record, cancellationToken)) ?? throw new InvalidDataException("휴지통 기록이 손상되었습니다.");
        var note = codec.Parse(await File.ReadAllTextAsync(file, cancellationToken), item.RelativePath);
        if (note.Id != id) throw new InvalidDataException("휴지통 기록의 메모 ID가 일치하지 않습니다.");
        try { await repository.SaveAsync(note, cancellationToken); }
        catch (IOException)
        {
            note = note with { RelativePath = $"Inbox/{id:N}.md" };
            await repository.SaveAsync(note, cancellationToken);
        }
        lock (notesGate) { deleted.Remove(id); notes[id] = note; }
        try { await index.UpsertAsync(note, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException) { /* 다음 시작 때 다시 맞춥니다. */ }
        File.Delete(record); File.Delete(file);
        return note;
    }
    public Task PurgeAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (file, record) = TrashPaths(id);
        File.Delete(record); File.Delete(file);
        var assets = Path.Combine(assetsRoot, id.ToString("N"));
        if (Directory.Exists(assets) && (File.GetAttributes(assets) & FileAttributes.ReparsePoint) == 0) Directory.Delete(assets, true);
        return Task.CompletedTask;
    }
    public async Task EmptyTrashAsync(CancellationToken cancellationToken)
    {
        foreach (var item in TrashedNotes) await PurgeAsync(item.Id, cancellationToken);
    }
    private (string File, string Record) TrashPaths(Guid id) => (Path.Combine(trashRoot, $"{id:N}.md"), Path.Combine(trashRoot, $"{id:N}.json"));

    // 이 메모의 이미지 폴더입니다. 이미지가 아직 없어도 열 수 있도록 만들어 둡니다.
    public string EnsureImageFolder(Guid id)
    {
        if (!notes.ContainsKey(id)) throw new KeyNotFoundException("메모를 찾지 못했습니다.");
        var directory = Path.Combine(assetsRoot, id.ToString("N"));
        StoragePathGuard.RejectLinks(directory);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // 붙여넣은 이미지를 메모별 폴더에 저장하고, 메모 파일 기준 상대 경로의 Markdown을 돌려줍니다.
    public async Task<string> SaveImageAsync(Guid id, byte[] png, CancellationToken cancellationToken)
    {
        const int maxBytes = 20 * 1024 * 1024;
        if (png.Length is 0 or > maxBytes) throw new InvalidDataException("이미지는 20MB까지 붙여넣을 수 있습니다.");
        if (!notes.TryGetValue(id, out var note)) throw new KeyNotFoundException("메모를 찾지 못했습니다.");
        var name = $"{Guid.NewGuid():N}.png";
        var directory = EnsureImageFolder(id);
        var temporary = Path.Combine(directory, name + ".tmp");
        await File.WriteAllBytesAsync(temporary, png, cancellationToken);
        File.Move(temporary, Path.Combine(directory, name));
        var depth = note.RelativePath.Replace('\\', '/').Count(character => character == '/');
        return $"![이미지]({string.Concat(Enumerable.Repeat("../", depth))}assets/{id:N}/{name})";
    }

    // draw.io Desktop이 그대로 열어 저장하는 SVG입니다. 편집 데이터는 content 속성에 들어 있습니다. 저장 전에는 안내 그림이 보입니다.
    private const string EmptyDiagram = """
        <svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="240px" height="80px" viewBox="0 0 240 80" content="&lt;mxfile&gt;&lt;diagram name=&quot;Page-1&quot;&gt;&lt;mxGraphModel&gt;&lt;root&gt;&lt;mxCell id=&quot;0&quot;/&gt;&lt;mxCell id=&quot;1&quot; parent=&quot;0&quot;/&gt;&lt;/root&gt;&lt;/mxGraphModel&gt;&lt;/diagram&gt;&lt;/mxfile&gt;"><rect x="1" y="1" width="238" height="78" fill="#fffdf5" stroke="#a89e80" stroke-dasharray="4 3"/><text x="120" y="45" font-size="14" font-family="sans-serif" fill="#665e4b" text-anchor="middle">빈 다이어그램</text></svg>
        """;
    public async Task<(string Markdown, string Path)> CreateDiagramAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!notes.TryGetValue(id, out var note)) throw new KeyNotFoundException("메모를 찾지 못했습니다.");
        var name = $"{Guid.NewGuid():N}.drawio.svg";
        var path = Path.Combine(EnsureImageFolder(id), name);
        await File.WriteAllTextAsync(path + ".tmp", EmptyDiagram, cancellationToken);
        File.Move(path + ".tmp", path);
        var depth = note.RelativePath.Replace('\\', '/').Count(character => character == '/');
        return ($"![다이어그램]({string.Concat(Enumerable.Repeat("../", depth))}assets/{id:N}/{name})", path);
    }
    // file은 브리지가 "폴더/파일.drawio.svg" 형식으로 검사한 값입니다.
    public string DiagramPath(string file)
    {
        var path = Path.GetFullPath(Path.Combine(assetsRoot, file));
        StoragePathGuard.RejectLinks(path);
        return File.Exists(path) ? path : throw new FileNotFoundException("다이어그램 파일을 찾지 못했습니다.", file);
    }

    // 앱이 저장한 이미지 중 어떤 메모, 휴지통 메모, 복구 기록도 파일 이름을 적지 않은 것만 찾습니다. 사용자가 폴더에 넣은 파일은 건드리지 않습니다.
    public async Task<IReadOnlyList<UnusedImage>> FindUnusedImagesAsync(CancellationToken cancellationToken)
    {
        await FlushOpenNotesAsync(cancellationToken);
        var texts = notes.Values.Select(note => note.Markdown).ToList();
        foreach (var directory in new[] { trashRoot, Path.Combine(Root, ".hunmin", "recovery") })
            if (Directory.Exists(directory))
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) texts.Add(await File.ReadAllTextAsync(file, cancellationToken));
        var result = new List<UnusedImage>();
        if (!Directory.Exists(assetsRoot)) return result;
        foreach (var directory in Directory.EnumerateDirectories(assetsRoot))
        {
            if (!NoteAssetFolder.IsMatch(Path.GetFileName(directory)) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (!AppImage.IsMatch(name) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                if (!texts.Any(text => text.Contains(name, StringComparison.OrdinalIgnoreCase))) result.Add(new(file, new FileInfo(file).Length));
            }
        }
        return result;
    }
    // 확인 창을 보는 사이 다시 쓰게 된 이미지는 지우지 않습니다.
    public async Task<int> DeleteUnusedImagesAsync(IEnumerable<UnusedImage> chosen, Action<string> delete, CancellationToken cancellationToken)
    {
        var current = (await FindUnusedImagesAsync(cancellationToken)).Select(image => image.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var image in chosen)
        {
            if (!current.Contains(image.Path)) continue;
            try { delete(image.Path); count++; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 지우지 못한 파일은 다음 정리 때 다시 찾습니다. */ }
        }
        return count;
    }
    private static readonly System.Text.RegularExpressions.Regex NoteAssetFolder = new("^[0-9a-f]{32}$");
    private static readonly System.Text.RegularExpressions.Regex AppImage = new(@"^[0-9a-f]{32}\.(png|drawio\.svg)$");

    // 외부 Markdown 파일을 새 메모로 복사합니다. 원본은 바꾸지 않습니다. 보존할 수 없는 앞머리는 지우지 않고 본문에 그대로 둡니다.
    public async Task<Note> ImportMarkdownAsync(string path, CancellationToken cancellationToken)
    {
        const int maxFileBytes = 4 * 1024 * 1024, maxMessageBytes = 1_000_000;
        if (new FileInfo(path).Length > maxFileBytes) throw new InvalidDataException("파일이 너무 커서 메모로 열 수 없습니다.");
        var text = DecodeMarkdown(await File.ReadAllBytesAsync(path, cancellationToken));
        if (text.Contains('\0')) throw new InvalidDataException("텍스트 파일이 아닙니다.");
        var encoded = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(text, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (encoded.Length > maxMessageBytes) throw new InvalidDataException("파일이 너무 커서 메모로 열 수 없습니다.");
        var id = Guid.NewGuid();
        var relative = $"Inbox/{id:N}.md";
        var name = Path.GetFileNameWithoutExtension(path);
        var fallback = NoteTitle.Normalize(name.Length > 200 ? name[..200] : name);
        var now = DateTimeOffset.UtcNow;
        Note note;
        try
        {
            var read = codec.Import(text, fallback + ".md");
            note = read with { Id = id, RelativePath = relative, Title = NoteTitle.Normalize(read.Title) };
        }
        catch (Exception error) when (error is FormatException or ArgumentException) { note = new(id, relative, fallback, text, [], now, now); }
        await creation.WaitAsync(cancellationToken);
        try
        {
            await repository.SaveAsync(note, cancellationToken);
            notes[id] = note;
        }
        finally { creation.Release(); }
        try { await index.UpsertAsync(note, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException) { /* 다음 시작 때 다시 맞춥니다. */ }
        return note;
    }
    private static string DecodeMarkdown(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..]) return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes is [0xFE, 0xFF, ..]) return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new System.Text.UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (System.Text.DecoderFallbackException)
        {
            // UTF-8이 아니면 한글 Windows의 옛 메모장 형식으로 읽습니다.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    public void StartWatching()
    {
        var directory = Path.Combine(Root, "notes");
        Directory.CreateDirectory(directory);
        syncTimer = new Timer(_ => _ = RunExternalSyncAsync());
        watcher = new FileSystemWatcher(directory, "*.md") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName };
        watcher.Changed += (_, _) => QueueExternalSync();
        watcher.Created += (_, _) => QueueExternalSync();
        watcher.Deleted += (_, _) => QueueExternalSync();
        watcher.Renamed += (_, _) => QueueExternalSync();
        watcher.Error += (_, _) => QueueExternalSync();
        watcher.EnableRaisingEvents = true;
    }
    private void QueueExternalSync() { if (!disposing) syncTimer?.Change(500, Timeout.Infinite); }
    private async Task RunExternalSyncAsync()
    {
        try { await SyncExternalChangesAsync(CancellationToken.None); }
        catch (Exception error) when (!disposing) { ExternalSyncFailed?.Invoke(error.Message); }
        catch (Exception) { /* 종료 중에는 보고하지 않습니다. */ }
    }

    public async Task SyncExternalChangesAsync(CancellationToken cancellationToken)
    {
        await syncing.WaitAsync(cancellationToken);
        try
        {
            if (disposing) return;
            var disk = (await repository.ListAsync(cancellationToken)).ToDictionary(note => note.Id);
            // 처리 중 새로 만든 보존 사본이나 새 메모를 삭제된 파일로 착각하지 않게 먼저 정합니다.
            var missing = notes.Keys.Except(disk.Keys).ToArray();
            foreach (var note in disk.Values)
            {
                bool tombstone;
                lock (notesGate) tombstone = deleted.Contains(note.Id);
                if (tombstone) continue;
                if (Service.IsOpen(note.Id)) { await Service.ReconcileWithDiskAsync(note.Id, PreserveExternalAsync, cancellationToken); continue; }
                if (notes.TryGetValue(note.Id, out var known) && NoteFingerprint.Compute(known) == NoteFingerprint.Compute(note)) continue;
                notes[note.Id] = note;
                await index.UpsertAsync(note, cancellationToken);
            }
            foreach (var id in missing)
            {
                if (Service.IsOpen(id)) { await Service.ReconcileWithDiskAsync(id, PreserveExternalAsync, cancellationToken); continue; }
                if (await repository.GetAsync(id, cancellationToken) is not null) continue;
                notes.TryRemove(id, out _);
                await index.RemoveAsync(id, cancellationToken);
            }
        }
        finally { syncing.Release(); }
    }
    private async Task<string> PreserveExternalAsync(Note external, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var suffix = " (외부 변경)";
        var title = external.Title.Length + suffix.Length > 200 ? external.Title[..(200 - suffix.Length)] + suffix : external.Title + suffix;
        var copy = external with { Id = id, RelativePath = $"Inbox/{id:N}.md", Title = title, Updated = DateTimeOffset.UtcNow };
        await repository.SaveAsync(copy, cancellationToken);
        notes[id] = copy;
        return title;
    }

    public async ValueTask DisposeAsync()
    {
        disposing = true;
        watcher?.Dispose();
        syncTimer?.Dispose();
        await syncing.WaitAsync();
        syncing.Release();
        try { await Service.DisposeAsync(); }
        finally { lease.Dispose(); }
    }
}
