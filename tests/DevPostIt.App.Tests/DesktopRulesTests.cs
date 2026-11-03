using DevPostIt.Desktop;
using DevPostIt.Storage;

namespace DevPostIt.App.Tests;

public sealed class DesktopRulesTests
{
    // Windows 제목 표시줄에 쓰던 0x00BBGGRR 값과 같아야 합니다.
    [Theory]
    [InlineData("yellow", 0x00BFF3FF)]
    [InlineData("green", 0x00DCF3D8)]
    [InlineData("blue", 0x00F8EAD6)]
    [InlineData("pink", 0x00E8DDFB)]
    [InlineData("purple", 0x00F7DFE6)]
    [InlineData("gray", 0x00E1E6E8)]
    [InlineData(null, 0x00BFF3FF)]
    [InlineData("unknown", 0x00BFF3FF)]
    public void Caption_colors_match_windows(string? color, int bgr)
    {
        var caption = NotePalette.Caption(color);

        Assert.Equal(bgr, caption.R | caption.G << 8 | caption.B << 16);
    }

    [Fact]
    public void Icon_colors_keep_the_yellow_default()
    {
        Assert.Equal(NotePalette.Icon("yellow"), NotePalette.Icon("unknown"));
        Assert.Equal(new NotePalette.Rgb(255, 232, 143), NotePalette.Icon("yellow").Fill);
        Assert.Equal(new NotePalette.Rgb(92, 146, 190), NotePalette.Icon("blue").Border);
    }

    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void Restore_opens_the_first_note_when_nothing_was_open() =>
        Assert.Equal([new SessionRestore.Item(A, false)], SessionRestore.Plan([A, B], new AppSettings()));

    [Fact]
    public void Restore_opens_known_notes_and_keeps_them_collapsed()
    {
        var settings = new AppSettings { OpenNotes = [B, C, A], CollapsedNotes = [A] };

        Assert.Equal([new SessionRestore.Item(B, false), new SessionRestore.Item(A, true)], SessionRestore.Plan([A, B], settings));
    }

    [Fact]
    public void Restore_opens_nothing_when_starting_in_the_tray() =>
        Assert.Empty(SessionRestore.Plan([A], new AppSettings { StartInTray = true, OpenNotes = [A] }));

    [Theory]
    [InlineData(100, 200, 900, 600)] // 왼쪽 위 기준 Top 100, 높이 200, 주 화면 900 → 왼쪽 아래 기준 Y 600
    [InlineData(0, 900, 900, 0)]
    [InlineData(-50, 100, 900, 850)]
    public void Screen_coordinates_flip_between_origins(double top, double height, double screen, double cocoaY)
    {
        Assert.Equal(cocoaY, ScreenCoordinates.FlipY(top, height, screen));
        Assert.Equal(top, ScreenCoordinates.FlipY(cocoaY, height, screen));
    }
}
