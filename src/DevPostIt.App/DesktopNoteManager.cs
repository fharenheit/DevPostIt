using System.Windows;
using System.Windows.Threading;
using DevPostIt.Storage;
using DevPostIt.Core;
using DevPostIt.EditorHost;

namespace DevPostIt.App;

public sealed class DesktopNoteManager(NoteWorkspace workspace, Func<Task<Note>>? createNote = null, Action? shutdown = null, AppSettingsStore? store = null, bool canChangeFolder = false)
{
    private readonly OperationLifetime operations = new();
    private readonly Dictionary<Guid, MainWindow> windows = [];
    private readonly Dictionary<Guid, CollapsedNoteWindow> icons = [];
    private readonly AppSettingsStore settingsStore = store ?? new AppSettingsStore(System.IO.Path.Combine(workspace.Root, ".hunmin", "app-settings.json"));
    private DispatcherTimer? saveTimer;
    private bool exiting;
    private SettingsWindow? settings;
    private SearchWindow? search;
    public MainWindow? ShowNote(Guid id)
    {
        if (exiting) return null;
        if (icons.Remove(id, out var icon)) icon.Close();
        if (!windows.TryGetValue(id, out var window))
        {
            window = new MainWindow(workspace, id);
            var state = settingsStore.Note(id);
            window.ApplyAppearance(NoteColors.IsKnown(state.Color) ? state.Color : NoteColors.Default, state.Topmost);
            ApplyPlacement(window, state);
            windows[id] = window;
            window.CollapseRequested += Collapse;
            window.NewNoteRequested += async () => await RunAsync(CreateAsync);
            window.SettingsRequested += ShowSettings;
            window.SearchRequested += ShowSearch;
            window.AppearanceChanged += (color, topmost) => { state.Color = color; state.Topmost = topmost; RememberSoon(); };
            window.LocationChanged += (_, _) => RememberPlacement(window, state);
            window.SizeChanged += (_, _) => RememberPlacement(window, state);
            window.Closed += (_, _) =>
            {
                windows.Remove(id);
                if (icons.Remove(id, out var old)) old.Close();
                if (!exiting) RememberSoon();
            };
            window.IsVisibleChanged += (_, e) => { if (e.NewValue is true && icons.Remove(id, out var old)) old.Close(); };
        }
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        RememberSoon();
        return window;
    }
    public void Collapse(MainWindow window)
    {
        if (exiting || !window.CanCollapse || icons.ContainsKey(window.NoteId)) return;
        var title = workspace.Notes.First(note => note.Id == window.NoteId).Title;
        var state = settingsStore.Note(window.NoteId);
        var icon = new CollapsedNoteWindow(title, () => ShowNote(window.NoteId), window.NoteColor);
        var area = SystemParameters.WorkArea;
        var (left, top) = (state.IconLeft ?? window.Left, state.IconTop ?? window.Top);
        if (!IsOnScreen(left, top, icon.Width, icon.Height)) (left, top) = (window.Left, window.Top);
        icon.Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - icon.Width));
        icon.Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - icon.Height));
        if (state.IconLeft is null) { icon.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - icon.Width)); icon.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - icon.Height)); }
        icon.LocationChanged += (_, _) => { state.IconLeft = icon.Left; state.IconTop = icon.Top; RememberSoon(); };
        icons[window.NoteId] = icon;
        icon.Show(); window.Hide();
        RememberSoon();
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
            settings = new SettingsWindow(workspace.Root, settingsStore, canChangeFolder);
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }
    public void ShowSearch()
    {
        if (exiting) return;
        if (search is null)
        {
            search = new SearchWindow(workspace, id => ShowNote(id));
            search.Closed += (_, _) => search = null;
            search.Show();
        }
        if (search.WindowState == WindowState.Minimized) search.WindowState = WindowState.Normal;
        search.Activate();
    }
    public async Task RestoreFromTrashAsync(Guid id)
    {
        await Task.Run(() => workspace.RestoreAsync(id, CancellationToken.None));
        ShowNote(id);
    }
    // 앱을 다시 켜면 지난번에 열어 둔 메모를 같은 상태로 엽니다.
    public void RestoreSession()
    {
        if (settingsStore.Current.StartInTray) return;
        var known = workspace.Notes.Select(note => note.Id).ToHashSet();
        var open = settingsStore.Current.OpenNotes?.Where(known.Contains).ToArray() ?? [];
        if (open.Length == 0) { ShowNote(workspace.Notes[0].Id); return; }
        var collapsed = settingsStore.Current.CollapsedNotes.ToHashSet();
        foreach (var id in open)
            if (ShowNote(id) is { } window && collapsed.Contains(id)) Collapse(window);
    }
    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        var completed = false;
        try
        {
            await operations.StopAsync();
            RememberNow();
            foreach (var window in windows.Values.ToArray()) if (!await window.RequestCloseAsync()) return;
            settings?.Close();
            search?.Close();
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
    private static bool IsOnScreen(double left, double top, double width, double height) =>
        left + width > SystemParameters.VirtualScreenLeft && top + height > SystemParameters.VirtualScreenTop &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    // 모니터 구성이 바뀌어 화면 밖이 된 위치는 쓰지 않습니다.
    private static void ApplyPlacement(MainWindow window, NoteWindowState state)
    {
        if (state.Width is { } width && state.Height is { } height && width >= window.MinWidth && height >= window.MinHeight) { window.Width = width; window.Height = height; }
        if (state.Left is { } left && state.Top is { } top && IsOnScreen(left, top, window.Width, window.Height))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left; window.Top = top;
        }
    }
    private void RememberPlacement(MainWindow window, NoteWindowState state)
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return;
        state.Left = window.Left; state.Top = window.Top; state.Width = window.Width; state.Height = window.Height;
        RememberSoon();
    }
    private void RememberSoon()
    {
        saveTimer ??= CreateSaveTimer();
        saveTimer.Stop();
        saveTimer.Start();
    }
    private DispatcherTimer CreateSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => { timer.Stop(); if (!exiting) RememberNow(); };
        return timer;
    }
    private void RememberNow()
    {
        settingsStore.Current.OpenNotes = [.. windows.Keys];
        settingsStore.Current.CollapsedNotes = [.. icons.Keys];
        settingsStore.Save();
    }
}
