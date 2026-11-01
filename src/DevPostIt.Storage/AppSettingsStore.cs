using System.Text.Json;

namespace DevPostIt.Storage;

public sealed class NoteWindowState
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? IconLeft { get; set; }
    public double? IconTop { get; set; }
    public bool Topmost { get; set; }
    public string Color { get; set; } = "yellow";
}

public sealed class AppSettings
{
    public string? DataRoot { get; set; }
    public string? PendingMigrationFrom { get; set; }
    public bool StartInTray { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public bool TransparentImagesRepaired { get; set; }
    public int CaptureDelaySeconds { get; set; }
    public List<Guid>? OpenNotes { get; set; }
    public List<Guid> CollapsedNotes { get; set; } = [];
    public Dictionary<Guid, NoteWindowState> Notes { get; set; } = [];
}

// 메모 내용이 아닌 앱 상태만 보관합니다. 파일이 손상되어도 메모와 시작에는 영향을 주지 않습니다.
public sealed class AppSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly object gate = new();
    public AppSettings Current { get; private set; } = Load(path);

    private static AppSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new() : new(); }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        lock (gate)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(Current, Options));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 창 상태를 저장하지 못해도 메모 작업을 막지 않습니다. */ }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
        }
    }

    public NoteWindowState Note(Guid id)
    {
        lock (gate)
        {
            if (!Current.Notes.TryGetValue(id, out var state)) Current.Notes[id] = state = new();
            return state;
        }
    }
}
