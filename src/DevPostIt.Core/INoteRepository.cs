namespace DevPostIt.Core;

public interface INoteRepository
{
    Task<Note?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(Note note, CancellationToken cancellationToken);
}
