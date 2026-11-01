using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevPostIt.Storage.Tests;

public sealed class BackupAndUpdateTests : IDisposable
{
    private readonly string baseDirectory = Path.Combine(Path.GetTempPath(), "devpostit-backup-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(baseDirectory, "Data");
    private string Zip => Path.Combine(baseDirectory, "backup.zip");
    public BackupAndUpdateTests() => Directory.CreateDirectory(baseDirectory);
    public void Dispose() { if (Directory.Exists(baseDirectory)) Directory.Delete(baseDirectory, true); }

    private async Task<Guid> CreateBackupAsync()
    {
        await using var workspace = await NoteWorkspace.OpenAsync(Root, default);
        var note = workspace.Notes.Single();
        await workspace.SaveImageAsync(note.Id, [1, 2, 3], default);
        var trashed = await workspace.CreateAsync(default);
        await workspace.TrashAsync(trashed.Id, default);
        var summary = await NoteBackup.CreateAsync(Root, Zip, "0.1.0", default);
        Assert.Equal(1, summary.Notes);
        return note.Id;
    }

    private void RewriteZip(Action<ZipArchive> change)
    {
        using var archive = ZipFile.Open(Zip, ZipArchiveMode.Update);
        change(archive);
    }

    [Fact]
    public async Task BackupRestoresNotesImagesAndTrashAndKeepsPreviousData()
    {
        var id = await CreateBackupAsync();
        await File.WriteAllTextAsync(Path.Combine(Root, "notes", "Inbox", "첫메모.md"), "나중에 망가진 내용");

        var summary = await NoteBackup.PrepareRestoreAsync(Zip, Root, default);
        Assert.Equal(1, summary.Notes);
        Assert.Equal("나중에 망가진 내용", await File.ReadAllTextAsync(Path.Combine(Root, "notes", "Inbox", "첫메모.md")));

        var previous = NoteBackup.ApplyPending(Root);

        Assert.NotNull(previous);
        Assert.Equal("나중에 망가진 내용", await File.ReadAllTextAsync(Path.Combine(previous!, "notes", "Inbox", "첫메모.md")));
        Assert.False(Directory.Exists(Root + ".restoring"));
        await using var restored = await NoteWorkspace.OpenAsync(Root, default);
        Assert.Equal(id, restored.Notes.Single().Id);
        Assert.Single(restored.TrashedNotes);
        Assert.Single(Directory.GetFiles(Path.Combine(restored.AssetsRoot, id.ToString("N"))));
        Assert.Single(await restored.SearchAsync("아이디어", 10, default));
    }

    [Fact]
    public async Task BackupLeavesOutIndexAndLock()
    {
        await CreateBackupAsync();
        using var archive = ZipFile.OpenRead(Zip);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith("note-index.db", StringComparison.Ordinal) || entry.FullName.EndsWith("session.lock", StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName == "devpostit-backup.json");
    }

    [Fact]
    public void ApplyPendingWithoutRestoreDoesNothing() => Assert.Null(NoteBackup.ApplyPending(Root));

    [Fact]
    public async Task UnfinishedStagingIsDiscardedAndDataKept()
    {
        await CreateBackupAsync();
        Directory.CreateDirectory(Path.Combine(Root + ".restoring", "notes"));
        Assert.Null(NoteBackup.ApplyPending(Root));
        Assert.False(Directory.Exists(Root + ".restoring"));
        Assert.True(Directory.Exists(Path.Combine(Root, "notes")));
    }

    [Fact]
    public async Task ApplyRefusesWhileDataIsInUse()
    {
        await CreateBackupAsync();
        await NoteBackup.PrepareRestoreAsync(Zip, Root, default);
        await using var running = await NoteWorkspace.OpenAsync(Root, default);
        Assert.ThrowsAny<IOException>(() => NoteBackup.ApplyPending(Root));
        Assert.Contains(running.Notes, _ => true);
    }

    [Fact]
    public async Task RejectsFileThatIsNotBackup()
    {
        await File.WriteAllTextAsync(Zip, "zip 아님");
        await Assert.ThrowsAsync<InvalidBackupException>(() => NoteBackup.PrepareRestoreAsync(Zip, Root, default));
        Assert.False(Directory.Exists(Root + ".restoring"));
    }

    [Fact]
    public async Task RejectsZipWithoutManifest()
    {
        using (var archive = ZipFile.Open(Zip, ZipArchiveMode.Create)) archive.CreateEntry("notes/a.md");
        await Assert.ThrowsAsync<InvalidBackupException>(() => NoteBackup.PrepareRestoreAsync(Zip, Root, default));
    }

    [Fact]
    public async Task RejectsTamperedFile()
    {
        await CreateBackupAsync();
        RewriteZip(archive =>
        {
            var entry = archive.Entries.First(item => item.FullName.EndsWith("첫메모.md", StringComparison.Ordinal));
            var name = entry.FullName;
            byte[] bytes;
            using (var input = entry.Open()) using (var memory = new MemoryStream()) { input.CopyTo(memory); bytes = memory.ToArray(); }
            bytes[^1] ^= 1;
            entry.Delete();
            using var output = archive.CreateEntry(name).Open();
            output.Write(bytes);
        });
        await Assert.ThrowsAsync<InvalidBackupException>(() => NoteBackup.PrepareRestoreAsync(Zip, Root, default));
        Assert.False(Directory.Exists(Root + ".restoring"));
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("notes/../../outside.md")]
    [InlineData(".hunmin/session.lock")]
    [InlineData(".hunmin/note-index.db")]
    [InlineData("other/a.md")]
    [InlineData("notes/a:b.md")]
    public async Task RejectsPathsOutsideAllowedFolders(string path)
    {
        await CreateBackupAsync();
        RewriteZip(archive =>
        {
            var bytes = Encoding.UTF8.GetBytes("x");
            using (var output = archive.CreateEntry(path).Open()) output.Write(bytes);
            var manifestEntry = archive.GetEntry("devpostit-backup.json")!;
            JsonDocument manifest;
            using (var input = manifestEntry.Open()) manifest = JsonDocument.Parse(input);
            var files = manifest.RootElement.GetProperty("Files").EnumerateArray().Select(item => JsonSerializer.Deserialize<object>(item.GetRawText())).ToList();
            files.Add(new { Path = path, Size = 1L, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            var updated = new { Format = 1, App = "x", Created = DateTimeOffset.UtcNow, Files = files };
            manifestEntry.Delete();
            using var replaced = archive.CreateEntry("devpostit-backup.json").Open();
            JsonSerializer.Serialize(replaced, updated);
        });
        await Assert.ThrowsAsync<InvalidBackupException>(() => NoteBackup.PrepareRestoreAsync(Zip, Root, default));
        Assert.False(File.Exists(Path.Combine(baseDirectory, "outside.md")));
    }

    private const string Hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
    private static string Release(string tag, bool draft = false, bool prerelease = false, string name = "DevPostIt-0.2.0-win-x64.exe", string url = "https://github.com/fharenheit/DevPostIt/releases/download/v0.2.0/a.exe", string? digest = "sha256:" + Hash, long size = 10) =>
        JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, assets = new object[] { digest is null ? new { name, browser_download_url = url, size } : new { name, browser_download_url = url, size, digest } } });

    [Fact]
    public void ParseFindsNewerRelease()
    {
        var update = AppUpdater.ParseLatest(Release("v0.2.0"), new Version(0, 1, 0, 0));
        Assert.Equal(new Version(0, 2, 0), update?.Version);
        Assert.Equal(Hash, update?.Sha256);
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    [InlineData("nightly")]
    public void ParseIgnoresSameOlderOrInvalidVersion(string tag) => Assert.Null(AppUpdater.ParseLatest(Release(tag), new Version(0, 1, 0, 0)));

    [Fact]
    public void ParseIgnoresUnsafeReleases()
    {
        var current = new Version(0, 1, 0);
        Assert.Null(AppUpdater.ParseLatest(Release("v0.2.0", draft: true), current));
        Assert.Null(AppUpdater.ParseLatest(Release("v0.2.0", prerelease: true), current));
        Assert.Null(AppUpdater.ParseLatest(Release("v0.2.0", name: "other.exe"), current));
        Assert.Null(AppUpdater.ParseLatest(Release("v0.2.0", digest: null), current));
        Assert.Null(AppUpdater.ParseLatest(Release("v0.2.0", url: "http://github.com/a.exe"), current));
        Assert.Null(AppUpdater.ParseLatest(Release("v0.2.0", url: "https://evil.example.com/a.exe"), current));
        Assert.Null(AppUpdater.ParseLatest("not json", current));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private (AppUpdater Updater, string Exe, AppUpdate Update) Prepare(byte[] content, bool selfTestResult = true, string? sha = null)
    {
        var exe = Path.Combine(baseDirectory, "DevPostIt.exe");
        File.WriteAllText(exe, "현재 버전");
        var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }));
        var updater = new AppUpdater(http, exe, _ => Task.FromResult(selfTestResult));
        return (updater, exe, new AppUpdate(new Version(0, 2, 0), "https://github.com/a.exe", sha ?? Convert.ToHexString(SHA256.HashData(content)), content.Length));
    }

    [Fact]
    public async Task DownloadAndInstallReplacesExecutableAndCleansUp()
    {
        var (updater, exe, update) = Prepare("새 버전"u8.ToArray());
        await updater.DownloadAsync(update, default);
        Assert.Equal("현재 버전", File.ReadAllText(exe));
        updater.Install();
        Assert.Equal("새 버전", File.ReadAllText(exe));
        Assert.True(File.Exists(Path.Combine(baseDirectory, "DevPostIt.old.exe")));
        updater.CleanUp();
        Assert.False(File.Exists(Path.Combine(baseDirectory, "DevPostIt.old.exe")));
    }

    [Fact]
    public async Task WrongHashIsRejectedAndNothingChanges()
    {
        var (updater, exe, update) = Prepare("새 버전"u8.ToArray(), sha: Hash);
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(update, default));
        Assert.Equal("현재 버전", File.ReadAllText(exe));
        Assert.False(File.Exists(Path.Combine(baseDirectory, "DevPostIt.update.exe")));
    }

    [Fact]
    public async Task BlockedSelfTestKeepsCurrentVersion()
    {
        var (updater, exe, update) = Prepare("새 버전"u8.ToArray(), selfTestResult: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => updater.DownloadAsync(update, default));
        Assert.Equal("현재 버전", File.ReadAllText(exe));
        Assert.False(File.Exists(Path.Combine(baseDirectory, "DevPostIt.update.exe")));
    }

    [Fact]
    public async Task CheckSendsUserAgentAndTreatsMissingReleaseAsNoUpdate()
    {
        HttpRequestMessage? seen = null;
        var http = new HttpClient(new FakeHandler(request => { seen = request; return new HttpResponseMessage(HttpStatusCode.NotFound); }));
        var updater = new AppUpdater(http, Path.Combine(baseDirectory, "DevPostIt.exe"));
        Assert.Null(await updater.CheckAsync(new Version(0, 1, 0), default));
        Assert.NotEmpty(seen!.Headers.UserAgent);
        Assert.Equal(AppUpdater.LatestReleaseUrl, seen.RequestUri!.ToString());
    }
}
