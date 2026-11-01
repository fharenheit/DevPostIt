using DevPostIt.Core;
using DevPostIt.Storage;

namespace DevPostIt.Storage.Tests;

public sealed class NoteWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-workspace-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task MultipleNotesShareLeaseAndSaveIndependentlyAcrossRestart()
    {
        Guid firstId, secondId;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default))
        {
            firstId = workspace.Notes.Single().Id;
            secondId = (await workspace.CreateAsync(default)).Id;
            Assert.NotEqual(firstId, secondId);
            await workspace.OpenNoteAsync(firstId, default);
            await workspace.Service.ApplyChangeAsync(new(firstId, 1, "첫 본문", "첫 이름"), default);
            await workspace.Service.ApplyChangeAsync(new(secondId, 1, "둘 본문", "둘 이름"), default);
            await workspace.Service.FlushAsync(firstId, default);
            await workspace.Service.FlushAsync(secondId, default);
            Assert.Throws<IOException>(() => new DataDirectoryLease(root));
            Assert.Equal(2, workspace.Notes.Count);
        }
        await using var restarted = await NoteWorkspace.OpenAsync(root, default);
        Assert.Equal("첫 이름", (await restarted.OpenNoteAsync(firstId, default)).Note.Title);
        Assert.Equal("둘 본문", (await restarted.OpenNoteAsync(secondId, default)).Note.Markdown);
        Assert.Equal(2, restarted.Notes.Count);
        Assert.Equal(2, (await new MarkdownNoteRepository(Path.Combine(root, "notes")).ListAsync(default)).Count);
    }

    [Fact]
    public async Task AcceptedDraftRemainsRecoverableAfterWorkspaceDisposal()
    {
        Guid id;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default))
        {
            id = workspace.Notes.Single().Id;
            await workspace.OpenNoteAsync(id, default);
            await workspace.Service.ApplyChangeAsync(new(id, 1, "복구 본문", "복구 이름"), default);
        }
        await using var restarted = await NoteWorkspace.OpenAsync(root, default);
        var state = await restarted.OpenNoteAsync(id, default);
        Assert.True(state.RecoveryPending);
        Assert.Equal("복구 이름", state.Note.Title);
        Assert.Equal("복구 본문", state.Note.Markdown);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public async Task DeletedLastNoteIdIsNotReusedByNextStartup()
    {
        Guid id;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default))
        {
            id = workspace.Notes.Single().Id;
            await workspace.DeleteAsync(id, default);
            Assert.Empty(workspace.Notes);
        }
        await using var next = await NoteWorkspace.OpenAsync(root, default);
        Assert.NotEqual(id, next.Notes.Single().Id);
    }

    [Fact]
    public async Task PermanentDeleteRemovesOnlyChosenFileDraftAndSearchEntry()
    {
        Guid deletedId, keptId;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default))
        {
            deletedId = workspace.Notes.Single().Id;
            keptId = (await workspace.CreateAsync(default)).Id;
            await workspace.OpenNoteAsync(deletedId, default);
            await workspace.Service.ApplyChangeAsync(new(deletedId, 1, "삭제 검색어", "삭제 대상"), default);
            await workspace.Service.FlushAsync(deletedId, default);
            await workspace.Service.ApplyChangeAsync(new(deletedId, 2, "마지막 초안"), default);
            await workspace.DeleteAsync(deletedId, default);
            Assert.DoesNotContain(workspace.Notes, note => note.Id == deletedId);
            var index = new SqliteNoteIndex(Path.Combine(root, ".hunmin", "note-index.db"));
            Assert.Empty(await index.SearchAsync("삭제 검색어", 10, default));
            Assert.Null(await new FileRecoveryJournal(Path.Combine(root, ".hunmin", "recovery")).ReadAsync(deletedId, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => workspace.Service.ApplyChangeAsync(new(deletedId, 3, "되살리기"), default));
            Assert.Equal(keptId, workspace.Notes.Single().Id);
        }
        await using var restarted = await NoteWorkspace.OpenAsync(root, default);
        Assert.Equal(keptId, restarted.Notes.Single().Id);
    }

    [Fact]
    public async Task LockedFileDeleteFailureKeepsNoteAndRecoveryAndCanRetry()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new(note.Id, 1, "남겨야 할 초안"), default);
        var full = Path.Combine(root, "notes", note.RelativePath);
        using (var locked = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => workspace.DeleteAsync(note.Id, default));
        Assert.Single(workspace.Notes);
        Assert.NotNull(await new FileRecoveryJournal(Path.Combine(root, ".hunmin", "recovery")).ReadAsync(note.Id, default));
        await workspace.DeleteAsync(note.Id, default);
        Assert.Empty(workspace.Notes);
        Assert.False(File.Exists(full));
    }

    [Fact]
    public async Task FailedDeleteDoesNotEraseRecoveryConflictFingerprint()
    {
        Note saved;
        await using (var first = await NoteWorkspace.OpenAsync(root, default))
        {
            saved = first.Notes.Single();
            await first.OpenNoteAsync(saved.Id, default);
            await first.Service.ApplyChangeAsync(new(saved.Id, 1, "복구 본문"), default);
        }
        var journal = new FileRecoveryJournal(Path.Combine(root, ".hunmin", "recovery"));
        var original = (await journal.ReadAsync(saved.Id, default))!;
        var repository = new MarkdownNoteRepository(Path.Combine(root, "notes"));
        await repository.SaveAsync(saved with { Markdown = "외부 변경" }, default);
        await using (var second = await NoteWorkspace.OpenAsync(root, default))
        {
            await second.OpenNoteAsync(saved.Id, default);
            using var locked = new FileStream(Path.Combine(root, "notes", saved.RelativePath), FileMode.Open, FileAccess.Read, FileShare.Read);
            await Assert.ThrowsAsync<IOException>(() => second.DeleteAsync(saved.Id, default));
        }
        var preserved = (await journal.ReadAsync(saved.Id, default))!;
        Assert.Equal(original.BaseFingerprint, preserved.BaseFingerprint);
        Assert.Equal(original.Revision, preserved.Revision);
        Assert.Equal(original.Note.Markdown, preserved.Note.Markdown);
        await using var last = await NoteWorkspace.OpenAsync(root, default);
        await last.OpenNoteAsync(saved.Id, default);
        await Assert.ThrowsAsync<NoteSaveConflictException>(() => last.Service.ResolveRecoveryAsync(saved.Id, true, default));
        Assert.Equal("외부 변경", (await repository.GetAsync(saved.Id, default))!.Markdown);
    }

    [Fact]
    public async Task DiscardedRecoveryTitleIsRemovedFromList()
    {
        Guid id;
        await using (var first = await NoteWorkspace.OpenAsync(root, default))
        {
            id = first.Notes.Single().Id;
            await first.OpenNoteAsync(id, default);
            await first.Service.ApplyChangeAsync(new(id, 1, "초안", "버릴 이름"), default);
        }
        await using var restarted = await NoteWorkspace.OpenAsync(root, default);
        await restarted.OpenNoteAsync(id, default);
        await restarted.Service.ResolveRecoveryAsync(id, false, default);
        await restarted.Service.FlushAsync(id, default);
        Assert.Equal("첫 메모", restarted.Notes.Single().Title);
    }
}
