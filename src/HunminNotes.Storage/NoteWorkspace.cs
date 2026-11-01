using System.Collections.Concurrent;
using HunminNotes.Core;

namespace HunminNotes.Storage;

public sealed class NoteWorkspace : IAsyncDisposable
{
    private readonly DataDirectoryLease lease;
    private readonly MarkdownNoteRepository repository;
    private readonly ConcurrentDictionary<Guid, Note> notes = new();
    private readonly SemaphoreSlim creation = new(1, 1);
    public NoteService Service { get; }
    public IReadOnlyList<Note> Notes => notes.Values.OrderByDescending(note => note.Updated).ThenBy(note => note.Id).ToArray();

    private NoteWorkspace(string root)
    {
        lease = new DataDirectoryLease(root);
        try
        {
            repository = new MarkdownNoteRepository(Path.Combine(root, "notes"));
            Service = new NoteService(repository, new FileRecoveryJournal(Path.Combine(root, ".hunmin", "recovery")), new SqliteNoteIndex(Path.Combine(root, ".hunmin", "note-index.db")));
            Service.StateChanged += state => notes[state.Note.Id] = state.Note;
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
                var note = new Note(Guid.Parse("a8187628-1101-4030-8060-a48a3b704c60"), "Inbox/첫메모.md", "첫 메모", "# DevPostIt\n\n아이디어와 할 일을 Markdown으로 적어 보세요.\n\n> 변경 내용은 자동 저장됩니다.\n", [], now, now);
                await workspace.repository.SaveAsync(note, cancellationToken);
                workspace.notes[note.Id] = note;
            }
            return workspace;
        }
        catch { await workspace.DisposeAsync(); throw; }
    }
    public async Task<NoteSaveState> OpenNoteAsync(Guid id, CancellationToken cancellationToken)
    {
        var state = await Service.OpenAsync(id, cancellationToken);
        notes[id] = state.Note;
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
    public async ValueTask DisposeAsync()
    {
        try { await Service.DisposeAsync(); }
        finally { lease.Dispose(); }
    }
}
