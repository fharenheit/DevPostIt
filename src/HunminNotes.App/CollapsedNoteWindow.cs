using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace HunminNotes.App;

public sealed class CollapsedNoteWindow : Window
{
    public CollapsedNoteWindow(string title, Action restore)
    {
        Title = $"작은 메모 — {title}";
        Width = Height = 24; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; AllowsTransparency = true; Background = Brushes.Transparent;
        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 232, 143)), BorderBrush = new SolidColorBrush(Color.FromRgb(205, 178, 86)),
            ToolTip = $"{title}\n클릭: 열기 · 드래그: 이동", Cursor = Cursors.Hand
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{title} 열기");
        Point start = default;
        var dragged = false;
        button.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(this); dragged = false; };
        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragged || (e.GetPosition(this) - start).Length < 5) return;
            dragged = true; e.Handled = true; DragMove();
        };
        button.Click += (_, _) => { if (!dragged) restore(); };
        Content = button;
    }
}
