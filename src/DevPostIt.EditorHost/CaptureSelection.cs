namespace DevPostIt.EditorHost;

// 캡처 중 커서 입력을 영역으로 바꾸는 규칙입니다. 입력을 받는 방법은 운영체제마다 다릅니다.
public sealed class CaptureSelection(ScreenRect screen)
{
    public const int MaxLevelsUp = 12;
    private const int DragThreshold = 5, HoverSlack = 2;
    private (int X, int Y)? hovered;
    private (int X, int Y)? pressed;
    private ScreenRect? element;
    private ScreenRect? dragged;

    public int LevelsUp { get; private set; }
    public bool IsDragging { get; private set; }
    public ScreenRect? Highlight => IsDragging ? dragged : element;

    // 커서가 움직이면 다시 가장 안쪽 요소부터 고릅니다.
    public void Hover(int x, int y, ScreenRect? found)
    {
        if (pressed is not null) return;
        if (hovered is not { } last || Math.Abs(x - last.X) > HoverSlack || Math.Abs(y - last.Y) > HoverSlack)
        {
            if (hovered is not null) LevelsUp = 0;
            hovered = (x, y);
        }
        element = found is { } rect ? CaptureGeometry.ClampToScreen(rect.X, rect.Y, rect.Width, rect.Height, screen) : null;
    }

    public void Wheel(bool down) => LevelsUp = down ? Math.Max(0, LevelsUp - 1) : Math.Min(MaxLevelsUp, LevelsUp + 1);

    public void Press(int x, int y) => pressed = (x, y);

    public void Move(int x, int y)
    {
        if (pressed is not { } start) return;
        if (!IsDragging && Math.Abs(x - start.X) < DragThreshold && Math.Abs(y - start.Y) < DragThreshold) return;
        IsDragging = true;
        dragged = CaptureGeometry.FromDrag(start.X, start.Y, x, y, screen);
    }

    public ScreenRect? Release(int x, int y)
    {
        Move(x, y);
        return Highlight;
    }
}
