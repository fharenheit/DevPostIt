namespace DevPostIt.Storage.Tests;

public sealed class NoteDataLocationTests : IDisposable
{
    private readonly string baseDirectory = Path.Combine(Path.GetTempPath(), "DevPostItLocationTests", Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(baseDirectory, "Documents", "DevPostIt");
    private string Target => Path.Combine(baseDirectory, "Local", "DevPostIt", "Data");

    public void Dispose() { if (Directory.Exists(baseDirectory)) Directory.Delete(baseDirectory, true); }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(Legacy, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void DefaultRootUsesLocalApplicationData() =>
        Assert.Equal(Path.Combine(@"C:\Users\me\AppData\Local", "DevPostIt", "Data"), NoteDataLocation.DefaultRoot(@"C:\Users\me\AppData\Local"));

    [Fact]
    public void LegacyRootIsDocumentsFolder() =>
        Assert.Equal(Path.Combine(@"C:\Users\me\Documents", "DevPostIt"), NoteDataLocation.LegacyRoot(@"C:\Users\me\Documents"));

    [Fact]
    public void CopiesNotesAndIndexAndKeepsLegacyFolder()
    {
        Write(Path.Combine("notes", "Inbox", "한글 메모.md"), "본문");
        Write(Path.Combine(".hunmin", "note-index.db"), "db");
        Write(Path.Combine(".hunmin", "recovery", "draft.json"), "초안");

        Assert.True(NoteDataLocation.MigrateLegacy(Legacy, Target));

        Assert.Equal("본문", File.ReadAllText(Path.Combine(Target, "notes", "Inbox", "한글 메모.md")));
        Assert.Equal("db", File.ReadAllText(Path.Combine(Target, ".hunmin", "note-index.db")));
        Assert.Equal("초안", File.ReadAllText(Path.Combine(Target, ".hunmin", "recovery", "draft.json")));
        Assert.False(File.Exists(Path.Combine(Target, ".hunmin", "session.lock")));
        Assert.True(File.Exists(Path.Combine(Legacy, "notes", "Inbox", "한글 메모.md")));
        Assert.False(Directory.Exists(Target + ".migrating"));
    }

    [Fact]
    public async Task MigratedNotesOpenInWorkspace()
    {
        var source = await NoteWorkspace.OpenAsync(Legacy, default);
        var id = source.Notes.Single().Id;
        await source.DisposeAsync();

        Assert.True(NoteDataLocation.MigrateLegacy(Legacy, Target));

        await using var migrated = await NoteWorkspace.OpenAsync(Target, default);
        Assert.Equal(id, migrated.Notes.Single().Id);
    }

    [Fact]
    public void ExistingTargetIsNeverOverwritten()
    {
        Write(Path.Combine("notes", "a.md"), "이전");
        Directory.CreateDirectory(Path.Combine(Target, "notes"));
        File.WriteAllText(Path.Combine(Target, "notes", "a.md"), "새 위치");

        Assert.False(NoteDataLocation.MigrateLegacy(Legacy, Target));

        Assert.Equal("새 위치", File.ReadAllText(Path.Combine(Target, "notes", "a.md")));
    }

    [Fact]
    public void NothingHappensWithoutLegacyNotes()
    {
        Assert.False(NoteDataLocation.MigrateLegacy(Legacy, Target));
        Assert.False(Directory.Exists(Target));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void RefusesWhileAnotherAppUsesLegacyFolder()
    {
        Write(Path.Combine("notes", "a.md"), "본문");
        using var running = new DataDirectoryLease(Legacy);

        Assert.ThrowsAny<IOException>(() => NoteDataLocation.MigrateLegacy(Legacy, Target));

        Assert.False(Directory.Exists(Target));
    }

    [Fact]
    public void FailedCopyLeavesNoTargetSoStartupCanRetry()
    {
        Write(Path.Combine("notes", "a.md"), "본문");
        var locked = Path.Combine(Legacy, "notes", "a.md");
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => NoteDataLocation.MigrateLegacy(Legacy, Target));
            Assert.False(Directory.Exists(Target));
            Assert.False(Directory.Exists(Target + ".migrating"));
        }
        Assert.True(NoteDataLocation.MigrateLegacy(Legacy, Target));
        Assert.Equal("본문", File.ReadAllText(Path.Combine(Target, "notes", "a.md")));
    }

    [Fact]
    public void LeftoverStagingFromInterruptedCopyIsReplaced()
    {
        Write(Path.Combine("notes", "a.md"), "본문");
        Directory.CreateDirectory(Path.Combine(Target + ".migrating", "notes"));
        File.WriteAllText(Path.Combine(Target + ".migrating", "notes", "stale.md"), "잘린 복사본");

        Assert.True(NoteDataLocation.MigrateLegacy(Legacy, Target));

        Assert.False(File.Exists(Path.Combine(Target, "notes", "stale.md")));
        Assert.True(File.Exists(Path.Combine(Target, "notes", "a.md")));
    }

    [Fact]
    public void RefusesLinksInsteadOfFollowingThem()
    {
        Write(Path.Combine("notes", "a.md"), "본문");
        var outside = Path.Combine(baseDirectory, "outside");
        Directory.CreateDirectory(outside);
        try { Directory.CreateSymbolicLink(Path.Combine(Legacy, "notes", "link"), outside); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; } // 개발자 모드가 아니면 링크를 만들 수 없습니다.

        Assert.ThrowsAny<IOException>(() => NoteDataLocation.MigrateLegacy(Legacy, Target));

        Assert.False(Directory.Exists(Target));
    }
}
