using System.Runtime.CompilerServices;
using DevPostIt.Core;
using Microsoft.Data.Sqlite;

namespace DevPostIt.Storage.Tests;

public sealed class SqliteNoteIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DevPostItIndexTests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "note-index.db");
    private SqliteNoteIndex Index => new(Database);

    public SqliteNoteIndexTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task InsertUpdateDeleteAndReopenKeepSearchInSync()
    {
        var note = Note.Create("Inbox/첫째.md", "회의록", "원래 본문", ["업무"]);
        await Index.UpsertAsync(note, default);
        Assert.Equal(new[] { note.Id }, await Index.SearchAsync("회의록", 20, default));
        Assert.Equal(new[] { note.Id }, await Index.SearchAsync("업무", 20, default));
        await Index.UpsertAsync(note with { Title = "수정 제목", Markdown = "새로운 본문", Tags = ["개발"] }, default);
        Assert.Empty(await Index.SearchAsync("회의록", 20, default));
        Assert.Empty(await Index.SearchAsync("원래", 20, default));
        Assert.Empty(await Index.SearchAsync("업무", 20, default));
        Assert.Equal(new[] { note.Id }, await Index.SearchAsync("새로운", 20, default));
        Assert.Equal(new[] { note.Id }, await Index.SearchAsync("개발", 20, default));
        await Index.RemoveAsync(note.Id, default);
        await Index.RemoveAsync(note.Id, default);
        Assert.Empty(await Index.SearchAsync("새로운", 20, default));
    }

    [Fact]
    public async Task UnicodePathCaseCollisionCannotCreateTwoNotesForSameWindowsPath()
    {
        var first = Note.Create("Inbox/É.md", "원본 메모", "보존", []);
        await Index.UpsertAsync(first, default);
        await Assert.ThrowsAsync<SqliteException>(() => Index.UpsertAsync(Note.Create("inbox/é.md", "다른 메모", "덮어쓰기", []), default));
        Assert.Equal(new[] { first.Id }, await Index.SearchAsync("보존", 10, default));
    }

    [Fact]
    public async Task SameIdIsUpsertedOnceAndPathCollisionDoesNotDestroyExistingEntries()
    {
        var first = Note.Create("one.md", "중복 검증", "본문", []);
        await Index.UpsertAsync(first, default);
        await Index.UpsertAsync(first, default);
        Assert.Equal(new[] { first.Id }, await Index.SearchAsync("중복", 50, default));
        var second = Note.Create("two.md", "둘째 메모", "본문", []);
        await Index.UpsertAsync(second, default);
        await Assert.ThrowsAsync<SqliteException>(() => Index.UpsertAsync(second with { RelativePath = "ONE.md", Title = "실패" }, default));
        Assert.Equal(new[] { second.Id }, await Index.SearchAsync("둘째", 50, default));
        Assert.Equal(new[] { first.Id }, await Index.SearchAsync("중복", 50, default));
    }

    [Theory]
    [InlineData("개발자")]
    [InlineData("발자회")]
    [InlineData("회의")]
    [InlineData("의")]
    [InlineData("인덱스")]
    [InlineData("태그검색")]
    public async Task KoreanSubstringsAreFoundInTitleBodyAndTags(string query)
    {
        var note = Note.Create("korean.md", "개발자회의록", "한글검색인덱스구현", ["태그검색용"]);
        await Index.UpsertAsync(note, default);
        Assert.Equal(new[] { note.Id }, await Index.SearchAsync(query, 20, default));
    }

    [Theory]
    [InlineData("' OR 1=1; DROP TABLE notes; --")]
    [InlineData("회의 OR 다른메모")]
    [InlineData("\"quoted\"")]
    [InlineData("100%")]
    [InlineData("_")]
    [InlineData("*")]
    [InlineData("C++")]
    [InlineData("A\0BCD")]
    public async Task SearchOperatorsQuotesAndWildcardsAreLiteralText(string query)
    {
        var target = Note.Create("target.md", "대상", $"앞 {query} 뒤", []);
        await Index.UpsertAsync(target, default);
        await Index.UpsertAsync(Note.Create("other.md", "회의 다른메모", "무관한 내용", []), default);
        Assert.Equal(new[] { target.Id }, await Index.SearchAsync(query, 50, default));
        Assert.Equal(new[] { target.Id }, await Index.SearchAsync("대상", 50, default));
    }

    [Fact]
    public async Task SearchNormalizesUnicodeAndCaseAndHonorsLimit()
    {
        var first = Note.Create("first.md", "Cafe\u0301 한글", "" , []);
        var second = Note.Create("second.md", "CAFÉ 한글", "", []);
        await Index.UpsertAsync(first, default);
        await Index.UpsertAsync(second, default);
        Assert.Equal(2, (await Index.SearchAsync("café", 10, default)).Count);
        Assert.Single(await Index.SearchAsync("한글", 1, default));
        Assert.Equal(await Index.SearchAsync("한글", 10, default), await Index.SearchAsync("한글", 10, default));
        Assert.Empty(await Index.SearchAsync("  ", 10, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Index.SearchAsync("한글", 0, default));
    }

    [Fact]
    public async Task RebuildReadsRealMarkdownFilesAndRemovesObsoleteRows()
    {
        var original = Note.Create("obsolete.md", "삭제할 항목", "", []);
        await Index.UpsertAsync(original, default);
        var notes = new[] { Note.Create("a.md", "파일정본", "한글 본문", ["업무"]), Note.Create("b.md", "둘째파일", "테스트", []) };
        var folder = Directory.CreateDirectory(Path.Combine(root, "notes")).FullName;
        var repository = new MarkdownNoteRepository(folder);
        foreach (var note in notes) await repository.SaveAsync(note, default);
        await Index.RebuildAsync(ReadFiles(folder), default);
        Assert.Empty(await Index.SearchAsync("삭제할", 10, default));
        Assert.Equal(new[] { notes[0].Id }, await Index.SearchAsync("파일정본", 10, default));
        Assert.Equal(new[] { notes[1].Id }, await Index.SearchAsync("둘째파일", 10, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptDatabaseCanBeExplicitlyRebuilt(bool corrupt)
    {
        byte[]? bytes = null;
        if (corrupt) { bytes = "손상된 DB 파일"u8.ToArray(); await File.WriteAllBytesAsync(Database, bytes); }
        var note = Note.Create("rebuilt.md", "재생성", "복구 본문", []);
        await Index.RebuildAsync(Sequence([note]), default);
        Assert.Equal(new[] { note.Id }, await Index.SearchAsync("복구", 10, default));
        if (corrupt)
        {
            var backup = Assert.Single(Directory.GetFiles(root, "note-index.db.corrupt-*"));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(backup));
        }
    }

    [Fact]
    public async Task CorruptDatabaseIsNotSilentlyResetOnSearch()
    {
        var bytes = "잘못된 DB"u8.ToArray();
        await File.WriteAllBytesAsync(Database, bytes);
        await Assert.ThrowsAsync<SqliteException>(() => Index.SearchAsync("본문", 10, default));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Database));
    }

    [Fact]
    public async Task FailedOrDuplicateRebuildPreservesPreviousUsableIndex()
    {
        var old = Note.Create("old.md", "기존항목", "보존", []);
        var next = Note.Create("next.md", "교체항목", "신규", []);
        await Index.UpsertAsync(old, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Index.RebuildAsync(Failing(next), default));
        Assert.Equal(new[] { old.Id }, await Index.SearchAsync("기존", 10, default));
        await Assert.ThrowsAsync<DuplicateNoteIdException>(() => Index.RebuildAsync(Sequence([next, next]), default));
        Assert.Equal(new[] { old.Id }, await Index.SearchAsync("보존", 10, default));
        Assert.Empty(Directory.GetFiles(root, "*.rebuild-*"));
    }

    [Fact]
    public async Task EmptyRebuildClearsIndex()
    {
        await Index.UpsertAsync(Note.Create("old.md", "기존", "", []), default);
        await Index.RebuildAsync(Sequence([]), default);
        Assert.Empty(await Index.SearchAsync("기존", 10, default));
    }

    [Fact]
    public async Task CancellationDuringRebuildPreservesPreviousIndex()
    {
        var old = Note.Create("old.md", "보존문서", "", []);
        await Index.UpsertAsync(old, default);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Index.RebuildAsync(Cancelled(cancellation), cancellation.Token));
        Assert.Equal(new[] { old.Id }, await Index.SearchAsync("보존", 10, default));
    }

    private static async IAsyncEnumerable<Note> Cancelled(CancellationTokenSource cancellation, [EnumeratorCancellation] CancellationToken token = default)
    {
        yield return Note.Create("next.md", "새문서", "", []);
        cancellation.Cancel();
        await Task.Yield();
        token.ThrowIfCancellationRequested();
    }
    private static async IAsyncEnumerable<Note> Failing(Note note) { yield return note; await Task.Yield(); throw new InvalidOperationException("가져오기 실패"); }
    private static async IAsyncEnumerable<Note> Sequence(IEnumerable<Note> notes) { foreach (var note in notes) { yield return note; await Task.Yield(); } }
    private static async IAsyncEnumerable<Note> ReadFiles(string folder)
    {
        var codec = new FrontmatterCodec();
        foreach (var file in Directory.EnumerateFiles(folder, "*.md")) yield return codec.Parse(await File.ReadAllTextAsync(file), Path.GetFileName(file));
    }
    public void Dispose() => Directory.Delete(root, true);
}
