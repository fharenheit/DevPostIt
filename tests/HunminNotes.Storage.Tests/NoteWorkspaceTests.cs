using HunminNotes.Core;
using HunminNotes.Storage;

namespace HunminNotes.Storage.Tests;

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
