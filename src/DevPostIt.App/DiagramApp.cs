using System.Diagnostics;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DevPostIt.App;

// 다이어그램 편집은 설치된 draw.io Desktop에 맡깁니다. 앱 안에 편집기를 넣지 않습니다.
internal static class DiagramApp
{
    private const string DownloadPage = "https://github.com/jgraph/drawio-desktop/releases/latest";
    public static string? Find()
    {
        string[] candidates =
        [
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "draw.io", "draw.io.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "draw.io", "draw.io.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "draw.io.exe"),
        ];
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
    public static void Open(string application, string file)
    {
        var start = new ProcessStartInfo(application) { UseShellExecute = false };
        start.ArgumentList.Add(file);
        Process.Start(start)?.Dispose();
    }
    public static void OfferDownload(Window owner)
    {
        var answer = MessageBox.Show(owner, "다이어그램을 편집하려면 draw.io Desktop이 필요합니다.\n내려받기 페이지를 기본 브라우저로 열까요?", "draw.io", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true })?.Dispose();
    }
}
