using System.Windows;
using HunminNotes.Storage;
using HunminNotes.Core;
using HunminNotes.EditorHost;

namespace HunminNotes.App;

public sealed class DesktopNoteManager(NoteWorkspace workspace, Func<Task<Note>>? createNote = null, Action? shutdown = null)
{
    private readonly OperationLifetime operations = new();
    private readonly Dictionary<Guid, MainWindow> windows = [];
    private readonly Dictionary<Guid, CollapsedNoteWindow> icons = [];
    private bool exiting;
    private SettingsWindow? settings;
    public MainWindow? ShowNote(Guid id)
    {
        if (exiting) return null;
        if (icons.Remove(id, out var icon)) icon.Close();
        if (!windows.TryGetValue(id, out var window))
        {
            window = new MainWindow(workspace, id);
            windows[id] = window;
            window.CollapseRequested += Collapse;
            window.NewNoteRequested += async () => await RunAsync(CreateAsync);
            window.SettingsRequested += ShowSettings;
            window.Closed += (_, _) => { windows.Remove(id); if (icons.Remove(id, out var old)) old.Close(); };
            window.IsVisibleChanged += (_, e) => { if (e.NewValue is true && icons.Remove(id, out var old)) old.Close(); };
        }
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        return window;
    }
    public void Collapse(MainWindow window)
    {
        if (exiting || !window.CanCollapse || icons.ContainsKey(window.NoteId)) return;
        var title = workspace.Notes.First(note => note.Id == window.NoteId).Title;
        var icon = new CollapsedNoteWindow(title, () => ShowNote(window.NoteId));
        var area = SystemParameters.WorkArea;
        icon.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - icon.Width));
        icon.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - icon.Height));
        icons[window.NoteId] = icon;
        icon.Show(); window.Hide();
    }
    public async Task CreateAsync()
    {
        using var operation = operations.TryBegin();
        if (operation is null) return;
        var note = await (createNote?.Invoke() ?? Task.Run(() => workspace.CreateAsync(CancellationToken.None)));
        if (!exiting) ShowNote(note.Id);
    }
    public void ShowAll() { foreach (var note in workspace.Notes) ShowNote(note.Id); }
    public void CollapseAll() { foreach (var window in windows.Values.ToArray()) Collapse(window); }
    public void ShowSettings()
    {
        if (exiting) return;
        if (settings is null)
        {
            settings = new SettingsWindow();
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }
    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        var completed = false;
        try
        {
            await operations.StopAsync();
            foreach (var window in windows.Values.ToArray()) if (!await window.RequestCloseAsync()) return;
            settings?.Close();
            await workspace.DisposeAsync();
            completed = true;
            (shutdown ?? (() => System.Windows.Application.Current.Shutdown()))();
        }
        finally { if (!completed) { exiting = false; operations.Resume(); } }
    }
    public static async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "메모", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
