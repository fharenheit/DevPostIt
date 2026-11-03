using DevPostIt.EditorHost;

namespace DevPostIt.App.Tests;

public sealed class CaptureSelectionTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1000, 800);

    [Fact]
    public void Click_takes_the_highlighted_element()
    {
        var selection = new CaptureSelection(Screen);
        selection.Hover(10, 10, new ScreenRect(5, 5, 100, 50));
        selection.Press(10, 10);
        selection.Move(11, 11);
        Assert.Equal(new ScreenRect(5, 5, 100, 50), selection.Release(11, 11));
    }

    [Fact]
    public void Drag_takes_the_dragged_area_in_any_direction()
    {
        var selection = new CaptureSelection(Screen);
        selection.Hover(200, 200, new ScreenRect(5, 5, 100, 50));
        selection.Press(200, 200);
        selection.Move(150, 120);
        Assert.Equal(new ScreenRect(150, 120, 50, 80), selection.Highlight);
        Assert.Equal(new ScreenRect(150, 120, 50, 80), selection.Release(150, 120));
    }

    [Fact]
    public void Small_movement_is_still_a_click()
    {
        var selection = new CaptureSelection(Screen);
        selection.Hover(100, 100, new ScreenRect(90, 90, 30, 30));
        selection.Press(100, 100);
        selection.Move(104, 96);
        Assert.Equal(new ScreenRect(90, 90, 30, 30), selection.Highlight);
    }

    [Fact]
    public void Dragged_area_stays_on_screen()
    {
        var selection = new CaptureSelection(Screen);
        selection.Press(900, 700);
        Assert.Equal(new ScreenRect(900, 700, 100, 100), selection.Release(1500, 1500));
    }

    [Fact]
    public void Click_with_nothing_highlighted_selects_nothing()
    {
        var selection = new CaptureSelection(Screen);
        selection.Press(10, 10);
        Assert.Null(selection.Release(10, 10));
    }

    [Fact]
    public void Wheel_moves_levels_within_limits_and_cursor_movement_resets_them()
    {
        var selection = new CaptureSelection(Screen);
        selection.Wheel(down: true);
        Assert.Equal(0, selection.LevelsUp);
        for (var count = 0; count < CaptureSelection.MaxLevelsUp + 5; count++) selection.Wheel(down: false);
        Assert.Equal(CaptureSelection.MaxLevelsUp, selection.LevelsUp);
        selection.Hover(100, 100, null);
        selection.Hover(101, 101, null);
        Assert.Equal(CaptureSelection.MaxLevelsUp, selection.LevelsUp);
        selection.Hover(110, 110, null);
        Assert.Equal(0, selection.LevelsUp);
    }

    [Fact]
    public void Element_is_clamped_to_the_screen()
    {
        var selection = new CaptureSelection(Screen);
        selection.Hover(10, 10, new ScreenRect(-50, -50, 100, 100));
        Assert.Equal(new ScreenRect(0, 0, 50, 50), selection.Highlight);
    }
}
