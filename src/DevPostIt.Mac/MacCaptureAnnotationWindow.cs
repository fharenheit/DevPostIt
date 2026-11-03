using CoreGraphics;
using DevPostIt.EditorHost;

namespace DevPostIt.Mac;

// 픽셀과 PNG 사이의 변환과 주석 그리기입니다. 주석 좌표는 모두 그림의 픽셀입니다.
internal static class CaptureImage
{
    private const double StrokeWidth = 3, ArrowSize = 16, TextSize = 20;

    public static byte[] ToPng(CGImage image)
    {
        using var representation = new NSBitmapImageRep(image);
        return representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary())!.ToArray();
    }

    public static (int Width, int Height, byte[] Pixels) Read(byte[] png)
    {
        using var data = NSData.FromArray(png);
        using var source = NSBitmapImageRep.ImageRepFromData(data) as NSBitmapImageRep ?? throw new InvalidDataException("그림을 읽지 못했습니다.");
        var (width, height) = ((int)source.PixelsWide, (int)source.PixelsHigh);
        var pixels = new byte[width * height * 4];
        using var space = CGColorSpace.CreateSrgb();
        using var context = new CGBitmapContext(pixels, width, height, 8, width * 4, space, CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);
        context.DrawImage(new CGRect(0, 0, width, height), source.CGImage);
        return (width, height, pixels);
    }

    // 모자이크는 픽셀 자체를 바꿉니다. 위에 덮어 그리면 원본이 그림 파일에 남기 때문입니다.
    public static CGImage Compose(byte[] source, int width, int height, IEnumerable<Annotation> items)
    {
        var data = source.ToArray();
        foreach (var item in items)
            if (item.Tool == AnnotationTool.Mosaic && CaptureAnnotations.Area(item.X1, item.Y1, item.X2, item.Y2, width, height) is { } area)
                CaptureAnnotations.Pixelate(data, width, height, area, CaptureAnnotations.MosaicBlock);
        using var space = CGColorSpace.CreateSrgb();
        using var context = new CGBitmapContext(data, width, height, 8, width * 4, space, CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);
        return context.ToImage()!;
    }

    public static NSColor Color(uint value) => NSColor.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);

    // 현재 그래픽 문맥에 그립니다. 문맥은 왼쪽 위가 원점이고 그림의 픽셀 단위입니다.
    public static void Paint(Annotation item, double density)
    {
        var color = Color(item.Color);
        var stroke = (nfloat)(StrokeWidth * density);
        switch (item.Tool)
        {
            case AnnotationTool.Rectangle:
                color.SetStroke();
                using (var path = NSBezierPath.FromRect(new CGRect(Math.Min(item.X1, item.X2), Math.Min(item.Y1, item.Y2), Math.Abs(item.X2 - item.X1), Math.Abs(item.Y2 - item.Y1))))
                {
                    path.LineWidth = stroke;
                    path.Stroke();
                }
                break;
            case AnnotationTool.Arrow:
                color.SetStroke();
                color.SetFill();
                using (var line = new NSBezierPath { LineWidth = stroke, LineCapStyle = NSLineCapStyle.Round })
                {
                    line.MoveTo(new CGPoint(item.X1, item.Y1));
                    line.LineTo(new CGPoint(item.X2, item.Y2));
                    line.Stroke();
                }
                var head = CaptureAnnotations.ArrowHead(item.X1, item.Y1, item.X2, item.Y2, ArrowSize * density);
                using (var tip = new NSBezierPath())
                {
                    tip.MoveTo(new CGPoint(head[0].X, head[0].Y));
                    tip.LineTo(new CGPoint(head[1].X, head[1].Y));
                    tip.LineTo(new CGPoint(head[2].X, head[2].Y));
                    tip.ClosePath();
                    tip.Fill();
                }
                break;
            case AnnotationTool.Text:
                using (var text = new NSAttributedString(item.Text, new NSStringAttributes { Font = NSFont.SystemFontOfSize((nfloat)(TextSize * density), NSFontWeight.Semibold), ForegroundColor = color }))
                    text.DrawAtPoint(new CGPoint(item.X1, item.Y1));
                break;
        }
    }

    public static byte[] Render(byte[] png, IReadOnlyList<Annotation> items, double density)
    {
        var (width, height, pixels) = Read(png);
        using var composed = Compose(pixels, width, height, items);
        using var space = CGColorSpace.CreateSrgb();
        using var context = new CGBitmapContext(null, width, height, 8, width * 4, space, CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);
        context.DrawImage(new CGRect(0, 0, width, height), composed);
        context.TranslateCTM(0, height);
        context.ScaleCTM(1, -1);
        NSGraphicsContext.GlobalSaveGraphicsState();
        NSGraphicsContext.CurrentContext = NSGraphicsContext.FromCGContext(context, true);
        foreach (var item in items) Paint(item, density);
        NSGraphicsContext.GlobalRestoreGraphicsState();
        using var result = context.ToImage();
        return ToPng(result!);
    }
}

// 캡처한 그림에 주석을 달고 메모에 넣기 전에 확인하는 창입니다. 닫으면 그림을 버립니다.
internal sealed class MacCaptureAnnotationWindow : NSWindow
{
    private const double ToolbarHeight = 44, Margin = 12, MinDrag = 3;
    private const int MaxTextLength = 200;
    private static readonly (string Name, AnnotationTool Tool)[] Tools = [("화살표", AnnotationTool.Arrow), ("사각형", AnnotationTool.Rectangle), ("글자", AnnotationTool.Text), ("모자이크", AnnotationTool.Mosaic)];
    private static readonly (string Name, uint Color)[] Colors = [("빨강", CaptureAnnotations.Red), ("파랑", CaptureAnnotations.Blue), ("검정", CaptureAnnotations.Black), ("노랑", CaptureAnnotations.Yellow)];
    private readonly byte[] original;
    private readonly Action<byte[]> finished;
    private readonly double density;
    private readonly int imageWidth, imageHeight;
    private readonly byte[] pixels;
    private readonly List<Annotation> annotations = [];
    private readonly Canvas canvas;
    private AnnotationTool tool = AnnotationTool.Arrow;
    private uint color = CaptureAnnotations.Red;
    private NSTextField? editing;
    private CGPoint editingAt;
    private bool editingCancelled;

    public event Action? WindowClosed;
    internal int AnnotationCount => annotations.Count;

    public MacCaptureAnnotationWindow(byte[] png, double density, Action<byte[]> finished)
        : base(new CGRect(0, 0, 640, 360), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
    {
        original = png; this.finished = finished; this.density = density;
        (imageWidth, imageHeight, pixels) = CaptureImage.Read(png);
        ReleaseWhenClosed(false);
        Title = "캡처 주석";
        Level = NSWindowLevel.Floating;
        BackgroundColor = NSColor.FromRgb(255, 253, 245);
        MinSize = new CGSize(760, 300);
        var area = NSScreen.MainScreen?.VisibleFrame.Size ?? new CGSize(1200, 800);
        SetContentSize(new CGSize(
            Math.Clamp(imageWidth / density + 2 * Margin, 760, Math.Max(760, area.Width * .9)),
            Math.Clamp(imageHeight / density + ToolbarHeight + 2 * Margin, 360, Math.Max(360, area.Height * .9))));
        canvas = new Canvas(this);
        var content = ContentView!;
        canvas.Frame = new CGRect(Margin, Margin, content.Bounds.Width - 2 * Margin, content.Bounds.Height - ToolbarHeight - 2 * Margin);
        canvas.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        content.AddSubview(canvas);
        content.AddSubview(CreateToolbar(content.Bounds));
        Center();
        WillClose += (_, _) => WindowClosed?.Invoke();
        MakeFirstResponder(canvas);
    }

    public void BringToFront()
    {
        NSApplication.SharedApplication.Activate();
        MakeKeyAndOrderFront(this);
    }

    private NSView CreateToolbar(CGRect bounds)
    {
        var tools = NSSegmentedControl.FromLabels(Tools.Select(item => item.Name).ToArray(), NSSegmentSwitchTracking.SelectOne, () => { });
        tools.SelectedSegment = 0;
        tools.Activated += (_, _) => { CommitText(); tool = Tools[(int)tools.SelectedSegment].Tool; };
        var colors = NSSegmentedControl.FromLabels(Colors.Select(item => item.Name).ToArray(), NSSegmentSwitchTracking.SelectOne, () => { });
        colors.SelectedSegment = 0;
        colors.Activated += (_, _) => color = Colors[(int)colors.SelectedSegment].Color;
        var bar = NSStackView.FromViews([
            tools, colors,
            NSButton.CreateButton("실행 취소", Undo),
            NSButton.CreateButton("그대로 넣기", InsertOriginal),
            NSButton.CreateButton("취소", Close),
            NSButton.CreateButton("메모에 넣기", Finish),
        ]);
        bar.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        bar.Spacing = 8;
        bar.Frame = new CGRect(Margin, bounds.Height - ToolbarHeight, bounds.Width - 2 * Margin, ToolbarHeight);
        bar.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        return bar;
    }

    internal void AddAnnotation(Annotation annotation)
    {
        annotations.Add(annotation);
        canvas.Refresh();
    }

    internal void Undo()
    {
        if (annotations.Count == 0) return;
        annotations.RemoveAt(annotations.Count - 1);
        canvas.Refresh();
    }

    internal void Finish()
    {
        CommitText();
        finished(annotations.Count == 0 ? original : CaptureImage.Render(original, annotations, density));
        Close();
    }

    internal void InsertOriginal()
    {
        finished(original);
        Close();
    }

    private void BeginText(CGPoint viewPoint, CGPoint imagePoint)
    {
        editingAt = imagePoint;
        editingCancelled = false;
        var field = new NSTextField(new CGRect(viewPoint.X, viewPoint.Y, 240, 28))
        {
            Font = NSFont.SystemFontOfSize(18)!,
            TextColor = CaptureImage.Color(color),
            PlaceholderString = "글자 입력",
            };
        field.Delegate = new TextDelegate(this);
        canvas.AddSubview(field);
        editing = field;
        MakeFirstResponder(field);
    }

    private void CommitText()
    {
        if (editing is not { } field) return;
        editing = null;
        var text = field.StringValue.Trim();
        var keep = !editingCancelled && text.Length > 0;
        field.RemoveFromSuperview();
        if (keep) AddAnnotation(new Annotation(AnnotationTool.Text, editingAt.X, editingAt.Y, 0, 0, color, text.Length > MaxTextLength ? text[..MaxTextLength] : text));
        MakeFirstResponder(canvas);
    }

    // 텍스트 칸의 입력기로 글자를 받으므로 한글 조합 중의 Enter는 조합을 끝내는 데 쓰입니다.
    private sealed class TextDelegate(MacCaptureAnnotationWindow owner) : NSTextFieldDelegate
    {
        public override void EditingEnded(NSNotification notification) => owner.CommitText();
        public override bool DoCommandBySelector(NSControl control, NSTextView textView, ObjCRuntime.Selector commandSelector)
        {
            if (commandSelector.Name != "cancelOperation:") return false;
            owner.editingCancelled = true;
            owner.CommitText();
            return true;
        }
    }

    private sealed class Canvas(MacCaptureAnnotationWindow owner) : NSView
    {
        private const ushort ReturnKey = 36, EnterKey = 76, EscapeKey = 53;
        private NSImage? composed;
        private CGPoint? start;
        private Annotation? draft;

        public override bool IsFlipped => true;
        public override bool AcceptsFirstResponder() => true;
        public override void ResetCursorRects() => AddCursorRect(Bounds, NSCursor.CrosshairCursor);

        // 그림을 줄이기만 합니다. 키우지 않습니다.
        private nfloat Scale => (nfloat)Math.Min(1 / owner.density, Math.Min(Bounds.Width / owner.imageWidth, Bounds.Height / owner.imageHeight));

        private CGPoint ImagePoint(NSEvent theEvent)
        {
            var at = ConvertPointFromView(theEvent.LocationInWindow, null);
            return new CGPoint(Math.Clamp(at.X / Scale, 0, owner.imageWidth), Math.Clamp(at.Y / Scale, 0, owner.imageHeight));
        }

        public void Refresh()
        {
            composed?.Dispose();
            composed = null;
            NeedsDisplay = true;
        }

        public override void MouseDown(NSEvent theEvent)
        {
            owner.CommitText();
            var at = ImagePoint(theEvent);
            if (owner.tool == AnnotationTool.Text) { owner.BeginText(ConvertPointFromView(theEvent.LocationInWindow, null), at); return; }
            start = at;
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            if (start is not { } from) return;
            var to = ImagePoint(theEvent);
            draft = new Annotation(owner.tool, from.X, from.Y, to.X, to.Y, owner.color);
            NeedsDisplay = true;
        }

        public override void MouseUp(NSEvent theEvent)
        {
            if (start is not { } from) return;
            start = null;
            draft = null;
            var to = ImagePoint(theEvent);
            if (Math.Abs(to.X - from.X) >= MinDrag || Math.Abs(to.Y - from.Y) >= MinDrag) owner.AddAnnotation(new Annotation(owner.tool, from.X, from.Y, to.X, to.Y, owner.color));
            else NeedsDisplay = true;
        }

        public override void KeyDown(NSEvent theEvent)
        {
            if (theEvent.KeyCode == EscapeKey) owner.Close();
            else if (theEvent.KeyCode is ReturnKey or EnterKey) owner.Finish();
            else base.KeyDown(theEvent);
        }

        [Export("undo:")] public void UndoCommand(NSObject? sender) => owner.Undo();

        public override void DrawRect(CGRect dirtyRect)
        {
            var scale = Scale;
            using var transform = new NSAffineTransform();
            transform.Scale(scale);
            transform.Concat();
            var size = new CGSize(owner.imageWidth, owner.imageHeight);
            if (composed is null)
            {
                using var image = CaptureImage.Compose(owner.pixels, owner.imageWidth, owner.imageHeight, owner.annotations);
                composed = new NSImage(image, size);
            }
            composed.Draw(new CGRect(CGPoint.Empty, size), CGRect.Empty, NSCompositingOperation.SourceOver, 1, true, null);
            foreach (var item in owner.annotations) CaptureImage.Paint(item, owner.density);
            if (draft is not { } current) return;
            if (current.Tool == AnnotationTool.Mosaic)
            {
                NSColor.Gray.SetStroke();
                using var path = NSBezierPath.FromRect(new CGRect(Math.Min(current.X1, current.X2), Math.Min(current.Y1, current.Y2), Math.Abs(current.X2 - current.X1), Math.Abs(current.Y2 - current.Y1)));
                path.LineWidth = (nfloat)owner.density;
                path.SetLineDash([(nfloat)(4 * owner.density), (nfloat)(4 * owner.density)], 0);
                path.Stroke();
            }
            else CaptureImage.Paint(current, owner.density);
        }
    }
}
