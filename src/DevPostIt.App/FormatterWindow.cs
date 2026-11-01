using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DevPostIt.EditorHost;
using Microsoft.Web.WebView2.Wpf;
using Color = System.Windows.Media.Color;

namespace DevPostIt.App;

// 소스코드 포맷 창입니다. 메모와 별개이며 입력을 저장하지 않고, 페이지의 메시지를 받지 않습니다.
public sealed class FormatterWindow : Window
{
    internal WebView2 View { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245) };
    public FormatterWindow()
    {
        Title = "소스코드 포맷터";
        Icon = NoteIcon.WindowImage;
        Width = 760; Height = 680; MinWidth = 420; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Content = View;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) =>
        {
            try
            {
                var core = await LocalWebView.ConfigureAsync(View, EditorOrigin.IsFormatterPage);
                core.Navigate(EditorOrigin.FormatterPage);
            }
            catch (Exception error) { Content = new TextBlock { Text = $"포맷터를 열지 못했습니다.\n{error.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; }
        };
        Closed += (_, _) => View.Dispose();
    }
}
