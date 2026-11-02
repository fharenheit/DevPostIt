using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevPostIt.Storage;

public sealed class HolidayEntry
{
    public string Date { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Substitute { get; set; }
}
public sealed class HolidayFile
{
    public int Version { get; set; }
    public string Source { get; set; } = "";
    public string Checked { get; set; } = "";
    public Dictionary<string, List<HolidayEntry>> Years { get; set; } = [];
}

// 저장소의 공휴일 파일을 받아 저장합니다. 파일에 있는 연도는 달력의 내장 계산 대신 이 목록을 씁니다.
// 받지 못하거나 형식이 잘못되면 마지막으로 저장한 파일을, 그것도 없으면 내장 계산을 씁니다.
public sealed class HolidayStore(string folder, HttpClient? http = null)
{
    public const string Url = "https://raw.githubusercontent.com/fharenheit/DevPostIt/main/data/korean-holidays.json";
    private const int MaxBytes = 64 * 1024, MaxYears = 20, MaxEntries = 64, MaxName = 50, MaxNote = 500;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    // 다른 주소로 넘어가는 응답은 따르지 않습니다.
    private readonly HttpClient client = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly AtomicFileWriter writer = new();
    private string FilePath => Path.Combine(folder, "korean-holidays.json");

    public HolidayFile? Load()
    {
        try { return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : null; }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
    }
    // 내용이 바뀌어 저장했으면 true입니다.
    public async Task<bool> UpdateAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.OK) return false;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        if (length > MaxBytes) throw new InvalidDataException("공휴일 파일이 너무 큽니다.");
        var text = Encoding.UTF8.GetString(buffer, 0, length);
        Parse(text);
        if (File.Exists(FilePath) && await File.ReadAllTextAsync(FilePath, cancellationToken) == text) return false;
        Directory.CreateDirectory(folder);
        await writer.WriteAsync(FilePath, text, cancellationToken);
        return true;
    }
    public static HolidayFile Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("공휴일 파일이 너무 큽니다.");
        HolidayFile file;
        try { file = JsonSerializer.Deserialize<HolidayFile>(text, Options) ?? throw new InvalidDataException("공휴일 파일이 비어 있습니다."); }
        catch (JsonException) { throw new InvalidDataException("공휴일 파일 형식이 잘못되었습니다."); }
        if (file.Version != 1) throw new InvalidDataException("지원하지 않는 공휴일 파일입니다.");
        if (file.Source.Length > MaxNote || file.Checked.Length > MaxNote || file.Years.Count > MaxYears) throw new InvalidDataException("공휴일 파일 내용이 너무 깁니다.");
        foreach (var (year, entries) in file.Years)
        {
            if (!int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || year.Length != 4 || number is < 2000 or > 2100) throw new InvalidDataException("공휴일 파일의 연도가 잘못되었습니다.");
            if (entries.Count > MaxEntries) throw new InvalidDataException("공휴일이 너무 많습니다.");
            foreach (var entry in entries)
            {
                if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year != number) throw new InvalidDataException("공휴일 날짜가 잘못되었습니다.");
                if (entry.Name.Trim().Length == 0 || entry.Name.Length > MaxName || entry.Name.Any(char.IsControl)) throw new InvalidDataException("공휴일 이름이 잘못되었습니다.");
            }
        }
        return file;
    }
}
