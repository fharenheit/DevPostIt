using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevPostIt.App;

// 0.2.0은 클립보드 비트맵의 알파값 0을 그대로 저장해 그림이 보이지 않았습니다.
// 앱이 저장한 이미지 중 완전히 투명한 파일만 찾아 색상은 두고 불투명도만 고칩니다.
internal static partial class NoteImageRepair
{
    [GeneratedRegex("^[0-9a-f]{32}$")] private static partial Regex AppFileName();

    public static int Run(string assetsRoot)
    {
        if (!Directory.Exists(assetsRoot)) return 0;
        var repaired = 0;
        foreach (var directory in Directory.EnumerateDirectories(assetsRoot))
        {
            if (!AppFileName().IsMatch(Path.GetFileName(directory)) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
            {
                if (!AppFileName().IsMatch(Path.GetFileNameWithoutExtension(file))) continue;
                try { if (RepairFile(file)) repaired++; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { /* 읽지 못한 파일은 그대로 둡니다. */ }
            }
        }
        return repaired;
    }

    private static bool RepairFile(string path)
    {
        BitmapFrame source;
        using (var input = File.OpenRead(path)) source = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] != 0) return false;
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride)));
        var temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write)) encoder.Save(output);
        File.Move(temporary, path, overwrite: true);
        return true;
    }
}
