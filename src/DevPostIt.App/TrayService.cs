using System.Windows.Threading;
using DevPostIt.Storage;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DevPostIt.App;

public sealed class TrayService : IDisposable
{
    internal Forms.ContextMenuStrip Menu => menu;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Drawing.Icon icon = NoteIcon.Create();
    public TrayService(DesktopNoteManager manager, NoteWorkspace workspace, Dispatcher dispatcher)
    {
        void Run(Func<Task> action) => dispatcher.BeginInvoke(new Action(() => _ = DesktopNoteManager.RunAsync(action)));
        tray = new Forms.NotifyIcon { Text = "메모장", Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Run(manager.CreateAsync); };
        menu.Opening += (_, _) =>
        {
            foreach (var old in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) old.Dispose();
            menu.Items.Clear();
            menu.Items.Add("새 메모", null, (_, _) => Run(manager.CreateAsync));
            var notes = new Forms.ToolStripMenuItem("메모 목록");
            foreach (var note in workspace.Notes) notes.DropDownItems.Add(note.Title.Replace("&", "&&"), null, (_, _) => manager.ShowNote(note.Id));
            menu.Items.Add(notes);
            menu.Items.Add("설정", null, (_, _) => manager.ShowSettings());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("모두 표시", null, (_, _) => manager.ShowAll());
            menu.Items.Add("모두 접기", null, (_, _) => manager.CollapseAll());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => Run(manager.ExitAsync));
        };
    }
    public void Dispose() { tray.Visible = false; tray.Dispose(); menu.Dispose(); icon.Dispose(); }
}
