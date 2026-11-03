using CoreGraphics;
using DevPostIt.Desktop;

namespace DevPostIt.Mac;

// 그림 없는 24×24 아이콘입니다. 클릭하면 복원하고 드래그하면 옮깁니다.
internal sealed class CollapsedNotePanel : NSPanel
{
    public const double Size = 24;
    public event Action? Moved;

    public CollapsedNotePanel(string title, Action restore, string color)
        : base(new CGRect(0, 0, Size, Size), NSWindowStyle.Borderless | NSWindowStyle.NonactivatingPanel, NSBackingStore.Buffered, false)
    {
        ReleaseWhenClosed(false);
        HidesOnDeactivate = false;
        // 항상 위로 둔 메모(Floating)에도 가려지지 않게 한 단계 위에 둡니다.
        Level = (NSWindowLevel)((nint)NSWindowLevel.Floating + 1);
        IsOpaque = false;
        BackgroundColor = NSColor.Clear;
        HasShadow = false;
        ContentView = new IconView(title, color, restore) { Frame = new CGRect(0, 0, Size, Size) };
        Delegate = new PanelDelegate(this);
    }

    public override bool CanBecomeKeyWindow => false;

    private sealed class PanelDelegate(CollapsedNotePanel owner) : NSWindowDelegate
    {
        public override void DidMove(NSNotification notification) => owner.Moved?.Invoke();
    }

    private sealed class IconView : NSView
    {
        private const double DragThreshold = 5;
        private readonly Action restore;
        private readonly NotePalette.Rgb fill, border;

        public IconView(string title, string color, Action restore)
        {
            this.restore = restore;
            (fill, border) = NotePalette.Icon(color);
            ToolTip = $"{title}\n클릭: 열기 · 드래그: 이동";
            AccessibilityLabel = $"{title} 열기";
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var path = NSBezierPath.FromRoundedRect(Bounds.Inset(0.5f, 0.5f), 2, 2);
            NSColor.FromRgb(fill.R, fill.G, fill.B).SetFill();
            path.Fill();
            NSColor.FromRgb(border.R, border.G, border.B).SetStroke();
            path.Stroke();
        }

        public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

        public override void MouseDown(NSEvent theEvent)
        {
            var start = theEvent.LocationInWindow;
            var window = Window!;
            while (window.NextEventMatchingMask(NSEventMask.LeftMouseUp | NSEventMask.LeftMouseDragged) is { } next)
            {
                if (next.Type == NSEventType.LeftMouseUp) { restore(); return; }
                var moved = Math.Sqrt(Math.Pow(next.LocationInWindow.X - start.X, 2) + Math.Pow(next.LocationInWindow.Y - start.Y, 2));
                if (moved >= DragThreshold) { window.PerformWindowDrag(theEvent); return; }
            }
        }
    }
}
