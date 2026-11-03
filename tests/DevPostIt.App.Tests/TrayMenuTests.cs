using DevPostIt.Desktop;

namespace DevPostIt.App.Tests;

public sealed class TrayMenuTests
{
    private static readonly string[] WindowsOrder =
    [
        "새 메모", "메모 검색", "화면 캡처", "소스코드 포맷터", "마인드맵", "할 일", "달력", "Markdown 가져오기…",
        "메모 목록", "휴지통", "설정", "도움말", "정보", "-", "모두 표시", "모두 접기", "-", "종료",
    ];

    [Fact]
    public void Build_lists_the_windows_tray_items_in_order()
    {
        var titles = TrayMenu.Build(new HashSet<TrayCommand>()).Select(entry => entry.IsSeparator ? "-" : entry.Title);

        Assert.Equal(WindowsOrder, titles);
    }

    [Fact]
    public void Build_enables_only_the_available_commands()
    {
        var entries = TrayMenu.Build(new HashSet<TrayCommand> { TrayCommand.Exit });

        var enabled = Assert.Single(entries, entry => entry.Enabled);
        Assert.Equal(TrayCommand.Exit, enabled.Command);
        Assert.Equal("종료", enabled.Title);
    }

    [Fact]
    public void Build_never_enables_a_separator()
    {
        var all = Enum.GetValues<TrayCommand>().ToHashSet();

        Assert.All(TrayMenu.Build(all), entry => Assert.Equal(!entry.IsSeparator, entry.Enabled));
    }
}
