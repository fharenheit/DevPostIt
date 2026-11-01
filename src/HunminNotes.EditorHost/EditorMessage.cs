using System.Text.Json;

namespace HunminNotes.EditorHost;

public record EditorMessage(int ProtocolVersion, string Type, string RequestId, Guid NoteId, long Revision, JsonElement Payload);
