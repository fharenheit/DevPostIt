using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevPostIt.EditorHost;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace DevPostIt.App;

// 캡처한 그림에 주석을 달고 메모에 넣기 전에 확인하는 창입니다. 닫으면 그림을 버립니다.
public sealed class CaptureAnnotationWindow : Window
{
    private const double StrokeWidth = 3, ArrowSize = 16, TextSize = 20, MinDrag = 3, EdgeTolerance = 6, CropLine = 1.5, HandleLength = 32, HandleThickness = 5;
    private static readonly FontFamily TextFont = new("Segoe UI, Malgun Gothic");
    private readonly byte[] original;
    private readonly Action<byte[]> finished;
    private readonly Action<byte[]>? copied;
    private readonly int imageWidth, imageHeight;
    private readonly byte[] pixels;
    private readonly List<Annotation> annotations = [];
    private readonly Stack<Action> history = new();
    private readonly Layer layer;
    private readonly Canvas overlay;
    private AnnotationTool tool = AnnotationTool.Arrow;
    private uint color = CaptureAnnotations.Red;
    private CaptureCrop crop;
    private CaptureCrop? cropBeforeDrag;
    private CropEdge? draggedEdge;
    private Point? start;
    private TextBox? editing;

    internal int AnnotationCount => annotations.Count;
    internal CaptureCrop Crop => crop;

    public CaptureAnnotationWindow(byte[] png, Action<byte[]> finished, Action<byte[]>? copied = null)
    {
        original = png; this.finished = finished; this.copied = copied;
        (imageWidth, imageHeight, pixels) = Read(png);
        crop = CaptureCrop.Full(imageWidth, imageHeight);
        Title = "캡처 주석";
        Icon = NoteIcon.WindowImage;
        Topmost = true;
        FontFamily = TextFont;
        FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(255, 253, 245));
        var area = SystemParameters.WorkArea;
        Width = Math.Clamp(imageWidth + 40.0, 640, Math.Max(640, area.Width * .9));
        Height = Math.Clamp(imageHeight + 110.0, 360, Math.Max(360, area.Height * .9));
        MinWidth = 560; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) => MainWindow.ApplyTitleBarColors(new WindowInteropHelper(this).Handle);

        layer = new Layer(this) { Width = imageWidth, Height = imageHeight, Cursor = System.Windows.Input.Cursors.Cross };
        overlay = new Canvas { Width = imageWidth, Height = imageHeight };
        layer.MouseLeftButtonDown += PointerDown;
        layer.MouseMove += PointerMove;
        layer.MouseLeftButtonUp += PointerUp;
        var picture = new Grid { Width = imageWidth, Height = imageHeight, Children = { layer, overlay } };
        var root = new DockPanel();
        var tools = Toolbar();
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);
        root.Children.Add(new Viewbox { Child = picture, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(12) });
        Content = root;
        PreviewKeyDown += KeyPressed;
        Loaded += (_, _) => { Activate(); layer.InvalidateVisual(); };
        // 그림의 배율이 바뀌면 테두리와 손잡이의 화면 크기를 다시 맞춥니다.
        SizeChanged += (_, _) => layer.InvalidateVisual();
    }

    private WrapPanel Toolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 0) };
        foreach (var (name, value) in new[] { ("화살표", AnnotationTool.Arrow), ("사각형", AnnotationTool.Rectangle), ("글자", AnnotationTool.Text), ("모자이크", AnnotationTool.Mosaic) })
        {
            var option = new RadioButton { Content = name, GroupName = "tool", IsChecked = value == tool, Margin = new Thickness(0, 4, 10, 4), VerticalContentAlignment = VerticalAlignment.Center };
            option.Checked += (_, _) => { CommitText(); tool = value; };
            bar.Children.Add(option);
        }
        foreach (var (name, value) in new[] { ("빨강", CaptureAnnotations.Red), ("파랑", CaptureAnnotations.Blue), ("검정", CaptureAnnotations.Black), ("노랑", CaptureAnnotations.Yellow) })
        {
            var option = new RadioButton { GroupName = "color", IsChecked = value == color, ToolTip = name, Margin = new Thickness(0, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, Content = new Border { Width = 16, Height = 16, BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, Background = Brush(value) } };
            AutomationPropertiesName(option, name);
            option.Checked += (_, _) => color = value;
            bar.Children.Add(option);
        }
        bar.Children.Add(Action("실행 취소", Undo, new Thickness(8, 0, 12, 0)));
        bar.Children.Add(Action("그대로 넣기", InsertOriginal, new Thickness(0, 0, 4, 0)));
        bar.Children.Add(Action("클립보드에 넣기", CopyToClipboard, new Thickness(0, 0, 4, 0)));
        bar.Children.Add(Action("취소", Close, new Thickness(0, 0, 4, 0)));
        bar.Children.Add(Action("메모에 넣기", Finish, new Thickness(0)));
        return bar;
    }
    private static void AutomationPropertiesName(DependencyObject element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
    private static Button Action(string name, Action action, Thickness margin)
    {
        var button = new Button { Content = name, Padding = new Thickness(10, 2, 10, 2), Margin = margin };
        button.Click += (_, _) => action();
        return button;
    }

    internal void AddAnnotation(Annotation annotation)
    {
        annotations.Add(annotation);
        history.Push(() => annotations.RemoveAt(annotations.Count - 1));
        layer.Refresh();
    }
    internal void Undo()
    {
        if (!history.TryPop(out var restore)) return;
        restore();
        layer.Refresh();
    }
    internal void DragCrop(CropEdge edge, double position)
    {
        cropBeforeDrag ??= crop;
        crop = crop.Move(edge, position, imageWidth, imageHeight);
        layer.InvalidateVisual();
    }
    // 끄는 동안의 움직임을 묶어 실행 취소 한 번으로 되돌립니다.
    internal void EndCropDrag()
    {
        if (cropBeforeDrag is { } before && before != crop) history.Push(() => crop = before);
        cropBeforeDrag = null;
    }
    private byte[] Result() => annotations.Count == 0 && crop.IsFull(imageWidth, imageHeight) ? original : Render(original, annotations, crop);
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

    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        CommitText();
        var at = e.GetPosition(layer);
        if (crop.EdgeAt(at.X, at.Y, EdgeTolerance / ViewScale) is { } edge)
        {
            draggedEdge = edge;
            layer.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (tool == AnnotationTool.Text) { BeginText(at); e.Handled = true; return; }
        start = at;
        layer.CaptureMouse();
    }
    private void PointerMove(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(layer);
        if (draggedEdge is { } edge) DragCrop(edge, edge is CropEdge.Left or CropEdge.Right ? at.X : at.Y);
        else if (start is { } from) layer.ShowDraft(new Annotation(tool, from.X, from.Y, at.X, at.Y, color));
        else layer.Cursor = crop.EdgeAt(at.X, at.Y, EdgeTolerance / ViewScale) switch
        {
            CropEdge.Left or CropEdge.Right => System.Windows.Input.Cursors.SizeWE,
            CropEdge.Top or CropEdge.Bottom => System.Windows.Input.Cursors.SizeNS,
            _ => System.Windows.Input.Cursors.Cross,
        };
    }
    // 큰 그림은 창에 맞춰 줄어듭니다. 테두리와 손잡이가 화면에서 같은 크기로 보이도록 씁니다.
    private double ViewScale => Math.Max(layer.TransformToAncestor(this).TransformBounds(new Rect(0, 0, 1, 1)).Width, .01);
    private void PointerUp(object sender, MouseButtonEventArgs e)
    {
        if (draggedEdge is not null)
        {
            draggedEdge = null;
            layer.ReleaseMouseCapture();
            EndCropDrag();
            return;
        }
        if (start is not { } from) return;
        start = null;
        layer.ReleaseMouseCapture();
        layer.ShowDraft(null);
        var to = e.GetPosition(layer);
        if (Math.Abs(to.X - from.X) >= MinDrag || Math.Abs(to.Y - from.Y) >= MinDrag) AddAnnotation(new Annotation(tool, from.X, from.Y, to.X, to.Y, color));
    }
    private void BeginText(Point at)
    {
        var box = new TextBox { FontFamily = TextFont, FontSize = TextSize, Foreground = Brush(color), MinWidth = 120, MaxLength = 200, Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) };
        Canvas.SetLeft(box, at.X);
        Canvas.SetTop(box, at.Y);
        overlay.Children.Add(box);
        editing = box;
        box.LostKeyboardFocus += (_, _) => { if (editing == box) CommitText(); };
        box.Focus();
    }
    private void CommitText()
    {
        if (editing is not { } box) return;
        editing = null;
        overlay.Children.Remove(box);
        if (box.Text.Trim().Length > 0)
            AddAnnotation(new Annotation(AnnotationTool.Text, Canvas.GetLeft(box), Canvas.GetTop(box), 0, 0, ((box.Foreground as SolidColorBrush)?.Color is { } value) ? (uint)(value.R << 16 | value.G << 8 | value.B) : color, box.Text.Trim()));
    }
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (editing is { } box)
        {
            if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
            else if (e.Key == Key.Escape) { editing = null; overlay.Children.Remove(box); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Finish(); e.Handled = true; }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { Undo(); e.Handled = true; }
    }

    private static SolidColorBrush Brush(uint value)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value));
        brush.Freeze();
        return brush;
    }
    private static (int Width, int Height, byte[] Pixels) Read(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var frame = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var data = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        frame.CopyPixels(data, frame.PixelWidth * 4, 0);
        return (frame.PixelWidth, frame.PixelHeight, data);
    }
    // 모자이크는 픽셀 자체를 바꿉니다. 위에 덮어 그리면 원본이 그림 파일에 남기 때문입니다.
    private static BitmapSource Compose(byte[] source, int width, int height, IEnumerable<Annotation> items)
    {
        var data = source.ToArray();
        foreach (var item in items)
            if (item.Tool == AnnotationTool.Mosaic && CaptureAnnotations.Area(item.X1, item.Y1, item.X2, item.Y2, width, height) is { } area)
                CaptureAnnotations.Pixelate(data, width, height, area, CaptureAnnotations.MosaicBlock);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
    private static void Paint(DrawingContext context, Annotation item)
    {
        var brush = Brush(item.Color);
        var pen = new Pen(brush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        switch (item.Tool)
        {
            case AnnotationTool.Rectangle:
                context.DrawRectangle(null, pen, new Rect(new Point(item.X1, item.Y1), new Point(item.X2, item.Y2)));
                break;
            case AnnotationTool.Arrow:
                context.DrawLine(pen, new Point(item.X1, item.Y1), new Point(item.X2, item.Y2));
                var head = CaptureAnnotations.ArrowHead(item.X1, item.Y1, item.X2, item.Y2, ArrowSize);
                var figure = new PathFigure(new Point(head[0].X, head[0].Y), [new LineSegment(new Point(head[1].X, head[1].Y), true), new LineSegment(new Point(head[2].X, head[2].Y), true)], true);
                context.DrawGeometry(brush, null, new PathGeometry([figure]));
                break;
            case AnnotationTool.Text:
                var typeface = new Typeface(TextFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                context.DrawText(new FormattedText(item.Text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, typeface, TextSize, brush, 1.0), new Point(item.X1, item.Y1));
                break;
        }
    }
    public static byte[] Render(byte[] png, IReadOnlyList<Annotation> items, CaptureCrop? crop = null)
    {
        var (width, height, data) = Read(png);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(Compose(data, width, height, items), new Rect(0, 0, width, height));
            foreach (var item in items) Paint(context, item);
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        // 잘린 부분은 결과 그림에 남지 않습니다. 모자이크와 주석은 원본 좌표로 그린 뒤 자릅니다.
        BitmapSource result = crop is { } area && !area.IsFull(width, height) ? new CroppedBitmap(target, new Int32Rect(area.Left, area.Top, area.Width, area.Height)) : target;
        encoder.Frames.Add(BitmapFrame.Create(result));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class Layer(CaptureAnnotationWindow owner) : FrameworkElement
    {
        private BitmapSource? composed;
        private Annotation? draft;
        public void Refresh()
        {
            composed = Compose(owner.pixels, owner.imageWidth, owner.imageHeight, owner.annotations);
            InvalidateVisual();
        }
        public void ShowDraft(Annotation? value)
        {
            draft = value;
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext context)
        {
            composed ??= Compose(owner.pixels, owner.imageWidth, owner.imageHeight, owner.annotations);
            context.DrawImage(composed, new Rect(0, 0, owner.imageWidth, owner.imageHeight));
            foreach (var item in owner.annotations) Paint(context, item);
            if (draft is not null)
            {
                if (draft.Tool == AnnotationTool.Mosaic)
                {
                    var pen = new Pen(Brushes.DimGray, 1) { DashStyle = DashStyles.Dash };
                    pen.Freeze();
                    context.DrawRectangle(null, pen, new Rect(new Point(draft.X1, draft.Y1), new Point(draft.X2, draft.Y2)));
                }
                else Paint(context, draft);
            }
            DrawCrop(context);
        }
        // 잘려 나갈 곳을 어둡게 하고 테두리와 손잡이를 그립니다. 손잡이는 잘리지 않도록 영역 안쪽에 둡니다.
        private void DrawCrop(DrawingContext context)
        {
            var crop = owner.crop;
            var (width, height) = (owner.imageWidth, owner.imageHeight);
            var shade = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
            shade.Freeze();
            context.DrawRectangle(shade, null, new Rect(0, 0, width, crop.Top));
            context.DrawRectangle(shade, null, new Rect(0, crop.Bottom, width, height - crop.Bottom));
            context.DrawRectangle(shade, null, new Rect(0, crop.Top, crop.Left, crop.Height));
            context.DrawRectangle(shade, null, new Rect(crop.Right, crop.Top, width - crop.Right, crop.Height));
            var scale = owner.IsLoaded ? owner.ViewScale : 1;
            var line = CropLine / scale;
            var border = new Pen(Brushes.White, line);
            border.Freeze();
            context.DrawRectangle(null, border, new Rect(crop.Left + line / 2, crop.Top + line / 2, Math.Max(crop.Width - line, 0), Math.Max(crop.Height - line, 0)));
            var outline = new Pen(Brushes.DimGray, 1 / scale);
            outline.Freeze();
            var (length, thickness) = (Math.Min(HandleLength / scale, Math.Min(crop.Width, crop.Height)), HandleThickness / scale);
            var (middleX, middleY) = ((crop.Left + crop.Right - length) / 2, (crop.Top + crop.Bottom - length) / 2);
            foreach (var handle in new[] { new Rect(crop.Left, middleY, thickness, length), new Rect(crop.Right - thickness, middleY, thickness, length), new Rect(middleX, crop.Top, length, thickness), new Rect(middleX, crop.Bottom - thickness, length, thickness) })
                context.DrawRectangle(Brushes.White, outline, handle);
        }
    }
}
