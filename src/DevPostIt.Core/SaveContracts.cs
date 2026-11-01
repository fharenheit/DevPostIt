namespace DevPostIt.Core;

public record RecoveryDraft(Note Note, long Revision, string BaseFingerprint);
public interface IDraftJournal
{
    Task<RecoveryDraft?> ReadAsync(Guid id, CancellationToken cancellationToken);
    Task WriteAsync(RecoveryDraft draft, CancellationToken cancellationToken);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken);
}
public interface INoteIndexWriter
{
    Task UpsertAsync(Note note, CancellationToken cancellationToken);
}
public interface INoteIndexDeletionWriter : INoteIndexWriter
{
    Task RemoveAsync(Guid id, CancellationToken cancellationToken);
}
public interface INoteDeletionRepository : INoteRepository
{
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
public enum NoteSaveStatus { Clean, Pending, Saved, Failed, RecoveryPending }
public record NoteSaveState(Note Note, long Revision, long SavedRevision, NoteSaveStatus Status,
    bool RecoveryPending, bool IndexPending, string? Error)
{
    public bool HasUnsavedChanges => Revision != SavedRevision;
}
public enum ExternalChangeKind { Reloaded, ConflictPreserved, Missing }
public record ExternalChange(Guid NoteId, ExternalChangeKind Kind, NoteSaveState State, string? Message);
public sealed class NoteSaveConflictException(string message) : IOException(message);
