using System.Text;
using System.Text.Json;

namespace DevPostIt.EditorHost;

public sealed record ToolRequest(string Type, JsonElement Root);

// 마인드맵, 할 일, 달력 페이지의 요청입니다. 페이지별로 허용한 종류와 필드만 받고 파일 경로는 받지 않습니다.
public static class ToolMessage
{
    private const int MaxBytes = 16 * 1024 * 1024;
    private static readonly Dictionary<string, string[]> MindMap = new()
    {
        ["mindmap.list"] = [], ["mindmap.open"] = ["id"], ["mindmap.save"] = ["map"], ["mindmap.delete"] = ["id"],
        ["mindmap.openLink"] = ["url"], ["mindmap.exportPng"] = ["name", "data"],
    };
    private static readonly Dictionary<string, string[]> Planner = new()
    {
        ["planner.load"] = [], ["planner.put"] = ["collection", "item"], ["planner.delete"] = ["collection", "id"],
    };
    public static bool TryReadMindMap(string json, string source, out ToolRequest? request) => TryRead(json, EditorOrigin.IsMindMapPage(source), MindMap, out request);
    public static bool TryReadPlanner(string json, string source, out ToolRequest? request) =>
        TryRead(json, EditorOrigin.IsTodoPage(source) || EditorOrigin.IsCalendarPage(source), Planner, out request);

    private static bool TryRead(string json, bool trusted, Dictionary<string, string[]> allowed, out ToolRequest? request)
    {
        request = null;
        if (!trusted || Encoding.UTF8.GetByteCount(json) > MaxBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 300 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1) return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || !allowed.TryGetValue(type.GetString()!, out var fields)) return false;
            var names = root.EnumerateObject().Select(property => property.Name).Where(name => name is not ("protocolVersion" or "type")).ToArray();
            if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Except(fields, StringComparer.Ordinal).Any()) return false;
            request = new(type.GetString()!, root.Clone());
            return true;
        }
        catch (JsonException) { return false; }
    }
}
