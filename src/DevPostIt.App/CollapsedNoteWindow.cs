using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace DevPostIt.App;

public sealed class CollapsedNoteWindow : Window
{
    public CollapsedNoteWindow(string title, Action restore, string color = EditorHost.NoteColors.Default)
    {
        Title = $"작은 메모 — {title}";
        Width = Height = 24; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent;
        var (fill, border) = NoteAppearance.IconColors(color);
        var button = new Button
        {
            Background = new SolidColorBrush(fill), BorderBrush = new SolidColorBrush(border),
            ToolTip = $"{title}\n클릭: 열기 · 드래그: 이동", Cursor = Cursors.Hand, Template = CreateIconTemplate()
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{title} 열기");
        Point start = default;
        var dragged = false;
        button.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(this); dragged = false; };
        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragged || (e.GetPosition(this) - start).Length < 5) return;
            dragged = true; e.Handled = true;
            // 버튼이 마우스 캡처를 잡고 있으면 창 이동이 시작되지 않습니다.
            button.ReleaseMouseCapture();
            DragMove();
        };
        button.Click += (_, _) => { if (!dragged) restore(); };
        Content = button;
    }
    // 기본 버튼 템플릿은 마우스를 올리면 파란색으로 바뀌므로 메모지 색을 유지하고 테두리만 진하게 합니다.
    private static ControlTemplate CreateIconTemplate()
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "chrome");
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(122, 92, 0)), "chrome"));
        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        template.Triggers.Add(hover);
        return template;
    }
}

internal static class NoteAppearance
{
    // 제목 표시줄 색(0x00BBGGRR)과 접힌 아이콘 색입니다.
    public static int CaptionColor(string? color)
    {
        var caption = Desktop.NotePalette.Caption(color);
        return caption.R | caption.G << 8 | caption.B << 16;
    }
    public static (Color Fill, Color Border) IconColors(string? color)
    {
        var (fill, border) = Desktop.NotePalette.Icon(color);
        return (Color.FromRgb(fill.R, fill.G, fill.B), Color.FromRgb(border.R, border.G, border.B));
    }
}
