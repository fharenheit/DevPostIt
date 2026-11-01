using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DevPostIt.EditorHost;
using Color = System.Windows.Media.Color;

namespace DevPostIt.App;

// 기능 안내 창입니다. 글자 크기는 메모 설정과 관계없이 13으로 고정합니다.
public sealed class HelpWindow : Window
{
    private const double TextSize = 13;
    private readonly List<TextBlock> texts = [];
    internal IReadOnlyList<TextBlock> Texts => texts;

    public HelpWindow()
    {
        Title = "도움말";
        Icon = NoteIcon.WindowImage;
        Width = 520; Height = 640; MinWidth = 320; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Foreground = new SolidColorBrush(Color.FromRgb(47, 42, 31));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Malgun Gothic");
        FontSize = TextSize;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        var content = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        foreach (var section in HelpContent.Sections)
        {
            content.Children.Add(Text(section.Title, FontWeights.SemiBold, new Thickness(0, 12, 0, 4)));
            foreach (var paragraph in section.Paragraphs) content.Children.Add(Text(paragraph, FontWeights.Normal, new Thickness(0, 0, 0, 6)));
        }
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
    }

    private TextBlock Text(string text, FontWeight weight, Thickness margin)
    {
        var block = new TextBlock { Text = text, FontSize = TextSize, FontWeight = weight, Margin = margin, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        texts.Add(block);
        return block;
    }
}
