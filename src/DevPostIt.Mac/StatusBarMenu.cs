using CoreGraphics;
using DevPostIt.Desktop;

namespace DevPostIt.Mac;

// Windows의 트레이에 해당하는 메뉴 막대 아이콘입니다.
internal sealed class StatusBarMenu
{
    // 아직 구현하지 않은 항목은 비활성으로 표시합니다. 새 메모를 연결하면 왼쪽 클릭을 새 메모로 바꿉니다.
    private static readonly IReadOnlySet<TrayCommand> Available = new HashSet<TrayCommand> { TrayCommand.Exit };
    private readonly NSStatusItem item = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Square);

    public StatusBarMenu()
    {
        item.Button.Image = CreateIcon();
        item.Button.ToolTip = "메모장";
        item.Menu = CreateMenu();
    }

    private static NSMenu CreateMenu()
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        foreach (var entry in TrayMenu.Build(Available))
        {
            if (entry.IsSeparator) { menu.AddItem(NSMenuItem.SeparatorItem); continue; }
            var menuItem = new NSMenuItem(entry.Title, (_, _) => Run(entry.Command!.Value)) { Enabled = entry.Enabled };
            menu.AddItem(menuItem);
        }
        return menu;
    }

    private static void Run(TrayCommand command)
    {
        if (command == TrayCommand.Exit) NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);
    }

    // Windows 아이콘과 같은 줄 있는 메모지입니다. 템플릿 이미지라 메뉴 막대의 밝기에 맞춰 색이 바뀝니다.
    private static NSImage CreateIcon()
    {
        var image = NSImage.ImageWithSize(new CGSize(18, 18), false, rect =>
        {
            NSColor.Black.SetStroke();
            var paper = NSBezierPath.FromRect(new CGRect(3.5, 1.5, 11, 15));
            paper.LineWidth = 1.5f;
            paper.Stroke();
            for (var y = 5; y <= 13; y += 4)
            {
                var line = new NSBezierPath { LineWidth = 1.5f };
                line.MoveTo(new CGPoint(6, y));
                line.LineTo(new CGPoint(12, y));
                line.Stroke();
            }
            return true;
        });
        image.Template = true;
        image.AccessibilityDescription = "메모장";
        return image;
    }
}
