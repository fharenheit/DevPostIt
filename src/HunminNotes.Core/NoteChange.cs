namespace HunminNotes.Core;

public record NoteChange(Guid NoteId, long Revision, string Markdown);
