using DevPostIt.Core;
using DevPostIt.Storage;
using Xunit;

namespace DevPostIt.Storage.Tests;

public sealed class NoteRenameTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-rename-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private string Full(string relative) => Path.Combine(root, "notes", relative);

    private static async Task SaveAsync(NoteWorkspace workspace, Guid id, string markdown, string? title = null)
    {
        var state = await workspace.OpenNoteAsync(id, default);
        await workspace.Service.ApplyChangeAsync(new NoteChange(id, state.Revision + 1, markdown, title), default);
        await workspace.Service.FlushAsync(id, default);
    }

    [Fact]
    public async Task RenameMovesTheFileAndKeepsIdAndContent()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await SaveAsync(workspace, note.Id, "본문");
        Assert.Equal("Inbox/첫메모.md", note.RelativePath);

        var state = await workspace.RenameFileAsync(note.Id, "북마크", default);

        Assert.Equal("Inbox/북마크.md", state.Note.RelativePath);
        Assert.False(File.Exists(Full("Inbox/첫메모.md")));
        Assert.True(File.Exists(Full("Inbox/북마크.md")));
        await SaveAsync(workspace, note.Id, "본문", "북마크");
        var saved = (await workspace.Service.GetStateAsync(note.Id, default)).Note;
        Assert.Equal((note.Id, "북마크", "본문", "Inbox/북마크.md"), (saved.Id, saved.Title, saved.Markdown, saved.RelativePath));
        Assert.Equal("Inbox/북마크.md", workspace.Notes.Single().RelativePath);
    }

    [Fact]
    public async Task RenamedNoteIsFoundAfterReopening()
    {
        Guid id;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default))
        {
            id = workspace.Notes.Single().Id;
            await workspace.RenameFileAsync(id, "북마크", default);
            await SaveAsync(workspace, id, "유지", "북마크");
        }
        await using var reopened = await NoteWorkspace.OpenAsync(root, default);
        var note = reopened.Notes.Single();
        Assert.Equal((id, "북마크", "유지", "Inbox/북마크.md"), (note.Id, note.Title, note.Markdown, note.RelativePath));
    }

    [Theory]
    [InlineData("다른 메모")]
    [InlineData("  다른 메모  ")]
    [InlineData("ABC")]
    public async Task DuplicateTitleIsRejectedAndNothingMoves(string title)
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var first = workspace.Notes.Single();
        await workspace.OpenNoteAsync(first.Id, default);
        var other = await workspace.CreateAsync(default);
        await SaveAsync(workspace, other.Id, "", title.Trim() == "ABC" ? "abc" : "다른 메모");

        var error = await Assert.ThrowsAsync<NoteNameConflictException>(() => workspace.RenameFileAsync(first.Id, title, default));

        Assert.Contains("같은 이름", error.Message);
        Assert.True(File.Exists(Full(first.RelativePath)));
        Assert.Equal(first.RelativePath, (await workspace.Service.GetStateAsync(first.Id, default)).Note.RelativePath);
    }

    [Fact]
    public async Task DifferentTitlesWithTheSameFileNameAreRejected()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var first = workspace.Notes.Single();
        await workspace.OpenNoteAsync(first.Id, default);
        var other = await workspace.CreateAsync(default);
        await workspace.RenameFileAsync(other.Id, "a:b", default);
        await SaveAsync(workspace, other.Id, "", "a:b");

        var error = await Assert.ThrowsAsync<NoteNameConflictException>(() => workspace.RenameFileAsync(first.Id, "a/b", default));

        Assert.Contains("a_b.md", error.Message);
        Assert.True(File.Exists(Full(first.RelativePath)));
    }

    [Fact]
    public async Task FileThatIsNotANoteIsNotOverwritten()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await File.WriteAllTextAsync(Full("Inbox/보존.md"), "앱 밖에서 만든 파일");

        await Assert.ThrowsAsync<NoteNameConflictException>(() => workspace.RenameFileAsync(note.Id, "보존", default));

        Assert.Equal("앱 밖에서 만든 파일", await File.ReadAllTextAsync(Full("Inbox/보존.md")));
        Assert.True(File.Exists(Full(note.RelativePath)));
    }

    [Theory]
    [InlineData("a/b:c*d?", "a_b_c_d_.md")]
    [InlineData("끝에 점과 공백. .", "끝에 점과 공백.md")]
    [InlineData("CON", "_CON.md")]
    [InlineData("nul.txt", "_nul.txt.md")]
    [InlineData("..", null)]
    public async Task UnsafeCharactersInFileNameAreReplaced(string title, string? expected)
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);

        var state = await workspace.RenameFileAsync(note.Id, title, default);

        Assert.Equal("Inbox/" + (expected ?? $"{note.Id:N}.md"), state.Note.RelativePath);
        Assert.True(File.Exists(Full(state.Note.RelativePath)));
    }

    [Fact]
    public async Task LongTitleUsesAShortenedFileName()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);

        var state = await workspace.RenameFileAsync(note.Id, new string('가', 200), default);

        Assert.Equal("Inbox/" + new string('가', 100) + ".md", state.Note.RelativePath);
    }

    [Fact]
    public async Task SameTitleMovesAnOldFileNameToMatch()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await SaveAsync(workspace, note.Id, "본문", "북마크");
        Assert.True(File.Exists(Full("Inbox/첫메모.md")));

        var state = await workspace.RenameFileAsync(note.Id, "북마크", default);

        Assert.Equal("Inbox/북마크.md", state.Note.RelativePath);
        Assert.False(File.Exists(Full("Inbox/첫메모.md")));
    }

    [Fact]
    public async Task ChangingOnlyTheCaseRenamesTheFile()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.RenameFileAsync(note.Id, "memo", default);
        await SaveAsync(workspace, note.Id, "", "memo");

        var state = await workspace.RenameFileAsync(note.Id, "MEMO", default);

        Assert.Equal("Inbox/MEMO.md", state.Note.RelativePath);
        Assert.Contains("MEMO.md", Directory.GetFiles(Full("Inbox")).Select(path => Path.GetFileName(path)));
    }

    [Fact]
    public async Task UnsavedDraftSurvivesRenameAndSavesToTheNewFile()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        var state = await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new NoteChange(note.Id, state.Revision + 1, "저장 전 입력"), default);

        await workspace.RenameFileAsync(note.Id, "북마크", default);
        await workspace.Service.FlushAsync(note.Id, default);

        Assert.Contains("저장 전 입력", await File.ReadAllTextAsync(Full("Inbox/북마크.md")));
        Assert.False(File.Exists(Full("Inbox/첫메모.md")));
    }

    [Fact]
    public async Task RecoveryAfterRenameStillMatchesTheMovedFile()
    {
        Guid id;
        await using (var workspace = await NoteWorkspace.OpenAsync(root, default))
        {
            id = workspace.Notes.Single().Id;
            var state = await workspace.OpenNoteAsync(id, default);
            await workspace.Service.ApplyChangeAsync(new NoteChange(id, state.Revision + 1, "복구할 입력"), default);
            await workspace.RenameFileAsync(id, "북마크", default);
            var journal = new FileRecoveryJournal(Path.Combine(root, ".hunmin", "recovery"));
            var recovered = await journal.ReadAsync(id, default);
            Assert.Equal("Inbox/북마크.md", recovered!.Note.RelativePath);
            var disk = await new MarkdownNoteRepository(Path.Combine(root, "notes")).GetAsync(id, default);
            Assert.Equal(NoteFingerprint.Compute(disk!), recovered.BaseFingerprint);
        }
    }

    [Fact]
    public async Task ExternalSyncAfterRenameDoesNotCreateAConflictCopy()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        var state = await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new NoteChange(note.Id, state.Revision + 1, "입력 중"), default);

        await workspace.RenameFileAsync(note.Id, "북마크", default);
        await workspace.SyncExternalChangesAsync(default);

        Assert.Single(workspace.Notes);
        Assert.Equal(NoteSaveStatus.Pending, (await workspace.Service.GetStateAsync(note.Id, default)).Status);
    }

    [Fact]
    public async Task FileChangedOutsideTheAppIsNotMoved()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        await workspace.OpenNoteAsync(note.Id, default);
        await File.AppendAllTextAsync(Full(note.RelativePath), "\n앱 밖 변경");

        await Assert.ThrowsAsync<NoteSaveConflictException>(() => workspace.RenameFileAsync(note.Id, "북마크", default));

        Assert.True(File.Exists(Full(note.RelativePath)));
        Assert.False(File.Exists(Full("Inbox/북마크.md")));
    }

    [Fact]
    public async Task TrashedNoteTitleCanBeReusedAndRestoreKeepsBothNotes()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var first = workspace.Notes.Single();
        await workspace.OpenNoteAsync(first.Id, default);
        var other = await workspace.CreateAsync(default);
        await workspace.RenameFileAsync(other.Id, "같은 이름", default);
        await SaveAsync(workspace, other.Id, "", "같은 이름");
        await workspace.TrashAsync(other.Id, default);

        var state = await workspace.RenameFileAsync(first.Id, "같은 이름", default);
        Assert.Equal("Inbox/같은 이름.md", state.Note.RelativePath);

        var restored = await workspace.RestoreAsync(other.Id, default);
        Assert.Equal($"Inbox/{other.Id:N}.md", restored.RelativePath);
        Assert.Equal(2, workspace.Notes.Count);
    }
}
