using System.Text;
using DevPostIt.Storage;
using Xunit;

namespace DevPostIt.Storage.Tests;

public sealed class NoteMediaTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-media-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static string FileOf(string markdown) => markdown[(markdown.LastIndexOf('/') + 1)..^1];
    private static string Folder(NoteWorkspace workspace, Guid id) => Path.Combine(workspace.AssetsRoot, id.ToString("N"));

    [Fact]
    public async Task UnusedImagesAreOnlyAppImagesNoNoteOrTrashedNoteRefersTo()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        var used = await workspace.SaveImageAsync(note.Id, [1], default);
        var removed = await workspace.SaveImageAsync(note.Id, [2], default);
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new Core.NoteChange(note.Id, 100, used, null), default);
        var mine = Path.Combine(Folder(workspace, note.Id), "내가 넣은 파일.png");
        await File.WriteAllBytesAsync(mine, [3]);
        var unknown = Path.Combine(workspace.AssetsRoot, "다른 폴더");
        Directory.CreateDirectory(unknown);
        await File.WriteAllBytesAsync(Path.Combine(unknown, new string('a', 32) + ".png"), [4]);

        var unused = await workspace.FindUnusedImagesAsync(default);
        var only = Assert.Single(unused);
        Assert.Equal(Path.Combine(Folder(workspace, note.Id), FileOf(removed)), only.Path);
        Assert.Equal(1, only.Size);
    }

    [Fact]
    public async Task ImageUsedByAnotherNoteOrTrashedNoteStays()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var first = workspace.Notes.Single();
        var second = await workspace.CreateAsync(default);
        var third = await workspace.CreateAsync(default);
        var shared = await workspace.SaveImageAsync(first.Id, [1], default);
        var trashed = await workspace.SaveImageAsync(third.Id, [2], default);
        await workspace.OpenNoteAsync(second.Id, default);
        await workspace.Service.ApplyChangeAsync(new Core.NoteChange(second.Id, 100, shared, null), default);
        await workspace.OpenNoteAsync(third.Id, default);
        await workspace.Service.ApplyChangeAsync(new Core.NoteChange(third.Id, 100, trashed, null), default);
        await workspace.TrashAsync(third.Id, default);
        Assert.Empty(await workspace.FindUnusedImagesAsync(default));
    }

    [Fact]
    public async Task DeletingUnusedImagesRechecksBeforeRemoving()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var note = workspace.Notes.Single();
        var markdown = await workspace.SaveImageAsync(note.Id, [1, 2], default);
        var other = await workspace.SaveImageAsync(note.Id, [3], default);
        var unused = await workspace.FindUnusedImagesAsync(default);
        Assert.Equal(2, unused.Count);
        // 확인 창을 보는 사이 한 이미지를 다시 썼습니다.
        await workspace.OpenNoteAsync(note.Id, default);
        await workspace.Service.ApplyChangeAsync(new Core.NoteChange(note.Id, 100, markdown, null), default);
        var deleted = new List<string>();
        var count = await workspace.DeleteUnusedImagesAsync(unused, deleted.Add, default);
        Assert.Equal(1, count);
        Assert.Equal(Path.Combine(Folder(workspace, note.Id), FileOf(other)), Assert.Single(deleted));
    }

    [Fact]
    public async Task DeletingRefusesFilesThatWereNotFoundAsUnused()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(root, default);
        var outside = Path.Combine(root, "바깥.png");
        await File.WriteAllBytesAsync(outside, [1]);
        var deleted = new List<string>();
        Assert.Equal(0, await workspace.DeleteUnusedImagesAsync([new UnusedImage(outside, 1)], deleted.Add, default));
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task ImportedMarkdownBecomesNewNoteAndLeavesSourceUntouched()
    {
        var source = Path.Combine(root, "바깥", "회의 기록.md");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, "# 회의\n\n내용\r\n");
        var before = await File.ReadAllBytesAsync(source);
        await using var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        var note = await workspace.ImportMarkdownAsync(source, default);
        Assert.Equal("회의 기록", note.Title);
        Assert.Equal("# 회의\n\n내용\r\n", note.Markdown);
        Assert.Contains(workspace.Notes, item => item.Id == note.Id);
        Assert.Equal(before, await File.ReadAllBytesAsync(source));
        Assert.Single(await workspace.SearchAsync("내용", 10, default), item => item.Note.Id == note.Id);
    }

    [Fact]
    public async Task ImportedNoteSurvivesReopening()
    {
        var source = Path.Combine(root, "기록.md");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, "본문");
        Guid id;
        await using (var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default))
            id = (await workspace.ImportMarkdownAsync(source, default)).Id;
        await using var reopened = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        Assert.Equal("본문", reopened.Notes.Single(note => note.Id == id).Markdown);
    }

    [Fact]
    public async Task ImportKeepsKnownFrontmatterButAlwaysAssignsNewId()
    {
        var existing = Guid.NewGuid();
        var source = Path.Combine(root, "파일.md");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, $"---\nid: \"{existing}\"\ntitle: \"앞머리 제목\"\ntags: [\"가\", \"나\"]\n---\n본문");
        await using var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        var first = await workspace.ImportMarkdownAsync(source, default);
        var second = await workspace.ImportMarkdownAsync(source, default);
        Assert.NotEqual(existing, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("앞머리 제목", first.Title);
        Assert.Equal(["가", "나"], first.Tags);
        Assert.Equal("본문", first.Markdown);
    }

    [Theory]
    [InlineData("---\ndate: 2024-01-01\ndraft: true\n---\n본문")]
    [InlineData("---\n닫히지 않은 앞머리\n본문")]
    [InlineData("---\ntitle: [잘못\n---\n본문")]
    public async Task ImportKeepsWholeTextWhenFrontmatterCannotBeKept(string text)
    {
        var source = Path.Combine(root, "다른 앱.md");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, text);
        await using var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        var note = await workspace.ImportMarkdownAsync(source, default);
        Assert.Equal("다른 앱", note.Title);
        Assert.Equal(text, note.Markdown);
    }

    [Fact]
    public async Task ImportReadsKoreanLegacyEncodingAndDropsBom()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(root);
        var legacy = Path.Combine(root, "옛날.md");
        await File.WriteAllBytesAsync(legacy, Encoding.GetEncoding(949).GetBytes("# 한글 문서"));
        var bom = Path.Combine(root, "표시.md");
        await File.WriteAllBytesAsync(bom, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("본문")]);
        await using var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        Assert.Equal("# 한글 문서", (await workspace.ImportMarkdownAsync(legacy, default)).Markdown);
        Assert.Equal("본문", (await workspace.ImportMarkdownAsync(bom, default)).Markdown);
    }

    [Fact]
    public async Task ImportRejectsBinaryFiles()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "그림.md");
        await File.WriteAllBytesAsync(source, [0x89, 0x50, 0x00, 0x00, 0x41]);
        await using var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.ImportMarkdownAsync(source, default));
    }

    [Fact]
    public async Task ImportRejectsFilesTooLargeForTheEditorAndLeavesNoNote()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "큼.md");
        await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)'a', 2 * 1024 * 1024).ToArray());
        await using var workspace = await NoteWorkspace.OpenAsync(Path.Combine(root, "data"), default);
        var count = workspace.Notes.Count;
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.ImportMarkdownAsync(source, default));
        Assert.Equal(count, workspace.Notes.Count);
    }
}
