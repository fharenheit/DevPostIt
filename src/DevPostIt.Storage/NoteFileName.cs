using System.Text.RegularExpressions;

namespace DevPostIt.Storage;

// 메모 이름을 Windows에서 쓸 수 있는 파일 이름으로 바꿉니다. 쓸 글자가 남지 않으면 메모 ID를 씁니다.
public static partial class NoteFileName
{
    private const int MaxLength = 100;
    public static string For(string title, Guid id)
    {
        var name = string.Concat(title.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (name.Length > MaxLength) name = name[..MaxLength];
        name = name.TrimEnd('.', ' ');
        if (name.Length == 0) return $"{id:N}.md";
        if (Reserved().IsMatch(name.Split('.')[0])) name = "_" + name;
        return name + ".md";
    }
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}
