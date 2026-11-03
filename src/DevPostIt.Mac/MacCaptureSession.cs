using CoreGraphics;
using DevPostIt.Desktop;
using DevPostIt.EditorHost;

namespace DevPostIt.Mac;

// 모니터마다 투명 패널을 띄워 입력을 직접 받습니다. 전역 후크가 없으므로 창을 닫으면 항상 입력이 돌아갑니다.
// 좌표는 모두 주 모니터 왼쪽 위가 원점인 화면 좌표입니다.
internal sealed class MacCaptureSession
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private readonly Action<ScreenRect> selected;
    private readonly Action cancelled;
    private readonly List<CaptureOverlay> overlays = [];
    private readonly CaptureSelection selection = new(VirtualScreen());
    private NSTimer? timeout;
    private (int X, int Y)? latest;
    private bool looking;
    private bool finished;

    public MacCaptureSession(Action<ScreenRect> selected, Action cancelled)
    {
        this.selected = selected; this.cancelled = cancelled;
    }

    public static ScreenRect VirtualScreen()
    {
        var primary = NSScreen.Screens[0].Frame.Height;
        var frames = NSScreen.Screens.Select(screen => screen.Frame).ToArray();
        var left = frames.Min(frame => frame.X);
        var top = frames.Min(frame => ScreenCoordinates.FlipY(frame.Y, frame.Height, primary));
        var right = frames.Max(frame => frame.GetMaxX());
        var bottom = frames.Max(frame => ScreenCoordinates.FlipY(frame.Y, frame.Height, primary) + frame.Height);
        return new ScreenRect((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }

    private static (int X, int Y) Cursor()
    {
        var location = NSEvent.CurrentMouseLocation;
        return ((int)location.X, (int)(NSScreen.Screens[0].Frame.Height - location.Y));
    }

    public void Start()
    {
        if (finished) return;
        NSApplication.SharedApplication.Activate();
        foreach (var screen in NSScreen.Screens)
        {
            var overlay = new CaptureOverlay(screen, this);
            overlays.Add(overlay);
            overlay.OrderFrontRegardless();
        }
        overlays.First(overlay => overlay.Contains(NSEvent.CurrentMouseLocation) || overlay == overlays[^1]).MakeKeyWindow();
        timeout = NSTimer.CreateScheduledTimer(Timeout.TotalSeconds, false, _ => Cancel());
        CursorMoved();
    }

    public void Cancel()
    {
        if (finished) return;
        Close();
        cancelled();
    }

    private void Close()
    {
        finished = true;
        timeout?.Invalidate();
        foreach (var overlay in overlays) overlay.Close();
        overlays.Clear();
    }

    internal void CursorMoved()
    {
        latest = Cursor();
        LookUpLatest();
    }

    // 응답하지 않는 앱에 막힐 수 있어 주 스레드에서 찾지 않습니다. 찾는 동안 움직이면 마지막 위치만 다시 찾습니다.
    private async void LookUpLatest()
    {
        if (looking || finished) return;
        looking = true;
        while (latest is { } point && !finished)
        {
            latest = null;
            var levels = selection.LevelsUp;
            var found = await Task.Run(() => MacWindowLookup.Find(point.X, point.Y, levels));
            if (finished) break;
            selection.Hover(point.X, point.Y, found);
            Refresh();
        }
        looking = false;
    }

    internal void WheelTurned(bool down)
    {
        selection.Wheel(down);
        latest = Cursor();
        LookUpLatest();
    }

    internal void Pressed() { var at = Cursor(); selection.Press(at.X, at.Y); }

    internal void Dragged()
    {
        var at = Cursor();
        selection.Move(at.X, at.Y);
        Refresh();
    }

    internal void Released()
    {
        if (finished) return;
        var at = Cursor();
        if (selection.Release(at.X, at.Y) is not { } rect) { Cancel(); return; }
        Close();
        selected(rect);
    }

    private void Refresh() { foreach (var overlay in overlays) overlay.Show(selection.Highlight); }

    private sealed class CaptureOverlay : NSPanel
    {
        private readonly NSScreen screen;
        private readonly OverlayView view;

        public CaptureOverlay(NSScreen screen, MacCaptureSession session)
            : base(screen.Frame, NSWindowStyle.Borderless | NSWindowStyle.NonactivatingPanel, NSBackingStore.Buffered, false)
        {
            this.screen = screen;
            ReleaseWhenClosed(false);
            HidesOnDeactivate = false;
            Level = NSWindowLevel.ScreenSaver;
            CollectionBehavior = NSWindowCollectionBehavior.CanJoinAllSpaces | NSWindowCollectionBehavior.FullScreenAuxiliary | NSWindowCollectionBehavior.Stationary;
            IsOpaque = false;
            // 완전히 투명하면 클릭이 아래 앱으로 지나갑니다.
            BackgroundColor = NSColor.FromWhite(0, 0.01f);
            HasShadow = false;
            AcceptsMouseMovedEvents = true;
            view = new OverlayView(session) { Frame = new CGRect(CGPoint.Empty, screen.Frame.Size) };
            ContentView = view;
            MakeFirstResponder(view);
        }

        public override bool CanBecomeKeyWindow => true;
        public bool Contains(CGPoint location) => screen.Frame.Contains(location);

        public void Show(ScreenRect? rect)
        {
            var primary = NSScreen.Screens[0].Frame.Height;
            view.Highlight = rect is { } value
                ? new CGRect(value.X - screen.Frame.X, value.Y - ScreenCoordinates.FlipY(screen.Frame.Y, screen.Frame.Height, primary), value.Width, value.Height)
                : null;
            view.NeedsDisplay = true;
        }
    }

    private sealed class OverlayView(MacCaptureSession session) : NSView
    {
        private const ushort EscapeKey = 53;
        private static readonly NSColor Border = NSColor.FromRgb(229, 56, 59);
        private static readonly NSColor Fill = NSColor.FromRgba(229, 56, 59, 40);
        public CGRect? Highlight { get; set; }

        public override bool IsFlipped => true;
        public override bool AcceptsFirstResponder() => true;
        public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;
        public override void ResetCursorRects() => AddCursorRect(Bounds, NSCursor.CrosshairCursor);
        public override void MouseMoved(NSEvent theEvent) => session.CursorMoved();
        public override void MouseDown(NSEvent theEvent) => session.Pressed();
        public override void MouseDragged(NSEvent theEvent) => session.Dragged();
        public override void MouseUp(NSEvent theEvent) => session.Released();
        public override void RightMouseDown(NSEvent theEvent) => session.Cancel();
        public override void ScrollWheel(NSEvent theEvent) { if (theEvent.ScrollingDeltaY != 0) session.WheelTurned(theEvent.ScrollingDeltaY < 0); }
        public override void KeyDown(NSEvent theEvent) { if (theEvent.KeyCode == EscapeKey) session.Cancel(); }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (Highlight is not { } rect) return;
            Fill.SetFill();
            NSBezierPath.FillRect(rect);
            Border.SetStroke();
            var path = NSBezierPath.FromRect(rect.Inset(1, 1));
            path.LineWidth = 2;
            path.Stroke();
        }
    }
}
