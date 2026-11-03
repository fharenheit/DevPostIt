using CoreGraphics;
using DevPostIt.Desktop;
using DevPostIt.EditorHost;
using DevPostIt.Storage;

namespace DevPostIt.Mac;

// 메모 창 목록과 접기, 세션 복원, 종료를 맡습니다.
internal sealed class MacNoteManager(NoteWorkspace workspace, AppSettingsStore store)
{
    private readonly OperationLifetime operations = new();
    private readonly Dictionary<Guid, MacNoteWindow> windows = [];
    private readonly Dictionary<Guid, CollapsedNotePanel> icons = [];
    private NSTimer? saveTimer;
    private bool exiting;

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
            window.Session.SettingsRequested += () => MacDialogs.ShowUnsupported("설정");
            window.Session.SearchRequested += () => MacDialogs.ShowUnsupported("메모 검색");
            window.Session.CaptureRequested += () => MacDialogs.ShowUnsupported("화면 캡처");
            window.Session.AppearanceChanged += (color, topmost) => { state.Color = color; state.Topmost = topmost; RememberSoon(); };
            window.FrameChanged += () => RememberPlacement(created, state);
            window.WindowClosed += () =>
            {
                windows.Remove(id);
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
            RememberNow();
            foreach (var window in windows.Values.ToArray()) if (!await window.Session.RequestCloseAsync()) return false;
            await workspace.DisposeAsync();
            Exited = true;
            return true;
        }
        finally { if (!Exited) { exiting = false; operations.Resume(); } }
    }

    public static async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { MacDialogs.Ask("메모", error.Message, "확인"); }
    }

    // 창이 있으면 Dock과 메뉴 막대를 표시하고 없으면 메뉴 막대 아이콘만 둡니다.
    private void UpdateActivationPolicy()
    {
        var visible = windows.Values.Any(window => window.IsVisible);
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
