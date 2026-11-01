using System.Diagnostics;
using System.Net.Http;
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
    private AboutWindow? about;
    private HelpWindow? help;
    private Action? afterExit;
    private AppUpdater? updater;
    private DispatcherTimer? updateTimer;
    internal Action Relauncher { get; set; } = () => { if (Environment.ProcessPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); };
    internal AppUpdate? AvailableUpdate { get; private set; }
    // 복원한 글꼴 설정입니다. 처음 열리는 메모 창이 WebView 저장소에 적용합니다.
    internal string? PendingPreferences { get; set; }
    internal string UpdateStatus { get; private set; } = "";
    public event Action<AppUpdate>? UpdateFound;
    internal static Version CurrentVersion => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
    // 게시한 단일 exe만 스스로 교체합니다. 소스 실행은 옆에 DLL이 있습니다.
    private static string? InstalledExecutable => Environment.ProcessPath is { } path && !System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "DevPostIt.dll")) ? path : null;
    internal bool CanUpdate => InstalledExecutable is not null;
    public MainWindow? ShowNote(Guid id)
    {
        if (exiting) return null;
        if (icons.Remove(id, out var icon)) icon.Close();
        if (!windows.TryGetValue(id, out var window))
        {
            window = new MainWindow(workspace, id);
            var state = settingsStore.Note(id);
            window.PendingPreferences = PendingPreferences;
            PendingPreferences = null;
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
        // 사용자가 옮기기 전까지 마지막 위치를 유지합니다. 처음 접을 때의 위치도 바로 저장합니다.
        if (state.IconLeft is { } left && state.IconTop is { } top && IsOnScreen(left, top, icon.Width, icon.Height)) { icon.Left = left; icon.Top = top; }
        else
        {
            var area = SystemParameters.WorkArea;
            icon.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - icon.Width));
            icon.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - icon.Height));
            state.IconLeft = icon.Left; state.IconTop = icon.Top;
        }
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
            settings = new SettingsWindow(this, workspace.Root, settingsStore, canChangeFolder);
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }
    public void ShowAbout()
    {
        if (exiting) return;
        if (about is null)
        {
            about = new AboutWindow();
            about.Closed += (_, _) => about = null;
            about.Show();
        }
        about.Activate();
    }
    public void ShowHelp()
    {
        if (exiting) return;
        if (help is null)
        {
            help = new HelpWindow();
            help.Closed += (_, _) => help = null;
            help.Show();
        }
        if (help.WindowState == WindowState.Minimized) help.WindowState = WindowState.Normal;
        help.Activate();
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
    public async Task<BackupSummary> BackupAsync(string destination, string? preferences = null)
    {
        await workspace.SavePreferencesAsync(preferences, CancellationToken.None);
        await workspace.FlushOpenNotesAsync(CancellationToken.None);
        return await Task.Run(() => NoteBackup.CreateAsync(workspace.Root, destination, CurrentVersion.ToString(3), CancellationToken.None));
    }
    public async Task RestoreBackupAsync(string backup)
    {
        await Task.Run(() => NoteBackup.PrepareRestoreAsync(backup, workspace.Root, CancellationToken.None));
        afterExit = Relauncher;
        await ExitAsync();
    }
    // 앱 시작 후와 하루 간격으로 새 버전을 찾습니다. 설치는 사용자가 고를 때만 합니다.
    public void StartUpdateChecks()
    {
        if (InstalledExecutable is not { } executable) return;
        updater = new AppUpdater(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, executable);
        updater.CleanUp();
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(24);
            if (settingsStore.Current.AutoUpdate) await CheckForUpdateAsync();
        };
        updateTimer.Start();
    }
    public async Task<string> CheckForUpdateAsync()
    {
        if (updater is null) return UpdateStatus = "소스에서 실행 중이면 업데이트를 사용할 수 없습니다.";
        try
        {
            var found = await updater.CheckAsync(CurrentVersion, CancellationToken.None);
            AvailableUpdate = found;
            UpdateStatus = found is null ? $"최신 버전입니다 ({CurrentVersion.ToString(3)})." : $"새 버전 {found.Version}을 설치할 수 있습니다. 트레이 메뉴에서 설치하세요.";
            if (found is not null) UpdateFound?.Invoke(found);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.IO.IOException) { UpdateStatus = $"업데이트를 확인하지 못했습니다: {error.Message}"; }
        return UpdateStatus;
    }
    public async Task InstallUpdateAsync()
    {
        if (updater is null || AvailableUpdate is not { } update) return;
        await updater.DownloadAsync(update, CancellationToken.None);
        afterExit = () => { updater.Install(); Relauncher(); };
        await ExitAsync();
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
            about?.Close();
            help?.Close();
            await workspace.DisposeAsync();
            completed = true;
            updateTimer?.Stop();
            try { afterExit?.Invoke(); }
            catch (Exception error) { System.Windows.MessageBox.Show($"새로 시작하지 못했습니다. 앱을 다시 실행해 주세요.\n{error.Message}", "메모", MessageBoxButton.OK, MessageBoxImage.Warning); }
            (shutdown ?? (() => System.Windows.Application.Current.Shutdown()))();
        }
        finally { if (!completed) { exiting = false; afterExit = null; operations.Resume(); } }
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
