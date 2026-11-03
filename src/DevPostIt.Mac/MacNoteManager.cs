using CoreGraphics;
using DevPostIt.Desktop;
using DevPostIt.EditorHost;
using DevPostIt.Storage;
using System.Net.Http;

namespace DevPostIt.Mac;

// 메모 창 목록과 접기, 세션 복원, 종료를 맡습니다.
internal sealed class MacNoteManager(NoteWorkspace workspace, AppSettingsStore store, bool canChangeFolder = false)
{
    private readonly OperationLifetime operations = new();
    private readonly Dictionary<Guid, MacNoteWindow> windows = [];
    private readonly Dictionary<Guid, CollapsedNotePanel> icons = [];
    private NSTimer? saveTimer;
    private bool exiting;
    private MacSettingsWindow? settings;
    private MacSearchWindow? search;
    private MacFormatterWindow? formatter;
    private MacMindMapWindow? mindMap;
    private MacPlannerWindow? todo;
    private MacPlannerWindow? calendar;
    private MacHelpWindow? help;
    private MacAboutWindow? about;
    private NSTimer? reminderTimer;
    private NSTimer? holidayTimer;
    private MindMapStore? mindMaps;
    private PlannerStore? planner;
    private HolidayStore? holidays;
    private MacNoteWindow? lastActive;
    private Action? afterExit;
    private MacCaptureSession? capture;
    private readonly List<MacCaptureAnnotationWindow> annotators = [];
    private string ToolsRoot => Path.Combine(workspace.Root, ".hunmin", "tools");
    private MindMapStore MindMaps => mindMaps ??= new MindMapStore(Path.Combine(ToolsRoot, "mindmaps"));
    internal PlannerStore Planner => planner ??= new PlannerStore(ToolsRoot);
    internal HolidayStore Holidays => holidays ??= new HolidayStore(ToolsRoot);

    public event Action<PlannerReminder>? ReminderDue;
    public static string CurrentVersion => NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "0.0.0";
    internal string? PendingPreferences { get; set; }
    // 쓰는 중인 프로세스를 먼저 끝내야 하므로 끝나기를 기다렸다가 같은 실행 파일을 다시 시작합니다. 환경 변수도 이어받습니다.
    internal Action Relauncher { get; set; } = () =>
    {
        var start = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { "-c", "while kill -0 \"$1\" 2>/dev/null; do sleep 0.1; done; exec \"$0\"", Environment.ProcessPath!, Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
        System.Diagnostics.Process.Start(start)?.Dispose();
    };
    internal Action<string> ImageDeleter { get; set; } = path =>
    {
        if (!NSFileManager.DefaultManager.TrashItem(NSUrl.CreateFileUrl(path, false, null), out _, out var error)) throw new IOException(error?.LocalizedDescription ?? path);
    };
    internal Func<string[]> ImportPathPicker { get; set; } = () => MacDialogs.PickOpenPaths("가져올 Markdown 파일 선택", true);
    internal Action<string> ImportReporter { get; set; } = message => MacDialogs.Ask("Markdown 가져오기", message, "확인");
    internal MacSettingsWindow? Settings => settings;
    internal MacSearchWindow? Search => search;
    internal MacFormatterWindow? Formatter => formatter;
    internal MacMindMapWindow? MindMap => mindMap;
    internal MacPlannerWindow? Todo => todo;
    internal MacPlannerWindow? Calendar => calendar;
    internal MacHelpWindow? Help => help;
    internal MacAboutWindow? About => about;
    internal IReadOnlyList<MacCaptureAnnotationWindow> Annotators => annotators;

    public bool Exited { get; private set; }
    internal IReadOnlyDictionary<Guid, MacNoteWindow> Windows => windows;
    internal IReadOnlyDictionary<Guid, CollapsedNotePanel> Icons => icons;
    private static double PrimaryHeight => NSScreen.Screens[0].Frame.Height;

    public MacNoteWindow? ShowNote(Guid id)
    {
        if (exiting) return null;
        if (icons.Remove(id, out var icon)) icon.Close();
        if (!windows.TryGetValue(id, out var window))
        {
            window = new MacNoteWindow(workspace, id);
            var state = store.Note(id);
            window.ApplyAppearance(NoteColors.IsKnown(state.Color) ? state.Color : NoteColors.Default, state.Topmost);
            ApplyPlacement(window, state);
            windows[id] = window;
            var created = window;
            window.CollapseRequested += () => Collapse(created);
            window.Session.NewNoteRequested += () => _ = RunAsync(CreateAsync);
            window.Session.PendingPreferences = PendingPreferences;
            PendingPreferences = null;
            window.BecameKey += current => lastActive = current;
            window.Session.SettingsRequested += ShowSettings;
            window.Session.SearchRequested += ShowSearch;
            window.Session.CaptureRequested += () => _ = RunAsync(StartCaptureAsync);
            window.Session.AppearanceChanged += (color, topmost) => { state.Color = color; state.Topmost = topmost; RememberSoon(); };
            window.FrameChanged += () => RememberPlacement(created, state);
            window.WindowClosed += () =>
            {
                windows.Remove(id);
                if (lastActive == created) lastActive = null;
                if (icons.Remove(id, out var old)) old.Close();
                UpdateActivationPolicy();
                if (!exiting) RememberSoon();
            };
        }
        window.BringToFront();
        _ = window.Session.EnsureInitializedAsync();
        UpdateActivationPolicy();
        RememberSoon();
        return window;
    }

    public void Collapse(MacNoteWindow window)
    {
        if (exiting || !window.Session.CanCollapse || icons.ContainsKey(window.NoteId)) return;
        var id = window.NoteId;
        var title = workspace.Notes.First(note => note.Id == id).Title;
        var state = store.Note(id);
        var icon = new CollapsedNotePanel(title, () => ShowNote(id), window.NoteColor);
        var frame = window.Frame;
        // 사용자가 옮기기 전까지 마지막 위치를 유지합니다. 처음 접을 때의 위치도 바로 저장합니다.
        if (state.IconLeft is { } left && state.IconTop is { } top && IsOnScreen(left, top, CollapsedNotePanel.Size, CollapsedNotePanel.Size)) MoveTo(icon, left, top);
        else
        {
            state.IconLeft = frame.X;
            state.IconTop = ScreenCoordinates.FlipY(frame.Y, frame.Height, PrimaryHeight);
            MoveTo(icon, state.IconLeft.Value, state.IconTop.Value);
        }
        icon.Moved += () =>
        {
            state.IconLeft = icon.Frame.X;
            state.IconTop = ScreenCoordinates.FlipY(icon.Frame.Y, icon.Frame.Height, PrimaryHeight);
            RememberSoon();
        };
        icons[id] = icon;
        icon.OrderFront(null);
        window.OrderOut(null);
        UpdateActivationPolicy();
        RememberSoon();
    }

    public async Task CreateAsync()
    {
        using var operation = operations.TryBegin();
        if (operation is null) return;
        var note = await Task.Run(() => workspace.CreateAsync(CancellationToken.None));
        if (!exiting) ShowNote(note.Id);
    }

    public void ShowAll() { foreach (var note in workspace.Notes) ShowNote(note.Id); }
    public void CollapseAll() { foreach (var window in windows.Values.ToArray()) Collapse(window); }

    private void ShowWeb<T>(ref T? field, Func<T> create, Action clear) where T : MacWebWindow
    {
        if (exiting) return;
        if (field is null)
        {
            field = create();
            field.WindowClosed += () => { clear(); UpdateActivationPolicy(); };
            _ = field.OpenAsync();
        }
        field.BringToFront();
        UpdateActivationPolicy();
    }

    private void ShowText<T>(ref T? field, Func<T> create, Action clear) where T : MacTextWindow
    {
        if (exiting) return;
        if (field is null)
        {
            field = create();
            field.WindowClosed += () => { clear(); UpdateActivationPolicy(); };
        }
        field.BringToFront();
        UpdateActivationPolicy();
    }

    public void ShowSettings() => ShowWeb(ref settings, () => new MacSettingsWindow(this, workspace.Root, store, canChangeFolder), () => settings = null);
    public void ShowSearch() => ShowWeb(ref search, () => new MacSearchWindow(workspace, id => ShowNote(id)), () => search = null);
    public void ShowFormatter() => ShowWeb(ref formatter, () => new MacFormatterWindow(), () => formatter = null);
    public void ShowMindMap() => ShowWeb(ref mindMap, () => new MacMindMapWindow(MindMaps), () => mindMap = null);
    public void ShowTodo() => ShowWeb(ref todo, () => new MacPlannerWindow("할 일", EditorOrigin.TodoPage, EditorOrigin.IsTodoPage, Planner, BroadcastPlanner, 980, 680), () => todo = null);
    public void ShowCalendar() => ShowWeb(ref calendar, () => new MacPlannerWindow("달력", EditorOrigin.CalendarPage, EditorOrigin.IsCalendarPage, Planner, BroadcastPlanner, 1100, 760, () => Holidays.Load()), () => calendar = null);
    public void ShowHelp() => ShowText(ref help, () => new MacHelpWindow(), () => help = null);
    public void ShowAbout() => ShowText(ref about, () => new MacAboutWindow(), () => about = null);

    private void BroadcastPlanner(PlannerState state)
    {
        todo?.SendState(state);
        calendar?.SendState(state);
    }

    // 시작 후와 하루 간격으로 저장소의 공휴일 파일을 받습니다. 실패해도 알리지 않고 가진 정보를 계속 씁니다.
    public void StartHolidayUpdates()
    {
        holidayTimer = NSTimer.CreateScheduledTimer(15, false, _ =>
        {
            holidayTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromHours(24), _ => OnHolidayTick());
            OnHolidayTick();
        });
    }

    private void OnHolidayTick() { if (store.Current.HolidayUpdates) _ = UpdateHolidaysAsync(); }

    private async Task UpdateHolidaysAsync()
    {
        if (exiting) return;
        try { if (await Task.Run(() => Holidays.UpdateAsync(CancellationToken.None))) calendar?.SendHolidays(); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { /* 가진 정보를 계속 씁니다. */ }
    }

    // 앱이 켜져 있는 동안 할 일과 일정의 알림을 확인합니다.
    public void StartReminders()
    {
        reminderTimer = NSTimer.CreateRepeatingScheduledTimer(30, timer => _ = CheckRemindersAsync(DateTime.Now));
        _ = CheckRemindersAsync(DateTime.Now);
    }

    internal async Task CheckRemindersAsync(DateTime now)
    {
        if (exiting) return;
        try { foreach (var reminder in await Task.Run(() => Planner.TakeRemindersAsync(now, CancellationToken.None))) ReminderDue?.Invoke(reminder); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { /* 다음 확인 때 다시 시도합니다. */ }
    }

    public async Task<BackupSummary> BackupAsync(string destination, string? preferences = null)
    {
        await workspace.SavePreferencesAsync(preferences, CancellationToken.None);
        await workspace.FlushOpenNotesAsync(CancellationToken.None);
        return await Task.Run(() => NoteBackup.CreateAsync(workspace.Root, destination, CurrentVersion, CancellationToken.None));
    }

    public async Task RestoreBackupAsync(string backup)
    {
        await Task.Run(() => NoteBackup.PrepareRestoreAsync(backup, workspace.Root, CancellationToken.None));
        afterExit = Relauncher;
        NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);
    }

    internal Task<IReadOnlyList<UnusedImage>> FindUnusedImagesAsync() => Task.Run(() => workspace.FindUnusedImagesAsync(CancellationToken.None));
    internal Task<int> DeleteUnusedImagesAsync(IReadOnlyList<UnusedImage> images) => Task.Run(() => workspace.DeleteUnusedImagesAsync(images, ImageDeleter, CancellationToken.None));

    // 파일을 복사해 새 메모로 만듭니다. 한 파일이 실패해도 나머지는 가져옵니다.
    public async Task ImportMarkdownAsync()
    {
        var paths = ImportPathPicker();
        if (paths.Length == 0) return;
        using var operation = operations.TryBegin();
        if (operation is null) return;
        var imported = new List<DevPostIt.Core.Note>();
        var failed = new List<string>();
        foreach (var path in paths)
        {
            try { imported.Add(await Task.Run(() => workspace.ImportMarkdownAsync(path, CancellationToken.None))); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { failed.Add($"{Path.GetFileName(path)}: {error.Message}"); }
        }
        if (!exiting && imported.Count > 0) ShowNote(imported[0].Id);
        if (imported.Count > 1 || failed.Count > 0)
            ImportReporter($"메모 {imported.Count}개를 가져왔습니다." + (imported.Count > 1 ? " 나머지는 메뉴 막대의 ‘메모 목록’에서 엽니다." : "") + (failed.Count > 0 ? $"\n\n가져오지 못한 파일:\n{string.Join("\n", failed)}" : ""));
    }

    // 메모 창을 숨기고 요소나 영역을 고르면 최근 메모에 넣습니다.
    public async Task StartCaptureAsync()
    {
        if (exiting || capture is not null) return;
        if (!MacScreenCapture.EnsurePermission() || !MacWindowLookup.OfferElementAccess()) return;
        var target = lastActive ?? windows.Values.FirstOrDefault(window => window.IsVisible) ?? windows.Values.FirstOrDefault() ?? ShowNote(workspace.Notes[0].Id);
        if (target is null) return;
        var hidden = windows.Values.Where(window => window.IsVisible).ToArray();
        void Restore()
        {
            capture = null;
            if (exiting) return;
            foreach (var window in hidden) if (windows.ContainsValue(window) && !icons.ContainsKey(window.NoteId)) window.OrderFront(null);
            UpdateActivationPolicy();
        }
        capture = new MacCaptureSession(
            rect => _ = RunAsync(async () =>
            {
                byte[] png;
                double density;
                // 패널이 화면에서 사라진 뒤에 찍습니다.
                await Task.Delay(60);
                try { (png, density) = await MacScreenCapture.CaptureAsync(rect); }
                catch (NSErrorException error) { Restore(); MacDialogs.Ask("화면을 캡처하지 못했습니다.", error.Error.LocalizedDescription, "확인"); return; }
                Restore();
                await FinishCaptureAsync(target, png, density);
            }),
            Restore);
        foreach (var window in hidden) window.OrderOut(null);
        // 대기 시간에는 메뉴 등을 열 수 있습니다.
        var delay = CaptureGeometry.DelaySeconds.Contains(store.Current.CaptureDelaySeconds) ? store.Current.CaptureDelaySeconds : 0;
        await Task.Delay(TimeSpan.FromMilliseconds(200 + delay * 1000));
        capture?.Start();
    }

    // 주석 창을 닫으면 그림을 버립니다. 주석을 끄면 바로 메모에 넣습니다.
    internal Task FinishCaptureAsync(MacNoteWindow target, byte[] png, double density)
    {
        if (exiting) return Task.CompletedTask;
        if (!store.Current.AnnotateCapture) return InsertCaptureAsync(target, png);
        var window = new MacCaptureAnnotationWindow(png, density, result => _ = RunAsync(() => InsertCaptureAsync(target, result)));
        annotators.Add(window);
        window.WindowClosed += () => annotators.Remove(window);
        window.BringToFront();
        return Task.CompletedTask;
    }

    private async Task InsertCaptureAsync(MacNoteWindow target, byte[] png)
    {
        if (ShowNote(target.NoteId) is not { } window) return;
        for (var attempt = 0; attempt < 100 && !window.Session.IsEditorReady; attempt++) await Task.Delay(100);
        await window.Session.InsertImageAsync(png);
        window.BringToFront();
    }

    public async Task RestoreFromTrashAsync(Guid id)
    {
        await Task.Run(() => workspace.RestoreAsync(id, CancellationToken.None));
        ShowNote(id);
    }

    public Task PurgeAsync(Guid id) => Task.Run(() => workspace.PurgeAsync(id, CancellationToken.None));

    public void RestoreSession()
    {
        foreach (var item in SessionRestore.Plan(workspace.Notes.Select(note => note.Id).ToArray(), store.Current))
            if (ShowNote(item.Id) is { } window && item.Collapsed) Collapse(window);
    }

    // 트레이 종료와 같이 메모를 보존합니다. 저장하지 못하면 거짓을 돌려주고 앱을 유지합니다.
    public async Task<bool> ExitAsync()
    {
        if (Exited) return true;
        if (exiting) return false;
        exiting = true;
        try
        {
            await operations.StopAsync();
            saveTimer?.Invalidate();
            capture?.Cancel();
            foreach (var window in annotators.ToArray()) window.Close();
            RememberNow();
            foreach (var window in windows.Values.ToArray()) if (!await window.Session.RequestCloseAsync()) return false;
            settings?.Close();
            search?.Close();
            formatter?.Close();
            help?.Close();
            about?.Close();
            reminderTimer?.Invalidate();
            holidayTimer?.Invalidate();
            foreach (var window in new MacWebWindow?[] { mindMap, todo, calendar }) if (window is not null) await window.FlushAndCloseAsync();
            await workspace.DisposeAsync();
            Exited = true;
            try { afterExit?.Invoke(); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { MacDialogs.Ask("새로 시작하지 못했습니다. 앱을 다시 실행해 주세요.", error.Message, "확인"); }
            return true;
        }
        finally { if (!Exited) { exiting = false; afterExit = null; operations.Resume(); } }
    }

    public static async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { MacDialogs.Ask("메모", error.Message, "확인"); }
    }

    // 창이 있으면 Dock과 메뉴 막대를 표시하고 없으면 메뉴 막대 아이콘만 둡니다.
    private void UpdateActivationPolicy()
    {
        var tools = new NSWindow?[] { settings, search, formatter, mindMap, todo, calendar, help, about };
        var visible = windows.Values.Any(window => window.IsVisible) || tools.Any(window => window?.IsVisible == true);
        NSApplication.SharedApplication.ActivationPolicy = visible ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Accessory;
    }

    private static void MoveTo(NSWindow window, double left, double top) =>
        window.SetFrameOrigin(new CGPoint(left, ScreenCoordinates.FlipY(top, window.Frame.Height, PrimaryHeight)));

    // 모니터 구성이 바뀌어 화면 밖이 된 위치는 쓰지 않습니다.
    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var frame = new CGRect(left, ScreenCoordinates.FlipY(top, height, PrimaryHeight), width, height);
        return NSScreen.Screens.Any(screen => screen.Frame.IntersectsWith(frame));
    }

    private static void ApplyPlacement(MacNoteWindow window, NoteWindowState state)
    {
        var width = state.Width is { } savedWidth && savedWidth >= MacNoteWindow.MinimumWidth ? savedWidth : MacNoteWindow.DefaultWidth;
        var height = state.Height is { } savedHeight && savedHeight >= MacNoteWindow.MinimumHeight ? savedHeight : MacNoteWindow.DefaultHeight;
        window.SetContentSize(new CGSize(width, height));
        if (state.Left is { } left && state.Top is { } top && IsOnScreen(left, top, window.Frame.Width, window.Frame.Height)) MoveTo(window, left, top);
        else window.Center();
    }

    private void RememberPlacement(MacNoteWindow window, NoteWindowState state)
    {
        if (!window.IsVisible) return;
        var frame = window.Frame;
        var content = window.ContentRectFor(frame);
        state.Left = frame.X;
        state.Top = ScreenCoordinates.FlipY(frame.Y, frame.Height, PrimaryHeight);
        state.Width = content.Width;
        state.Height = content.Height;
        RememberSoon();
    }

    private void RememberSoon()
    {
        saveTimer?.Invalidate();
        saveTimer = NSTimer.CreateScheduledTimer(0.5, false, _ => { if (!exiting) RememberNow(); });
    }

    private void RememberNow()
    {
        store.Current.OpenNotes = [.. windows.Keys];
        store.Current.CollapsedNotes = [.. icons.Keys];
        store.Save();
    }
}
