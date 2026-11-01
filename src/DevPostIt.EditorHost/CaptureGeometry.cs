namespace DevPostIt.EditorHost;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

// 화면 캡처 영역 계산입니다. 좌표는 모두 같은 화면 좌표계를 씁니다.
public static class CaptureGeometry
{
    private const int MinSize = 4;

    public static readonly IReadOnlyList<int> DelaySeconds = [0, 3, 5, 10];
    // 어느 방향으로 끌어도 같은 영역이 되게 합니다.
    public static ScreenRect? FromDrag(int startX, int startY, int endX, int endY, ScreenRect screen) =>
        ClampToScreen(Math.Min(startX, endX), Math.Min(startY, endY), Math.Abs(endX - startX), Math.Abs(endY - startY), screen);

    // 요소 영역을 화면 안으로 자릅니다. 비정상 값이거나 너무 작으면 캡처하지 않습니다.
    public static ScreenRect? ClampToScreen(double x, double y, double width, double height, ScreenRect screen)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return null;
        var left = Math.Max(Math.Floor(x), screen.X);
        var top = Math.Max(Math.Floor(y), screen.Y);
        var right = Math.Min(Math.Ceiling(x + width), (double)screen.X + screen.Width);
        var bottom = Math.Min(Math.Ceiling(y + height), (double)screen.Y + screen.Height);
        if (right - left < MinSize || bottom - top < MinSize) return null;
        return new ScreenRect((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }
}
