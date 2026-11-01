using HunminNotes.Core;

namespace HunminNotes.Storage.Tests;

public class FrontmatterCodecTests
{
    private readonly FrontmatterCodec codec = new();

    [Theory]
    [InlineData("한글 회의", "# 본문\n\n- [ ] 할 일\n")]
    [InlineData("콜론: # 태그 '인용' \"큰따옴표\"\n다음 줄", "\r\n<div>그대로</div>\r\n---\r\n")]
    [InlineData("빈 메모", "")]
    public void MetadataAndBodyRoundTrip(string title, string body)
    {
        var note = new Note(Guid.NewGuid(), "Inbox/회의.md", title, body,
            ["업무", "a: b", "#태그", "따옴표\""],
            DateTimeOffset.Parse("2026-11-01T09:00:00+09:00"),
            DateTimeOffset.Parse("2026-11-01T10:00:00+09:00"));

        var restored = codec.Parse(codec.Serialize(note), note.RelativePath);

        Assert.Equal(note.Id, restored.Id);
        Assert.Equal(note.Title, restored.Title);
        Assert.Equal(note.Tags, restored.Tags);
        Assert.Equal(note.Created, restored.Created);
        Assert.Equal(note.Updated, restored.Updated);
        Assert.Equal(body, restored.Markdown);
    }

    [Fact]
    public void ReadsQuotedYamlAndCrLfWithoutChangingBody()
    {
        var raw = "---\r\nid: 018f5df0-7e6c-7ad0-a8c1-9b67490da11b\r\n" +
            "title: '한글: # ''회의'''\r\ncreated: 2026-11-01T09:00:00+09:00\r\n" +
            "updated: 2026-11-01T09:45:00+09:00\r\ntags: [업무, 'a: b']\r\n---\r\n\r\n# 본문\r\n";

        var note = codec.Parse(raw, "Inbox/회의.md");

        Assert.Equal("한글: # '회의'", note.Title);
        Assert.Equal(new[] { "업무", "a: b" }, note.Tags);
        Assert.Equal("\r\n# 본문\r\n", note.Markdown);
        Assert.Equal(TimeSpan.FromHours(9), note.Created.Offset);
    }

    [Theory]
    [InlineData("# 일반 Markdown\r\n")]
    [InlineData("---\ntitle: 가져오기\ntags: [한글]\n---\n본문")]
    public void MissingIdIsAssignedOnlyByExplicitImport(string raw)
    {
        Assert.Throws<MissingNoteIdException>(() => codec.Parse(raw, "Inbox/가져오기.md"));
        var imported = codec.Import(raw, "Inbox/가져오기.md");

        Assert.NotEqual(Guid.Empty, imported.Id);
        Assert.Equal(imported.Id, codec.Parse(codec.Serialize(imported), imported.RelativePath).Id);
        Assert.Equal(raw.StartsWith("---") ? "본문" : raw, imported.Markdown);
    }

    [Theory]
    [InlineData("id: 틀린값")]
    [InlineData("id: 00000000-0000-0000-0000-000000000000")]
    [InlineData("title: [잘못된, 제목]")]
    [InlineData("created: 어제")]
    [InlineData("tags: 잘못된태그")]
    [InlineData("extra: 보존할수없는필드")]
    [InlineData("title: 첫째\ntitle: 둘째")]
    public void InvalidMetadataIsNotSilentlyImported(string metadata)
    {
        Assert.Throws<FormatException>(() => codec.Import($"---\n{metadata}\n---\n본문", "note.md"));
    }

    [Fact]
    public void UnclosedFrontmatterIsRejected()
    {
        Assert.Throws<FormatException>(() => codec.Import("---\ntitle: 미완성\n본문", "note.md"));
    }
}
