using HunminNotes.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HunminNotes.Storage;

public sealed class FileRecoveryJournal : IDraftJournal
{
    private readonly string root;
    private readonly AtomicFileWriter writer = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    private const int MaxBytes = 16 * 1024 * 1024;

    public FileRecoveryJournal(string directory)
    {
        root = Path.GetFullPath(directory);
        StoragePathGuard.RejectLinks(root);
    }

    public async Task<RecoveryDraft?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaxBytes) throw new InvalidDataException("복구 기록이 너무 큽니다.");
            var draft = await JsonSerializer.DeserializeAsync<RecoveryDraft>(stream, Json, cancellationToken).ConfigureAwait(false);
            Validate(draft);
            if (draft!.Note.Id != id) throw new InvalidDataException("복구 기록의 메모 ID가 일치하지 않습니다.");
            return draft;
        }
        catch (JsonException error) { throw new InvalidDataException("복구 기록을 읽지 못했습니다. 원본 기록은 보존했습니다.", error); }
    }

    public Task WriteAsync(RecoveryDraft draft, CancellationToken cancellationToken)
    {
        Validate(draft);
        var json = JsonSerializer.Serialize(draft, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("복구 기록이 너무 큽니다.");
        return writer.WriteAsync(GetPath(draft.Note.Id), json, cancellationToken);
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetPath(id));
        return Task.CompletedTask;
    }

    private string GetPath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("유효한 메모 ID가 필요합니다.", nameof(id));
        var path = Path.Combine(root, id.ToString("N") + ".json");
        StoragePathGuard.RejectLinks(path);
        return path;
    }

    private static void Validate(RecoveryDraft? draft)
    {
        if (draft?.Note is not { } note || note.Id == Guid.Empty || note.Markdown is null || note.Title is null ||
            note.RelativePath is null || note.Tags is null || note.Tags.Any(tag => tag is null) ||
            draft.Revision <= 0 || draft.Revision > 9_007_199_254_740_991 || draft.BaseFingerprint is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("복구 기록의 필수 값이 잘못되었습니다.");
    }
}
