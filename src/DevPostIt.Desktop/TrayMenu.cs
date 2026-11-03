namespace DevPostIt.Desktop;

public enum TrayCommand
{
    NewNote, Search, Capture, Formatter, MindMap, Todo, Calendar, ImportMarkdown,
    NoteList, Trash, Settings, Help, About, ShowAll, CollapseAll, Exit,
}

public sealed record TrayMenuEntry(TrayCommand? Command, string Title, bool Enabled)
{
    public bool IsSeparator => Command is null;
}

// 트레이 메뉴 항목을 데이터로 두어 운영체제별 메뉴가 어긋나지 않게 합니다.
public static class TrayMenu
{
    private static readonly (TrayCommand? Command, string Title)[] Items =
    [
        (TrayCommand.NewNote, "새 메모"),
        (TrayCommand.Search, "메모 검색"),
        (TrayCommand.Capture, "화면 캡처"),
        (TrayCommand.Formatter, "소스코드 포맷터"),
        (TrayCommand.MindMap, "마인드맵"),
        (TrayCommand.Todo, "할 일"),
        (TrayCommand.Calendar, "달력"),
        (TrayCommand.ImportMarkdown, "Markdown 가져오기…"),
        (TrayCommand.NoteList, "메모 목록"),
        (TrayCommand.Trash, "휴지통"),
        (TrayCommand.Settings, "설정"),
        (TrayCommand.Help, "도움말"),
        (TrayCommand.About, "정보"),
        (null, ""),
        (TrayCommand.ShowAll, "모두 표시"),
        (TrayCommand.CollapseAll, "모두 접기"),
        (null, ""),
        (TrayCommand.Exit, "종료"),
    ];

    public static IReadOnlyList<TrayMenuEntry> Build(IReadOnlySet<TrayCommand> available) =>
        [.. Items.Select(item => new TrayMenuEntry(item.Command, item.Title, item.Command is { } command && available.Contains(command)))];
}
