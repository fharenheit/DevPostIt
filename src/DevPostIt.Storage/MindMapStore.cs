using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevPostIt.Storage;

public sealed class MindMapNode
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public List<MindMapNode> Children { get; set; } = [];
    public bool Collapsed { get; set; }
    public string? Color { get; set; }
    public bool Bold { get; set; }
    public string Note { get; set; } = "";
    public string? Link { get; set; }
    public string? Icon { get; set; }
    public string? Side { get; set; }
}
public sealed class MindMapDocument
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Layout { get; set; } = "both";
    public string Updated { get; set; } = "";
    public MindMapNode Root { get; set; } = new();
}
public sealed record MindMapSummary(string Id, string Title, string Updated, int Nodes);

// 마인드맵 하나를 파일 하나로 저장합니다. 파일 이름은 앱이 ID로 정하므로 페이지가 경로를 정할 수 없습니다.
public sealed class MindMapStore(string folder)
{
    private const int MaxNodes = 20_000, MaxText = 4_000, MaxNote = 20_000;
    private static readonly string[] Layouts = ["both", "right", "left"];
    private static readonly string[] Icons = ["done", "important", "question", "star", "idea"];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 256, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly AtomicFileWriter writer = new();
    private string PathOf(Guid id) => Path.Combine(folder, $"{id:N}.json");

    public async Task<IReadOnlyList<MindMapSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<MindMapSummary>();
        if (!Directory.Exists(folder)) return result;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var map = JsonSerializer.Deserialize<MindMapDocument>(await File.ReadAllTextAsync(file, cancellationToken), Options);
                if (map is not null) result.Add(new(map.Id, map.Title, map.Updated, Count(map.Root)));
            }
            catch (JsonException) { /* 손상된 파일은 목록에서만 빼고 지우지 않습니다. */ }
        }
        return result.OrderByDescending(map => map.Updated, StringComparer.Ordinal).ToArray();
    }
    public async Task<MindMapDocument> OpenAsync(string id, CancellationToken cancellationToken)
    {
        var file = PathOf(RequireId(id));
        if (!File.Exists(file)) throw new PlannerDataException("마인드맵을 찾지 못했습니다.");
        try { return JsonSerializer.Deserialize<MindMapDocument>(await File.ReadAllTextAsync(file, cancellationToken), Options) ?? throw new PlannerDataException("마인드맵이 비어 있습니다."); }
        catch (JsonException) { throw new PlannerDataException("마인드맵 파일이 손상되었습니다."); }
    }
    public async Task<MindMapDocument> SaveAsync(JsonElement map, CancellationToken cancellationToken)
    {
        MindMapDocument document;
        try { document = map.Deserialize<MindMapDocument>(Options) ?? throw new PlannerDataException("마인드맵이 비어 있습니다."); }
        catch (JsonException) { throw new PlannerDataException("마인드맵 형식이 잘못되었거나 너무 깊습니다."); }
        Validate(document);
        document.Updated = DateTimeOffset.UtcNow.ToString("O");
        Directory.CreateDirectory(folder);
        await writer.WriteAsync(PathOf(RequireId(document.Id)), JsonSerializer.Serialize(document, Options), cancellationToken);
        return document;
    }
    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(PathOf(RequireId(id)));
        return Task.CompletedTask;
    }
    private static Guid RequireId(string id) => Guid.TryParse(id, out var parsed) && parsed != Guid.Empty ? parsed : throw new PlannerDataException("마인드맵 ID가 잘못되었습니다.");
    private static int Count(MindMapNode node) => 1 + node.Children.Sum(Count);
    private static void Validate(MindMapDocument map)
    {
        RequireId(map.Id);
        if (map.Title.Trim().Length == 0 || map.Title.Length > 200) throw new PlannerDataException("마인드맵 이름은 1~200자로 입력하세요.");
        if (!Layouts.Contains(map.Layout)) throw new PlannerDataException("배치 설정이 잘못되었습니다.");
        var ids = new HashSet<string>();
        var stack = new Stack<MindMapNode>([map.Root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (ids.Count >= MaxNodes) throw new PlannerDataException("주제가 너무 많습니다.");
            if (node.Id.Length is 0 or > 64 || !ids.Add(node.Id)) throw new PlannerDataException("주제 ID가 잘못되었습니다.");
            if (node.Text.Length > MaxText || node.Note.Length > MaxNote) throw new PlannerDataException("주제 글자 수가 너무 많습니다.");
            if (node.Link is { Length: > 2048 }) throw new PlannerDataException("링크가 너무 깁니다.");
            if (node.Color is not null && !System.Text.RegularExpressions.Regex.IsMatch(node.Color, "^#[0-9a-fA-F]{6}\\z")) throw new PlannerDataException("색상이 잘못되었습니다.");
            if (node.Icon is not null && !Icons.Contains(node.Icon)) throw new PlannerDataException("아이콘이 잘못되었습니다.");
            if (node.Side is not (null or "left" or "right")) throw new PlannerDataException("방향이 잘못되었습니다.");
            foreach (var child in node.Children) stack.Push(child);
        }
    }
}
