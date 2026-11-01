namespace DevPostIt.Core;

public record NoteChange(Guid NoteId, long Revision, string Markdown, string? Title = null);

public static class NoteTitle
{
    public static string Normalize(string title)
    {
        var value = title.Trim();
        if (value.Length is 0 or > 200 || value.Any(char.IsControl)) throw new ArgumentException("메모 이름은 줄바꿈 없이 1~200자로 입력하세요.", nameof(title));
        return value;
    }
}
