namespace DevPostIt.EditorHost;

public enum AnnotationTool { Arrow, Rectangle, Text, Mosaic }
public sealed record Annotation(AnnotationTool Tool, double X1, double Y1, double X2, double Y2, uint Color, string Text = "");

// 캡처 주석의 계산입니다. 좌표는 모두 캡처 그림의 픽셀입니다.
public static class CaptureAnnotations
{
    public const uint Red = 0xE5383B, Blue = 0x1565C0, Black = 0x202020, Yellow = 0xF2C200;
    public const int MosaicBlock = 16;

    public static ScreenRect? Area(double x1, double y1, double x2, double y2, int width, int height)
    {
        var left = (int)Math.Max(Math.Floor(Math.Min(x1, x2)), 0);
        var top = (int)Math.Max(Math.Floor(Math.Min(y1, y2)), 0);
        var right = (int)Math.Min(Math.Ceiling(Math.Max(x1, x2)), width);
        var bottom = (int)Math.Min(Math.Ceiling(Math.Max(y1, y2)), height);
        return right > left && bottom > top ? new ScreenRect(left, top, right - left, bottom - top) : null;
    }

    // BGRA 픽셀의 영역을 블록 평균색으로 바꿉니다. 원래 무늬는 결과 그림에 남지 않습니다.
    public static void Pixelate(byte[] pixels, int width, int height, ScreenRect area, int block)
    {
        var endX = Math.Min(area.X + area.Width, width);
        var endY = Math.Min(area.Y + area.Height, height);
        for (var top = Math.Max(area.Y, 0); top < endY; top += block)
            for (var left = Math.Max(area.X, 0); left < endX; left += block)
            {
                var (right, bottom) = (Math.Min(left + block, endX), Math.Min(top + block, endY));
                var count = (right - left) * (bottom - top);
                int blue = 0, green = 0, red = 0, alpha = 0;
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                    {
                        var at = (y * width + x) * 4;
                        blue += pixels[at]; green += pixels[at + 1]; red += pixels[at + 2]; alpha += pixels[at + 3];
                    }
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                    {
                        var at = (y * width + x) * 4;
                        pixels[at] = (byte)(blue / count); pixels[at + 1] = (byte)(green / count); pixels[at + 2] = (byte)(red / count); pixels[at + 3] = (byte)(alpha / count);
                    }
            }
    }

    // 첫 점이 화살촉 끝입니다.
    public static (double X, double Y)[] ArrowHead(double x1, double y1, double x2, double y2, double size)
    {
        const double spread = Math.PI / 7;
        var angle = Math.Atan2(y2 - y1, x2 - x1);
        return [(x2, y2),
            (x2 - size * Math.Cos(angle - spread), y2 - size * Math.Sin(angle - spread)),
            (x2 - size * Math.Cos(angle + spread), y2 - size * Math.Sin(angle + spread))];
    }
}
