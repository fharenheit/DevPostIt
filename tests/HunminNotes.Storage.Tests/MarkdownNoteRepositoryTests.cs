using HunminNotes.Core;

namespace HunminNotes.Storage.Tests;

public class MarkdownNoteRepositoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HunminNotesTests", Guid.NewGuid().ToString("N"));
    private readonly FrontmatterCodec codec = new();

    public MarkdownNoteRepositoryTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task SavesLoadsAndUpdatesAcrossRepositoryInstances()
    {
        var note = Note.Create("Inbox\\회의.md", "회의", "한글 본문\r\n", ["업무"]);
        await new MarkdownNoteRepository(root).SaveAsync(note, default);
        var repository = new MarkdownNoteRepository(root);
        var loaded = await repository.GetAsync(note.Id, default);

        Assert.NotNull(loaded);
        Assert.Equal("Inbox/회의.md", loaded.RelativePath);
        Assert.Equal(note.Markdown, loaded.Markdown);
        Assert.Equal(note.Title, loaded.Title);
        Assert.Equal(note.Tags, loaded.Tags);
        await repository.SaveAsync(loaded with { Markdown = "수정" }, default);
        Assert.Equal("수정", (await repository.GetAsync(note.Id, default))!.Markdown);
        Assert.Null(await repository.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task DuplicateIdCannotOverwriteExistingNote()
    {
        var repository = new MarkdownNoteRepository(root);
        var note = Note.Create("first.md", "첫째", "원본", []);
        await repository.SaveAsync(note, default);
        var original = await File.ReadAllTextAsync(Path.Combine(root, "first.md"));

        await Assert.ThrowsAsync<DuplicateNoteIdException>(() => repository.SaveAsync(note with { RelativePath = "copy.md", Markdown = "덮어쓰기" }, default));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(root, "first.md")));
        Assert.False(File.Exists(Path.Combine(root, "copy.md")));

        await File.WriteAllTextAsync(Path.Combine(root, "copy.md"), codec.Serialize(note));
        await Assert.ThrowsAsync<DuplicateNoteIdException>(() => repository.GetAsync(note.Id, default));
        await Assert.ThrowsAsync<DuplicateNoteIdException>(() => repository.SaveAsync(note with { Markdown = "수정" }, default));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(root, "first.md")));
    }

    [Fact]
    public async Task ImportCannotOverwriteExistingNoteWithSameIdAndPath()
    {
        var repository = new MarkdownNoteRepository(root);
        var original = Note.Create("note.md", "원본", "원본 본문", []);
        await repository.SaveAsync(original, default);
        var originalBytes = await File.ReadAllBytesAsync(Path.Combine(root, "note.md"));

        await Assert.ThrowsAsync<IOException>(() => repository.ImportAsync(
            codec.Serialize(original with { Markdown = "가져온 다른 본문" }), "note.md", default));

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Path.Combine(root, "note.md")));
    }

    [Fact]
    public async Task DifferentIdCannotOverwriteOccupiedPath()
    {
        var repository = new MarkdownNoteRepository(root);
        var first = Note.Create("same.md", "첫째", "원본", []);
        await repository.SaveAsync(first, default);
        await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(Note.Create("same.md", "둘째", "수정", []), default));
        Assert.Equal("원본", (await repository.GetAsync(first.Id, default))!.Markdown);
    }

    [Fact]
    public async Task ExplicitImportPersistsIdButOrdinaryReadDoesNotModifyUnmanagedFiles()
    {
        var repository = new MarkdownNoteRepository(root);
        var path = Path.Combine(root, "plain.md");
        await File.WriteAllTextAsync(path, "# 외부 파일\r\n");
        Assert.Null(await repository.GetAsync(Guid.NewGuid(), default));
        Assert.Equal("# 외부 파일\r\n", await File.ReadAllTextAsync(path));
        var imported = await repository.ImportAsync("# 가져온 본문", "import.md", default);
        Assert.Equal(imported.Id, (await new MarkdownNoteRepository(root).GetAsync(imported.Id, default))!.Id);
        await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(imported with { RelativePath = "plain.md" }, default));
        Assert.Equal("# 외부 파일\r\n", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("folder/../../escape.md")]
    [InlineData("folder\\..\\escape.md")]
    [InlineData("C:\\escape.md")]
    [InlineData("/escape.md")]
    [InlineData("\\\\server\\share\\escape.md")]
    [InlineData("note.md:stream")]
    [InlineData("folder./note.md")]
    [InlineData("CON.md")]
    [InlineData("note.txt")]
    public async Task RejectsUnsafePathsBeforeWriting(string path)
    {
        var repository = new MarkdownNoteRepository(root);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(Note.Create(path, "제목", "본문", []), default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public async Task RejectsDirectoryJunctionOutsideRoot()
    {
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var notes = Directory.CreateDirectory(Path.Combine(root, "notes")).FullName;
        var junction = Path.Combine(notes, "link");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction);
        start.ArgumentList.Add(outside);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => new MarkdownNoteRepository(notes).SaveAsync(Note.Create("link/escape.md", "제목", "본문", []), default));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public async Task CancellationDoesNotWriteAnything()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MarkdownNoteRepository(root).SaveAsync(Note.Create("note.md", "제목", "본문", []), cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    public void Dispose() => Directory.Delete(root, true);
}
