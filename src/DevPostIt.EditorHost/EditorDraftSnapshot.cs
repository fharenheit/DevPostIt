using System.Text;
using System.Text.Json;

namespace DevPostIt.EditorHost;

public record EditorDraftSnapshot(string Markdown, long Revision, bool Dirty, string? Title = null)
{
    public static EditorDraftSnapshot Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > EditorBridge.MaxMessageBytes) throw new EditorProtocolException("종료할 초안이 전송 제한을 초과했습니다.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new EditorProtocolException("종료할 초안을 확인하지 못했습니다.");
            var fields = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (fields.Length is not (3 or 4) || fields.Distinct().Count() != fields.Length || fields.Except(new[] { "markdown", "revision", "dirty", "title" }).Any() || new[] { "markdown", "revision", "dirty" }.Except(fields).Any())
                throw new EditorProtocolException("종료 초안의 필드가 잘못되었습니다.");
            var markdown = root.GetProperty("markdown").GetString() ?? throw new EditorProtocolException("초안 본문이 없습니다.");
            var revision = root.GetProperty("revision").GetInt64();
            if (revision < 0 || revision > 9_007_199_254_740_991) throw new EditorProtocolException("초안 리비전이 잘못되었습니다.");
            return new(markdown, revision, root.GetProperty("dirty").GetBoolean(), EditorBridge.ReadTitle(root));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { throw new EditorProtocolException("종료할 초안의 JSON이 잘못되었습니다."); }
    }
}
