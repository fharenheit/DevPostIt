namespace DevPostIt.Storage.Tests;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DevPostItAtomicTests", Guid.NewGuid().ToString("N"));
    public AtomicFileWriterTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task NewAndExistingFileUseUtf8AndLeaveNoTemporaryFiles()
    {
        var path = Path.Combine(root, "note.md");
        var writer = new AtomicFileWriter();
        await writer.WriteAsync(path, "첫 메모\r\n", default);
        Assert.Equal("첫 메모\r\n"u8.ToArray(), await File.ReadAllBytesAsync(path));
        await writer.WriteAsync(path, "교체한 본문\n", default);
        Assert.Equal("교체한 본문\n", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task FailedReplacePreservesPreviousBytesAndCleansTemporaryFile()
    {
        var path = Path.Combine(root, "locked.md");
        await File.WriteAllTextAsync(path, "이전 유효 파일");
        var before = await File.ReadAllBytesAsync(path);
        using (new HeldFile(path))
            await HeldFile.AssertFailsAsync(() => new AtomicFileWriter().WriteAsync(path, "새 본문", default));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task CreateOnlyAndCancelledWriteNeverReplaceExistingFile()
    {
        var path = Path.Combine(root, "existing.md");
        await File.WriteAllTextAsync(path, "기존 본문");
        await Assert.ThrowsAnyAsync<IOException>(() => new AtomicFileWriter().WriteNewAsync(path, "덮어쓰기", default));
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AtomicFileWriter().WriteAsync(path, "취소", source.Token));
        Assert.Equal("기존 본문", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.EnumerateFiles(root));
    }

    [Fact]
    public async Task RepositoryReplacementKeepsExistingReadersOnPreviousCompleteFile()
    {
        var repository = new MarkdownNoteRepository(root);
        var note = DevPostIt.Core.Note.Create("readable.md", "제목", "이전 본문", []);
        await repository.SaveAsync(note, default);
        await using var old = new FileStream(Path.Combine(root, "readable.md"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await repository.SaveAsync(note with { Markdown = "새로운 본문" }, default);
        using var reader = new StreamReader(old);
        Assert.Equal("이전 본문", new FrontmatterCodec().Parse(await reader.ReadToEndAsync(), "readable.md").Markdown);
        Assert.Equal("새로운 본문", (await repository.GetAsync(note.Id, default))!.Markdown);
    }

    [Fact]
    public async Task RepositorySaveUsesAtomicReplacementForLockedExistingFile()
    {
        var note = DevPostIt.Core.Note.Create("note.md", "제목", "이전 본문", []);
        var repository = new MarkdownNoteRepository(root);
        await repository.SaveAsync(note, default);
        var path = Path.Combine(root, "note.md");
        using (new HeldFile(path))
            await HeldFile.AssertFailsAsync(() => repository.SaveAsync(note with { Markdown = "새 본문" }, default));
        Assert.Equal("이전 본문", (await repository.GetAsync(note.Id, default))!.Markdown);
    }
    public void Dispose() => Directory.Delete(root, true);
}
