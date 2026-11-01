namespace DevPostIt.Core;

public record Note(Guid Id, string RelativePath, string Title, string Markdown,
    IReadOnlyList<string> Tags, DateTimeOffset Created, DateTimeOffset Updated)
{
    public static Note Create(string relativePath, string title, string markdown,
        IReadOnlyList<string> tags)
    {
        var now = DateTimeOffset.UtcNow;
        return new Note(Guid.NewGuid(), relativePath, title, markdown, tags, now, now);
    }
}
