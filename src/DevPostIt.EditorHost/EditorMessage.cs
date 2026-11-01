using System.Text.Json;

namespace DevPostIt.EditorHost;

public record EditorMessage(int ProtocolVersion, string Type, string RequestId, Guid NoteId, long Revision, JsonElement Payload);
