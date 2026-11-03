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

    public static byte[] Render(byte[] png, IReadOnlyList<Annotation> items, double density, CaptureCrop? crop = null)
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
        // 잘린 부분은 결과 그림에 남지 않습니다. 모자이크와 주석은 원본 좌표로 그린 뒤 자릅니다.
        if (crop is not { } area || area.IsFull(width, height)) return ToPng(result!);
        using var cut = result!.WithImageInRect(new CGRect(area.Left, area.Top, area.Width, area.Height));
        return ToPng(cut!);
    }
}

// 캡처한 그림에 주석을 달고 메모에 넣기 전에 확인하는 창입니다. 닫으면 그림을 버립니다.
internal sealed class MacCaptureAnnotationWindow : NSWindow
{
    private const double ToolbarHeight = 44, Margin = 12, MinDrag = 3, MinWidth = 880;
    private const int MaxTextLength = 200;
    private static readonly (string Name, AnnotationTool Tool)[] Tools = [("화살표", AnnotationTool.Arrow), ("사각형", AnnotationTool.Rectangle), ("글자", AnnotationTool.Text), ("모자이크", AnnotationTool.Mosaic)];
    private static readonly (string Name, uint Color)[] Colors = [("빨강", CaptureAnnotations.Red), ("파랑", CaptureAnnotations.Blue), ("검정", CaptureAnnotations.Black), ("노랑", CaptureAnnotations.Yellow)];
    private readonly byte[] original;
    private readonly Action<byte[]> finished;
    private readonly Action<byte[]>? copied;
    private NSStackView? toolbar;
    private readonly double density;
    private readonly int imageWidth, imageHeight;
    private readonly byte[] pixels;
    private readonly List<Annotation> annotations = [];
    private readonly Stack<Action> history = new();
    private CaptureCrop crop;
    private CaptureCrop? cropBeforeDrag;
    private readonly Canvas canvas;
    private AnnotationTool tool = AnnotationTool.Arrow;
    private uint color = CaptureAnnotations.Red;
    private NSTextField? editing;
    private CGPoint editingAt;
    private bool editingCancelled;

    public event Action? WindowClosed;
    internal int AnnotationCount => annotations.Count;
    internal CaptureCrop Crop => crop;
    internal NSView CanvasView => canvas;
    internal nfloat ToolbarWidth => toolbar!.FittingSize.Width + 2 * (nfloat)Margin;

    public MacCaptureAnnotationWindow(byte[] png, double density, Action<byte[]> finished, Action<byte[]>? copied = null)
        : base(new CGRect(0, 0, 640, 360), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
    {
        original = png; this.finished = finished; this.copied = copied; this.density = density;
        (imageWidth, imageHeight, pixels) = CaptureImage.Read(png);
        crop = CaptureCrop.Full(imageWidth, imageHeight);
        ReleaseWhenClosed(false);
        Title = "캡처 주석";
        Level = NSWindowLevel.Floating;
        BackgroundColor = NSColor.FromRgb(255, 253, 245);
        MinSize = new CGSize(MinWidth, 300);
        var area = NSScreen.MainScreen?.VisibleFrame.Size ?? new CGSize(1200, 800);
        SetContentSize(new CGSize(
            Math.Clamp(imageWidth / density + 2 * Margin, MinWidth, Math.Max(MinWidth, area.Width * .9)),
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
            NSButton.CreateButton("클립보드에 넣기", CopyToClipboard),
            NSButton.CreateButton("취소", Close),
            NSButton.CreateButton("메모에 넣기", Finish),
        ]);
        bar.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        bar.Spacing = 8;
        bar.Frame = new CGRect(Margin, bounds.Height - ToolbarHeight, bounds.Width - 2 * Margin, ToolbarHeight);
        bar.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        toolbar = bar;
        return bar;
    }

    internal void AddAnnotation(Annotation annotation)
    {
        annotations.Add(annotation);
        history.Push(() => annotations.RemoveAt(annotations.Count - 1));
        canvas.Refresh();
    }

    internal void Undo()
    {
        if (!history.TryPop(out var restore)) return;
        restore();
        canvas.Refresh();
    }

    private void DragCrop(CropEdge edge, double position)
    {
        cropBeforeDrag ??= crop;
        crop = crop.Move(edge, position, imageWidth, imageHeight);
    }

    // 끄는 동안의 움직임을 묶어 실행 취소 한 번으로 되돌립니다.
    private void EndCropDrag()
    {
        if (cropBeforeDrag is { } before && before != crop) history.Push(() => crop = before);
        cropBeforeDrag = null;
    }

    private byte[] Result() => annotations.Count == 0 && crop.IsFull(imageWidth, imageHeight) ? original : CaptureImage.Render(original, annotations, density, crop);

    internal void Finish()
    {
        CommitText();
        finished(Result());
        Close();
    }

    // 메모에 넣지 않고 파일도 만들지 않습니다.
    internal void CopyToClipboard()
    {
        CommitText();
        copied?.Invoke(Result());
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
        private const double EdgeTolerance = 6, CropLine = 1.5, HandleLength = 32, HandleThickness = 5;
        private NSImage? composed;
        private CGPoint? start;
        private Annotation? draft;
        private CropEdge? draggedEdge;

        public override bool IsFlipped => true;
        public override bool AcceptsFirstResponder() => true;

        public override void ViewDidMoveToWindow()
        {
            base.ViewDidMoveToWindow();
            if (TrackingAreas().Length == 0)
                AddTrackingArea(new NSTrackingArea(CGRect.Empty, NSTrackingAreaOptions.MouseMoved | NSTrackingAreaOptions.CursorUpdate | NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect, this, null));
        }

        public override void CursorUpdate(NSEvent theEvent) => UpdateCursor(theEvent);
        public override void MouseMoved(NSEvent theEvent) => UpdateCursor(theEvent);

        private void UpdateCursor(NSEvent theEvent)
        {
            var cursor = EdgeAt(theEvent) switch
            {
                CropEdge.Left or CropEdge.Right => NSCursor.ResizeLeftRightCursor,
                CropEdge.Top or CropEdge.Bottom => NSCursor.ResizeUpDownCursor,
                _ => NSCursor.CrosshairCursor,
            };
            cursor.Set();
        }

        // 그림 밖의 빈 곳을 누른 경우를 가려야 하므로 그림 안으로 당기지 않은 좌표로 찾습니다.
        private CropEdge? EdgeAt(NSEvent theEvent)
        {
            var at = ConvertPointFromView(theEvent.LocationInWindow, null);
            return owner.crop.EdgeAt(at.X / Scale, at.Y / Scale, EdgeTolerance / Scale);
        }

        private double EdgePosition(CropEdge edge, NSEvent theEvent)
        {
            var at = ConvertPointFromView(theEvent.LocationInWindow, null);
            return (edge is CropEdge.Left or CropEdge.Right ? at.X : at.Y) / Scale;
        }

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
            if (EdgeAt(theEvent) is { } edge) { draggedEdge = edge; return; }
            var at = ImagePoint(theEvent);
            if (owner.tool == AnnotationTool.Text) { owner.BeginText(ConvertPointFromView(theEvent.LocationInWindow, null), at); return; }
            start = at;
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            if (draggedEdge is { } edge)
            {
                owner.DragCrop(edge, EdgePosition(edge, theEvent));
                NeedsDisplay = true;
                return;
            }
            if (start is not { } from) return;
            var to = ImagePoint(theEvent);
            draft = new Annotation(owner.tool, from.X, from.Y, to.X, to.Y, owner.color);
            NeedsDisplay = true;
        }

        public override void MouseUp(NSEvent theEvent)
        {
            if (draggedEdge is not null)
            {
                draggedEdge = null;
                owner.EndCropDrag();
                return;
            }
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
            if (draft is { } current)
            {
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
            DrawCrop(scale);
        }

        // 잘려 나갈 곳을 어둡게 하고 테두리와 손잡이를 그립니다. 손잡이는 잘리지 않도록 영역 안쪽에 둡니다.
        private void DrawCrop(nfloat scale)
        {
            var crop = owner.crop;
            var (width, height) = (owner.imageWidth, owner.imageHeight);
            NSColor.Black.ColorWithAlphaComponent(.5f).SetFill();
            foreach (var part in new[] { new CGRect(0, 0, width, crop.Top), new CGRect(0, crop.Bottom, width, height - crop.Bottom), new CGRect(0, crop.Top, crop.Left, crop.Height), new CGRect(crop.Right, crop.Top, width - crop.Right, crop.Height) })
                if (part.Width > 0 && part.Height > 0)
                    using (var shade = NSBezierPath.FromRect(part)) shade.Fill();
            var line = (nfloat)(CropLine / scale);
            var inset = new CGRect(crop.Left, crop.Top, crop.Width, crop.Height).Inset(line / 2, line / 2);
            NSColor.White.SetStroke();
            using (var border = NSBezierPath.FromRect(inset)) { border.LineWidth = line; border.Stroke(); }
            var (length, thickness) = ((nfloat)(Math.Min(HandleLength / scale, Math.Min(crop.Width, crop.Height))), (nfloat)(HandleThickness / scale));
            var (middleX, middleY) = ((crop.Left + crop.Right - length) / 2, (crop.Top + crop.Bottom - length) / 2);
            foreach (var handle in new[] { new CGRect(crop.Left, middleY, thickness, length), new CGRect(crop.Right - thickness, middleY, thickness, length), new CGRect(middleX, crop.Top, length, thickness), new CGRect(middleX, crop.Bottom - thickness, length, thickness) })
                using (var path = NSBezierPath.FromRect(handle))
                {
                    NSColor.White.SetFill();
                    path.Fill();
                    NSColor.DarkGray.SetStroke();
                    path.LineWidth = 1 / scale;
                    path.Stroke();
                }
        }
    }
}
