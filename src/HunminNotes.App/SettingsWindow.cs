using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HunminNotes.EditorHost;
using Microsoft.Web.WebView2.Wpf;
using Color = System.Windows.Media.Color;

namespace HunminNotes.App;

// 메모와 별개인 설정 창입니다. 닫아도 메모에 영향을 주지 않으며 삭제 확인을 거치지 않습니다.
public sealed class SettingsWindow : Window
{
    internal WebView2 View { get; } = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 253, 245) };
    public SettingsWindow()
    {
        Title = "설정";
        Width = 380; Height = 300; MinWidth = 320; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        Content = View;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) =>
        {
            try { (await LocalWebView.ConfigureAsync(View, EditorOrigin.IsSettingsPage)).Navigate(EditorOrigin.SettingsPage); }
            catch (Exception error) { Content = new TextBlock { Text = $"설정을 열지 못했습니다.\n{error.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; }
        };
        Closed += (_, _) => View.Dispose();
    }
}
