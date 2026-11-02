using System.Net;
using System.Text;
using DevPostIt.Storage;
using Xunit;

namespace DevPostIt.Storage.Tests;

public sealed class HolidayStoreTests : IDisposable
{
    private const string Valid = """{"version":1,"source":"시험","checked":"2026-11-02","years":{"2026":[{"date":"2026-05-01","name":"노동절","substitute":false}]}}""";
    private readonly string root = Path.Combine(Path.GetTempPath(), "devpostit-holidays-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    private HolidayStore Store(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(root, new HttpClient(new FakeHandler(respond)));

    [Fact]
    public async Task UpdateSavesValidFileAndReportsChangeOnlyOnce()
    {
        var store = Store(_ => Ok(Valid));
        Assert.Null(store.Load());
        Assert.True(await store.UpdateAsync(default));
        Assert.False(await store.UpdateAsync(default));
        Assert.Equal("노동절", store.Load()!.Years["2026"].Single().Name);
    }

    [Fact]
    public async Task UpdateUsesTheRepositoryRawAddress()
    {
        var handler = new FakeHandler(_ => Ok(Valid));
        await new HolidayStore(root, new HttpClient(handler)).UpdateAsync(default);
        Assert.Equal("https://raw.githubusercontent.com/fharenheit/DevPostIt/main/data/korean-holidays.json", handler.Requested!.ToString());
    }

    public static TheoryData<string> InvalidFiles => new()
    {
        "망가진 파일",
        "[]",
        """{"version":2,"years":{}}""",
        """{"version":1,"years":{"abcd":[]}}""",
        """{"version":1,"years":{"1800":[]}}""",
        """{"version":1,"years":{"2026":[{"date":"2027-01-01","name":"다른 해","substitute":false}]}}""",
        """{"version":1,"years":{"2026":[{"date":"2026-13-01","name":"없는 달","substitute":false}]}}""",
        """{"version":1,"years":{"2026":[{"date":"2026-01-01","name":"","substitute":false}]}}""",
        """{"version":1,"years":{"2026":[{"date":"2026-01-01","name":"줄\nd바꿈","substitute":false}]}}""",
        """{"version":1,"years":{"2026":[{"date":"2026-01-01","name":"이","substitute":false,"url":"https://evil.example"}]}}""",
        """{"version":1,"path":"C:\\Windows","years":{}}""",
    };
    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task InvalidFileIsRejectedAndLastGoodFileStays(string body)
    {
        Assert.True(await Store(_ => Ok(Valid)).UpdateAsync(default));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(_ => Ok(body)).UpdateAsync(default));
        Assert.NotNull(Store(_ => Ok(Valid)).Load());
    }

    [Fact]
    public async Task NameLongerThanLimitIsRejected()
    {
        var body = Valid.Replace("노동절", new string('가', 51));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(_ => Ok(body)).UpdateAsync(default));
    }

    [Fact]
    public async Task OversizedFileIsRejected()
    {
        var body = Valid.Replace("시험", new string('a', 70 * 1024));
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(_ => Ok(body)).UpdateAsync(default));
        Assert.Null(Store(_ => Ok(Valid)).Load());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task NonSuccessKeepsLastGoodFileWithoutFollowing(HttpStatusCode code)
    {
        Assert.True(await Store(_ => Ok(Valid)).UpdateAsync(default));
        var store = Store(_ => new HttpResponseMessage(code) { Headers = { Location = new Uri("https://evil.example/") } });
        Assert.False(await store.UpdateAsync(default));
        Assert.NotNull(store.Load());
    }

    [Fact]
    public async Task DamagedSavedFileIsIgnored()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "korean-holidays.json"), "망가짐");
        Assert.Null(Store(_ => Ok(Valid)).Load());
    }

    [Fact]
    public void RepositoryFileIsValidAndHasThisYearAndNext()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "data", "korean-holidays.json"))) directory = directory.Parent;
        var file = HolidayStore.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "data", "korean-holidays.json")));
        Assert.Equal(22, file.Years["2026"].Count);
        Assert.Equal(24, file.Years["2027"].Count);
        Assert.Contains(file.Years["2026"], item => item.Date == "2026-05-01" && item.Name == "노동절");
        Assert.Contains(file.Years["2027"], item => item.Date == "2027-05-03" && item.Substitute);
        Assert.Equal(7, file.Years["2027"].Count(item => item.Substitute));
    }

    [Fact]
    public async Task HolidayFileIsIncludedInBackup()
    {
        var data = Path.Combine(root, "data");
        await using (var workspace = await NoteWorkspace.OpenAsync(data, default)) { }
        Assert.True(await new HolidayStore(Path.Combine(data, ".hunmin", "tools"), new HttpClient(new FakeHandler(_ => Ok(Valid)))).UpdateAsync(default));
        var backup = Path.Combine(root, "backup.zip");
        var summary = await NoteBackup.CreateAsync(data, backup, "0.0.0", default);
        Assert.True(summary.Files >= 2);
        using var archive = System.IO.Compression.ZipFile.OpenRead(backup);
        Assert.Contains(archive.Entries, entry => entry.FullName == ".hunmin/tools/korean-holidays.json");
    }
}
