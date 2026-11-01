using HunminNotes.Core;

namespace HunminNotes.Storage.Tests;

public sealed class FileRecoveryJournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DevPostItJournalTests", Guid.NewGuid().ToString("N"));
    public FileRecoveryJournalTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task JournalPersistsLatestDraftAcrossInstancesAndCanBeExplicitlyRemoved()
    {
        var note = Note.Create("Inbox/메모.md", "한글", "원본", ["태그"]);
        var first = new RecoveryDraft(note with { Markdown = "초안\r\n" }, 1, NoteFingerprint.Compute(note));
        await new FileRecoveryJournal(root).WriteAsync(first, default);
        var read = await new FileRecoveryJournal(root).ReadAsync(note.Id, default);
        Assert.Equal("초안\r\n", read!.Note.Markdown);
        Assert.Equal(first.BaseFingerprint, read.BaseFingerprint);
        await new FileRecoveryJournal(root).WriteAsync(first with { Revision = 2, Note = note with { Markdown = "최종" } }, default);
        Assert.Equal(2, (await new FileRecoveryJournal(root).ReadAsync(note.Id, default))!.Revision);
        Assert.Single(Directory.EnumerateFiles(root));
        await new FileRecoveryJournal(root).RemoveAsync(note.Id, default);
        Assert.Null(await new FileRecoveryJournal(root).ReadAsync(note.Id, default));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task CorruptRecordIsExposedAndNotDeleted(string json)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(root, id.ToString("N") + ".json");
        await File.WriteAllTextAsync(path, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileRecoveryJournal(root).ReadAsync(id, default));
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }
    public void Dispose() => Directory.Delete(root, true);
}
