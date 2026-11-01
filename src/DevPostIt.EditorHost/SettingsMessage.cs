using System.Text;
using System.Text.Json;

namespace DevPostIt.EditorHost;

// 설정 페이지가 보낼 수 있는 유일한 요청입니다. 경로는 받지 않고 앱이 자신의 데이터 폴더만 엽니다.
public static class SettingsMessage
{
    private const int MaxMessageBytes = 1024;
    public static bool IsOpenDataFolder(string json, string source) => Read(json, source, "settings.openDataFolder", 2, _ => true);
    public static bool IsChangeDataFolder(string json, string source) => Read(json, source, "settings.changeDataFolder", 2, _ => true);
    public static bool IsBackup(string json, string source) => Read(json, source, "settings.backup", 2, _ => true);
    public static bool IsRestore(string json, string source) => Read(json, source, "settings.restore", 2, _ => true);
    public static bool IsCheckUpdate(string json, string source) => Read(json, source, "settings.checkUpdate", 2, _ => true);
    public static bool IsCleanImages(string json, string source) => Read(json, source, "settings.cleanImages", 2, _ => true);
    public static bool TryReadCaptureDelay(string json, string source, out int seconds)
    {
        var read = 0;
        var accepted = Read(json, source, "settings.captureDelay", 3, root =>
            root.TryGetProperty("value", out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out read) && CaptureGeometry.DelaySeconds.Contains(read));
        seconds = accepted ? read : 0;
        return accepted;
    }
    public static bool TryReadCaptureAnnotate(string json, string source, out bool value) => TryReadFlag(json, source, "settings.captureAnnotate", out value);
    public static bool TryReadStartInTray(string json, string source, out bool value) => TryReadFlag(json, source, "settings.startInTray", out value);
    public static bool TryReadAutoUpdate(string json, string source, out bool value) => TryReadFlag(json, source, "settings.autoUpdate", out value);
    private static bool TryReadFlag(string json, string source, string type, out bool value)
    {
        var read = false;
        var accepted = Read(json, source, type, 3, root =>
        {
            if (!root.TryGetProperty("value", out var item) || item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            read = item.GetBoolean();
            return true;
        });
        value = read;
        return accepted;
    }
    private static bool Read(string json, string source, string expected, int fields, Func<JsonElement, bool> extra)
    {
        if (!EditorOrigin.IsSettingsPage(source) || Encoding.UTF8.GetByteCount(json) > MaxMessageBytes) return false;
        return ProtocolMessage.Read(json, expected, fields, extra);
    }
}

internal static class ProtocolMessage
{
    public static bool Read(string json, string expected, int fields, Func<JsonElement, bool> extra)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != fields) return false;
            return root.TryGetProperty("protocolVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1 &&
                root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == expected && extra(root);
        }
        catch (JsonException) { return false; }
    }
}

// 검색 페이지는 검색어와 메모 ID만 보낼 수 있습니다. 파일 경로는 받지 않습니다.
public static class SearchMessage
{
    private const int MaxMessageBytes = 2048, MaxQueryLength = 200;
    public static bool TryReadQuery(string json, string source, out string query)
    {
        var read = "";
        var accepted = EditorOrigin.IsSearchPage(source) && Encoding.UTF8.GetByteCount(json) <= MaxMessageBytes && ProtocolMessage.Read(json, "search.query", 3, root =>
        {
            if (!root.TryGetProperty("query", out var item) || item.ValueKind != JsonValueKind.String || item.GetString()!.Length > MaxQueryLength) return false;
            read = item.GetString()!;
            return true;
        });
        query = read;
        return accepted;
    }
    public static bool TryReadOpen(string json, string source, out Guid id)
    {
        var read = Guid.Empty;
        var accepted = EditorOrigin.IsSearchPage(source) && Encoding.UTF8.GetByteCount(json) <= MaxMessageBytes && ProtocolMessage.Read(json, "search.open", 3, root =>
            root.TryGetProperty("id", out var item) && item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out read) && read != Guid.Empty);
        id = read;
        return accepted;
    }
}
