using System.Security.Cryptography;
using System.Text.Json;

namespace HunminNotes.Core;

public static class NoteFingerprint
{
    public static string Compute(Note note) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(note)));
}
