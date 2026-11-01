using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace DevPostIt.App;

// 제품 정보 창입니다. 정해진 저장소 주소만 엽니다.
public sealed class AboutWindow : Window
{
    public const string RepositoryUrl = "https://github.com/fharenheit/DevPostIt";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    private const string Developer = "Edward KIM (fharenheit)";
    private const string Components = ".NET, WebView2, Monaco Editor, Prettier, markdown-it, DOMPurify, SQLite, D2Coding, JetBrains Mono";
    internal string VersionText { get; }
    internal Action<string> LinkOpener { get; set; } = link => Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();

    public AboutWindow()
    {
        VersionText = $"버전 {DesktopNoteManager.CurrentVersion.ToString(3)}";
        Title = "정보";
        Icon = NoteIcon.WindowImage;
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Foreground = new SolidColorBrush(Color.FromRgb(47, 42, 31));
        FontSize = 14;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Malgun Gothic");
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = "Post It for Developer", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        content.Children.Add(new TextBlock { Text = "개발자를 위한 Markdown 포스트잇 메모", Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBlock { Text = VersionText, Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock { Text = $"개발자: {Developer}", Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(Link("GitHub: ", RepositoryUrl));
        content.Children.Add(Link("릴리즈: ", ReleasesUrl));
        content.Children.Add(new TextBlock { Text = $"사용한 오픈소스: {Components}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        Content = content;
    }

    internal void OpenRepository() => Open(RepositoryUrl);
    private void Open(string link)
    {
        try { LinkOpener(link); }
        catch (Exception error) { System.Windows.MessageBox.Show(this, $"주소를 열지 못했습니다.\n{link}\n{error.Message}", "정보", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private TextBlock Link(string label, string url)
    {
        var link = new Hyperlink(new Run(url)) { Foreground = new SolidColorBrush(Color.FromRgb(122, 92, 0)) };
        link.Click += (_, _) => Open(url);
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        text.Inlines.Add(label);
        text.Inlines.Add(link);
        return text;
    }
}
