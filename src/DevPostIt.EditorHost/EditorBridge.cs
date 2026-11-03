using DevPostIt.Core;
using System.Text;
using System.Text.Json;

namespace DevPostIt.EditorHost;

public sealed class EditorProtocolException(string message) : Exception(message);
public record BridgeResult(EditorMessage Message, NoteChange? Change);

public static class NoteColors
{
    public const string Default = "yellow";
    public static readonly IReadOnlyList<string> All = ["yellow", "green", "blue", "pink", "purple", "gray"];
    public static bool IsKnown(string? color) => color is not null && All.Contains(color);
}

public sealed class EditorBridge(Guid noteId, long revision, Func<string, bool>? isTrustedPage = null)
{
    public void SyncRevision(long value) => Revision = value;
    public const int MaxMessageBytes = 1024 * 1024;
    public long Revision { get; private set; } = revision;
    public Task<NoteChange?> HandleAsync(EditorMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (message.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(message.RequestId) || message.RequestId.Length > 128 || message.Revision < 0 || message.Revision > 9_007_199_254_740_991)
            throw new EditorProtocolException("잘못된 프로토콜 헤더입니다.");
        if (message.Payload.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(message.Payload.GetRawText()) > MaxMessageBytes)
            throw new EditorProtocolException("잘못된 메시지 본문입니다.");
        if (message.Type == "editor.ready")
        {
            Fields(message.Payload);
            if (message.NoteId != Guid.Empty || message.Revision != 0) throw new EditorProtocolException("잘못된 준비 메시지입니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.NoteId != noteId || noteId == Guid.Empty) throw new EditorProtocolException("잘못된 메모 ID입니다.");
        if (message.Type is "window.collapseRequested" or "note.newRequested" or "window.settingsRequested" or "window.searchRequested" or "note.imagePasteRequested" or "note.exportPdfRequested" or "window.captureRequested" or "note.openImagesRequested" or "note.newDiagramRequested")
        {
            Fields(message.Payload);
            if (message.Revision != Revision) throw new EditorProtocolException("창 요청 리비전이 일치하지 않습니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.Type == "note.editDiagramRequested")
        {
            Fields(message.Payload, "file");
            var file = message.Payload.GetProperty("file");
            if (file.ValueKind != JsonValueKind.String || !EditorOrigin.IsDiagramFile(file.GetString())) throw new EditorProtocolException("편집할 수 없는 파일입니다.");
            if (message.Revision != Revision) throw new EditorProtocolException("창 요청 리비전이 일치하지 않습니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.Type == "note.openLinkRequested")
        {
            Fields(message.Payload, "url");
            var url = message.Payload.GetProperty("url");
            if (url.ValueKind != JsonValueKind.String || !PreviewLink.IsAllowed(url.GetString())) throw new EditorProtocolException("열 수 없는 링크입니다.");
            if (message.Revision != Revision) throw new EditorProtocolException("창 요청 리비전이 일치하지 않습니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.Type == "note.renameRequested")
        {
            Fields(message.Payload, "title");
            if (ReadTitle(message.Payload) is null) throw new EditorProtocolException("메모 이름이 없습니다.");
            if (message.Revision != Revision) throw new EditorProtocolException("창 요청 리비전이 일치하지 않습니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.Type == "window.appearanceChanged")
        {
            Fields(message.Payload, "color", "topmost");
            var color = message.Payload.GetProperty("color");
            var topmost = message.Payload.GetProperty("topmost");
            if (color.ValueKind != JsonValueKind.String || !NoteColors.IsKnown(color.GetString()) || topmost.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new EditorProtocolException("잘못된 메모 표시 설정입니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.Type is not ("note.changed" or "note.saveRequested"))
            throw new EditorProtocolException("허용하지 않는 메시지 또는 메모 ID입니다.");
        if (message.Payload.TryGetProperty("title", out _)) Fields(message.Payload, "markdown", "title");
        else Fields(message.Payload, "markdown");
        var title = ReadTitle(message.Payload);
        if (message.Payload.GetProperty("markdown").ValueKind != JsonValueKind.String)
            throw new EditorProtocolException("본문은 문자열이어야 합니다.");
        if (message.Type == "note.saveRequested")
        {
            if (message.Revision != Revision) throw new EditorProtocolException("저장 요청의 리비전이 일치하지 않습니다.");
            return Task.FromResult<NoteChange?>(null);
        }
        if (message.Revision <= Revision) throw new EditorProtocolException("오래된 리비전입니다.");
        var change = new NoteChange(noteId, message.Revision, message.Payload.GetProperty("markdown").GetString()!, title);
        Revision = message.Revision;
        return Task.FromResult<NoteChange?>(change);
    }

    public async Task<BridgeResult> ReceiveAsync(string json, string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!(isTrustedPage ?? EditorOrigin.IsTrustedPage)(source) || Encoding.UTF8.GetByteCount(json) > MaxMessageBytes)
            throw new EditorProtocolException("메시지 출처 또는 크기를 거부했습니다.");
        EditorMessage message;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Fields(root, "protocolVersion", "type", "requestId", "noteId", "revision", "payload");
            message = new EditorMessage(root.GetProperty("protocolVersion").GetInt32(), root.GetProperty("type").GetString()!,
                root.GetProperty("requestId").GetString()!, root.GetProperty("noteId").GetGuid(),
                root.GetProperty("revision").GetInt64(), root.GetProperty("payload").Clone());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new EditorProtocolException("메시지 JSON 형식이 잘못되었습니다.");
        }
        return new BridgeResult(message, await HandleAsync(message, cancellationToken));
    }

    public static string? ReadTitle(JsonElement element)
    {
        if (!element.TryGetProperty("title", out var title)) return null;
        if (title.ValueKind != JsonValueKind.String) throw new EditorProtocolException("메모 이름은 문자열이어야 합니다.");
        try { return NoteTitle.Normalize(title.GetString()!); }
        catch (ArgumentException error) { throw new EditorProtocolException(error.Message); }
    }

    private static void Fields(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new EditorProtocolException("JSON 객체가 필요합니다.");
        var names = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            names.Except(expected, StringComparer.Ordinal).Any())
            throw new EditorProtocolException("알 수 없거나 중복된 JSON 필드입니다.");
    }
}
