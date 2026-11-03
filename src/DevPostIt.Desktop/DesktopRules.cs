using DevPostIt.Storage;

namespace DevPostIt.Desktop;

// 제목 표시줄과 접힌 아이콘의 색입니다. 머리글 색은 style.css의 data-note-color와 맞춥니다.
public static class NotePalette
{
    public readonly record struct Rgb(byte R, byte G, byte B);

    public static Rgb Caption(string? color) => color switch
    {
        "green" => new(0xD8, 0xF3, 0xDC),
        "blue" => new(0xD6, 0xEA, 0xF8),
        "pink" => new(0xFB, 0xDD, 0xE8),
        "purple" => new(0xE6, 0xDF, 0xF7),
        "gray" => new(0xE8, 0xE6, 0xE1),
        _ => new(0xFF, 0xF3, 0xBF),
    };

    public static (Rgb Fill, Rgb Border) Icon(string? color) => color switch
    {
        "green" => (new(169, 229, 181), new(96, 166, 112)),
        "blue" => (new(169, 211, 242), new(92, 146, 190)),
        "pink" => (new(246, 179, 203), new(190, 110, 140)),
        "purple" => (new(201, 184, 240), new(136, 112, 190)),
        "gray" => (new(201, 198, 190), new(140, 136, 126)),
        _ => (new(255, 232, 143), new(205, 178, 86)),
    };
}

// 앱을 다시 켜면 지난번에 열어 둔 메모를 같은 상태로 엽니다.
public static class SessionRestore
{
    public readonly record struct Item(Guid Id, bool Collapsed);

    public static IReadOnlyList<Item> Plan(IReadOnlyList<Guid> notes, AppSettings settings)
    {
        if (settings.StartInTray) return [];
        var known = notes.ToHashSet();
        var open = settings.OpenNotes?.Where(known.Contains).ToArray() ?? [];
        if (open.Length == 0) return [new(notes[0], false)];
        var collapsed = settings.CollapsedNotes.ToHashSet();
        return [.. open.Select(id => new Item(id, collapsed.Contains(id)))];
    }
}

public static class ScreenCoordinates
{
    // 위치는 왼쪽 위 기준으로 저장합니다. AppKit은 왼쪽 아래 기준이며 같은 계산으로 서로 변환합니다.
    public static double FlipY(double y, double height, double primaryScreenHeight) => primaryScreenHeight - y - height;
}
