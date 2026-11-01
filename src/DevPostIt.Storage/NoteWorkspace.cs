using System.Collections.Concurrent;
using DevPostIt.Core;

namespace DevPostIt.Storage;

public record TrashedNote(Guid Id, string Title, string RelativePath, DateTimeOffset TrashedAt);
public record NoteSearchResult(Note Note, string Excerpt);

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

    // 붙여넣은 이미지를 메모별 폴더에 저장하고, 메모 파일 기준 상대 경로의 Markdown을 돌려줍니다.
    public async Task<string> SaveImageAsync(Guid id, byte[] png, CancellationToken cancellationToken)
    {
        const int maxBytes = 20 * 1024 * 1024;
        if (png.Length is 0 or > maxBytes) throw new InvalidDataException("이미지는 20MB까지 붙여넣을 수 있습니다.");
        if (!notes.TryGetValue(id, out var note)) throw new KeyNotFoundException("메모를 찾지 못했습니다.");
        var name = $"{Guid.NewGuid():N}.png";
        var directory = Path.Combine(assetsRoot, id.ToString("N"));
        StoragePathGuard.RejectLinks(directory);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, name + ".tmp");
        await File.WriteAllBytesAsync(temporary, png, cancellationToken);
        File.Move(temporary, Path.Combine(directory, name));
        var depth = note.RelativePath.Replace('\\', '/').Count(character => character == '/');
        return $"![이미지]({string.Concat(Enumerable.Repeat("../", depth))}assets/{id:N}/{name})";
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
