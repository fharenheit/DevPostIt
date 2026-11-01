using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace DevPostIt.App;

// 트레이와 창 제목 표시줄이 같은 노란 포스트잇 아이콘을 쓰도록 합니다.
internal static class NoteIcon
{
    public static BitmapSource WindowImage { get; } = CreateWindowImage();
    public static Drawing.Icon Create()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(Drawing.Color.Transparent);
        using var paper = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 232, 143));
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(91, 78, 34), 2);
        graphics.FillRectangle(paper, 5, 3, 23, 27);
        graphics.DrawRectangle(pen, 5, 3, 23, 27);
        for (var y = 10; y <= 24; y += 6) graphics.DrawLine(pen, 10, y, 23, y);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Drawing.Icon.FromHandle(handle); return (Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    private static BitmapSource CreateWindowImage()
    {
        using var icon = Create();
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
