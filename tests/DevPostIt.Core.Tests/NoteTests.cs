using DevPostIt.Core;

namespace DevPostIt.Core.Tests;

public class NoteTests
{
    [Fact]
    public void NewNotesHaveDistinctStableIds()
    {
        var first = Note.Create("Inbox/첫째.md", "첫째", "본문", []);
        var second = Note.Create("Inbox/둘째.md", "둘째", "본문", []);

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Id, (first with { Markdown = "수정한 본문" }).Id);
    }

    [Fact]
    public void TitleAndTagsRoundTripInDomain()
    {
        var id = Guid.NewGuid();
        var created = DateTimeOffset.Parse("2026-11-01T09:00:00+09:00");
        var updated = created.AddMinutes(1);
        var note = new Note(id, "Inbox/회의.md", "한글 회의", "# 본문",
            ["업무", "회의"], created, updated);

        var (actualId, path, title, markdown, tags, actualCreated, actualUpdated) = note;
        Assert.Equal(id, actualId);
        Assert.Equal("Inbox/회의.md", path);
        Assert.Equal("한글 회의", title);
        Assert.Equal("# 본문", markdown);
        Assert.Equal(new[] { "업무", "회의" }, tags);
        Assert.Equal(created, actualCreated);
        Assert.Equal(updated, actualUpdated);
    }

    [Fact]
    public void EmptyBodyIsAllowed()
    {
        var note = Note.Create("Inbox/빈메모.md", "빈 메모", "", []);

        Assert.Equal("", note.Markdown);
        Assert.Empty(note.Tags);
        Assert.Equal(note.Created, note.Updated);
    }
}
