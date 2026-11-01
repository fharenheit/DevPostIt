using System.Text;
using System.Text.Json;

namespace DevPostIt.EditorHost;

// 설정 페이지가 보낼 수 있는 유일한 요청입니다. 경로는 받지 않고 앱이 자신의 데이터 폴더만 엽니다.
public static class SettingsMessage
{
    private const int MaxMessageBytes = 1024;
    public static bool IsOpenDataFolder(string json, string source)
    {
        if (!EditorOrigin.IsSettingsPage(source) || Encoding.UTF8.GetByteCount(json) > MaxMessageBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2) return false;
            return root.TryGetProperty("protocolVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1 &&
                root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "settings.openDataFolder";
        }
        catch (JsonException) { return false; }
    }
}
