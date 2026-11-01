using System.Windows;
using System.Windows.Threading;
using DevPostIt.Storage;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using MessageBox = System.Windows.MessageBox;

namespace DevPostIt.App;

public sealed class TrayService : IDisposable
{
    private const int MaxListedNotes = 15;
    internal Forms.ContextMenuStrip Menu => menu;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Drawing.Icon icon = NoteIcon.Create();
    private readonly GlobalHotkey hotkey;
    internal bool IsHotkeyRegistered => hotkey.IsRegistered;
    public void Notify(string message) => tray.ShowBalloonTip(5000, "메모", message.Length > 200 ? message[..200] : message, Forms.ToolTipIcon.Warning);
    public TrayService(DesktopNoteManager manager, NoteWorkspace workspace, Dispatcher dispatcher)
    {
        void Run(Func<Task> action) => dispatcher.BeginInvoke(new Action(() => _ = DesktopNoteManager.RunAsync(action)));
        tray = new Forms.NotifyIcon { Text = "메모장", Icon = icon, ContextMenuStrip = menu, Visible = true };
        hotkey = new GlobalHotkey(() => Run(manager.StartObjectCaptureAsync));
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Run(manager.CreateAsync); };
        manager.UpdateFound += update => dispatcher.BeginInvoke(new Action(() => tray.ShowBalloonTip(5000, "메모", $"새 버전 {update.Version}이 있습니다. 트레이 메뉴에서 설치할 수 있습니다.", Forms.ToolTipIcon.Info)));
        menu.Opening += (_, _) =>
        {
            foreach (var old in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) old.Dispose();
            menu.Items.Clear();
            menu.Items.Add("새 메모", null, (_, _) => Run(manager.CreateAsync));
            menu.Items.Add("메모 검색", null, (_, _) => manager.ShowSearch());
            menu.Items.Add("화면 캡처", null, (_, _) => Run(manager.StartObjectCaptureAsync));
            menu.Items.Add("Markdown 가져오기…", null, (_, _) => Run(manager.ImportMarkdownAsync));
            var notes = new Forms.ToolStripMenuItem("메모 목록");
            var all = workspace.Notes;
            foreach (var note in all.Take(MaxListedNotes)) notes.DropDownItems.Add(Label(note.Title), null, (_, _) => manager.ShowNote(note.Id));
            if (all.Count > MaxListedNotes)
            {
                notes.DropDownItems.Add(new Forms.ToolStripSeparator());
                notes.DropDownItems.Add($"전체 {all.Count}개 검색…", null, (_, _) => manager.ShowSearch());
            }
            menu.Items.Add(notes);
            menu.Items.Add(TrashMenu(manager, workspace, Run));
            menu.Items.Add("설정", null, (_, _) => manager.ShowSettings());
            menu.Items.Add("도움말", null, (_, _) => manager.ShowHelp());
            menu.Items.Add("정보", null, (_, _) => manager.ShowAbout());
            if (manager.AvailableUpdate is { } update) menu.Items.Add($"업데이트 설치 ({update.Version})", null, (_, _) =>
            {
                if (MessageBox.Show($"버전 {update.Version}을 내려받아 설치하고 앱을 다시 시작합니다.\n이 파일은 코드 서명이 없으며 GitHub 릴리즈의 해시로 확인합니다.\n\n계속할까요?", "업데이트", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                    Run(manager.InstallUpdateAsync);
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("모두 표시", null, (_, _) => manager.ShowAll());
            menu.Items.Add("모두 접기", null, (_, _) => manager.CollapseAll());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => Run(manager.ExitAsync));
        };
    }
    private static string Label(string title) => title.Replace("&", "&&");
    private static Forms.ToolStripMenuItem TrashMenu(DesktopNoteManager manager, NoteWorkspace workspace, Action<Func<Task>> run)
    {
        var menu = new Forms.ToolStripMenuItem("휴지통");
        var items = workspace.TrashedNotes;
        if (items.Count == 0) { menu.DropDownItems.Add(new Forms.ToolStripMenuItem("비어 있음") { Enabled = false }); return menu; }
        foreach (var item in items.Take(MaxListedNotes))
        {
            var entry = new Forms.ToolStripMenuItem(Label(item.Title));
            entry.DropDownItems.Add("복원", null, (_, _) => run(() => manager.RestoreFromTrashAsync(item.Id)));
            entry.DropDownItems.Add("영구 삭제", null, (_, _) =>
            {
                if (Confirm($"‘{item.Title}’ 메모를 영구 삭제하시겠습니까?\n되돌릴 수 없습니다.")) run(() => workspace.PurgeAsync(item.Id, CancellationToken.None));
            });
            menu.DropDownItems.Add(entry);
        }
        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        menu.DropDownItems.Add("휴지통 비우기", null, (_, _) =>
        {
            if (Confirm($"휴지통의 메모 {items.Count}개를 영구 삭제하시겠습니까?\n되돌릴 수 없습니다.")) run(() => workspace.EmptyTrashAsync(CancellationToken.None));
        });
        return menu;
    }
    private static bool Confirm(string message) => MessageBox.Show(message, "휴지통", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    public void Dispose() { hotkey.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); icon.Dispose(); }
}
