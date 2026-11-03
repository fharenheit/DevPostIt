using CoreGraphics;
using DevPostIt.Desktop;
using DevPostIt.Storage;

namespace DevPostIt.Mac;

// Windows의 트레이에 해당하는 메뉴 막대 아이콘입니다. 왼쪽 클릭은 새 메모, 오른쪽이나 ⌃클릭은 메뉴입니다.
internal sealed class StatusBarMenu
{
    private const int MaxListedNotes = 15;
    private const int MaxTitleLength = 40;
    private const int MenuGap = 4;
    private static readonly IReadOnlySet<TrayCommand> Available = new HashSet<TrayCommand>
    {
        TrayCommand.NewNote, TrayCommand.NoteList, TrayCommand.Trash, TrayCommand.ShowAll, TrayCommand.CollapseAll, TrayCommand.Exit,
    };
    private readonly NSStatusItem item = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Square);
    private readonly MacNoteManager manager;
    private readonly NoteWorkspace workspace;

    // 시험은 화면에 메뉴를 띄우지 않고 이 값을 바꿉니다.
    internal Action<NSMenu> PresentMenu { get; set; }

    public StatusBarMenu(MacNoteManager manager, NoteWorkspace workspace)
    {
        this.manager = manager;
        this.workspace = workspace;
        PresentMenu = OpenBelowButton;
        var button = item.Button;
        button.Image = CreateIcon();
        button.ToolTip = "메모장";
        // 바인딩의 인수 형식은 NSEventType이지만 Cocoa가 받는 값은 NSEventMask입니다.
        button.SendActionOn((NSEventType)(ulong)(NSEventMask.LeftMouseUp | NSEventMask.RightMouseUp));
        button.Activated += (_, _) => HandleClick(NSApplication.SharedApplication.CurrentEvent);
    }

    internal void HandleClick(NSEvent? current)
    {
        var showMenu = current?.Type == NSEventType.RightMouseUp || current?.ModifierFlags.HasFlag(NSEventModifierMask.ControlKeyMask) == true;
        if (!showMenu) { _ = MacNoteManager.RunAsync(manager.CreateAsync); return; }
        // 클릭 처리가 끝난 뒤에 엽니다. 처리 중에 열면 메뉴 추적이 이 클릭과 겹칩니다.
        var menu = CreateMenu();
        item.Button.BeginInvokeOnMainThread(() => PresentMenu(menu));
    }

    private void OpenBelowButton(NSMenu menu)
    {
        var button = item.Button;
        menu.PopUpMenu(null, new CGPoint(0, button.IsFlipped ? button.Bounds.Height + MenuGap : -MenuGap), button);
    }

    private NSMenu CreateMenu()
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        foreach (var entry in TrayMenu.Build(Available))
        {
            if (entry.IsSeparator) { menu.AddItem(NSMenuItem.SeparatorItem); continue; }
            var menuItem = new NSMenuItem(entry.Title, (_, _) => Run(entry.Command!.Value)) { Enabled = entry.Enabled };
            if (entry.Command == TrayCommand.NoteList) menuItem.Submenu = CreateNoteList();
            if (entry.Command == TrayCommand.Trash) menuItem.Submenu = CreateTrash();
            menu.AddItem(menuItem);
        }
        return menu;
    }

    private NSMenu CreateNoteList()
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        var all = workspace.Notes;
        foreach (var note in all.Take(MaxListedNotes)) menu.AddItem(new NSMenuItem(Label(note.Title), (_, _) => manager.ShowNote(note.Id)));
        if (all.Count > MaxListedNotes) menu.AddItem(new NSMenuItem($"전체 {all.Count}개 중 최근 {MaxListedNotes}개") { Enabled = false });
        return menu;
    }

    private NSMenu CreateTrash()
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        var items = workspace.TrashedNotes;
        if (items.Count == 0) { menu.AddItem(new NSMenuItem("비어 있음") { Enabled = false }); return menu; }
        foreach (var note in items.Take(MaxListedNotes))
        {
            var entry = new NSMenuItem(Label(note.Title));
            var actions = new NSMenu { AutoEnablesItems = false };
            actions.AddItem(new NSMenuItem("복원", (_, _) => _ = MacNoteManager.RunAsync(() => manager.RestoreFromTrashAsync(note.Id))));
            actions.AddItem(new NSMenuItem("영구 삭제", (_, _) =>
            {
                if (MacDialogs.Ask($"‘{note.Title}’ 메모를 영구 삭제하시겠습니까?", "되돌릴 수 없습니다.", "아니요", "예") == 1)
                    _ = MacNoteManager.RunAsync(() => manager.PurgeAsync(note.Id));
            }));
            entry.Submenu = actions;
            menu.AddItem(entry);
        }
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("휴지통 비우기", (_, _) =>
        {
            if (MacDialogs.Ask($"휴지통의 메모 {items.Count}개를 영구 삭제하시겠습니까?", "되돌릴 수 없습니다.", "아니요", "예") == 1)
                _ = MacNoteManager.RunAsync(() => workspace.EmptyTrashAsync(CancellationToken.None));
        }));
        return menu;
    }

    private static string Label(string title) => title.Length <= MaxTitleLength ? title : title[..MaxTitleLength] + "…";

    private void Run(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.NewNote: _ = MacNoteManager.RunAsync(manager.CreateAsync); break;
            case TrayCommand.ShowAll: manager.ShowAll(); break;
            case TrayCommand.CollapseAll: manager.CollapseAll(); break;
            case TrayCommand.Exit: NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication); break;
        }
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
