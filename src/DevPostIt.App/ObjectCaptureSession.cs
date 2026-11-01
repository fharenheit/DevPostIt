using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DevPostIt.EditorHost;
using Color = System.Windows.Media.Color;

namespace DevPostIt.App;

// 커서 아래 UI 요소를 강조하고 클릭한 요소나 드래그한 영역을 PNG로 돌려줍니다.
// 전역 후크는 이 세션 동안만 설치하며, 끝나면 항상 해제합니다.
public sealed class ObjectCaptureSession : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const int MaxLevelsUp = 12, DragThreshold = 5;
    private readonly Action<byte[]> captured;
    private readonly Action cancelled;
    private readonly bool installHooks;
    private readonly CancellationTokenSource stop = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timeout = new() { Interval = Timeout };
    private readonly HookProc mouseProc;
    private readonly HookProc keyboardProc;
    private IntPtr mouseHook, keyboardHook;
    private FrameWindow? frame;
    private ScreenRect? current;
    private volatile int levelsUp;
    private volatile PressedPoint? pressed;
    private volatile bool dragging;
    private bool finished;

    private sealed record PressedPoint(int X, int Y);

    public ObjectCaptureSession(Action<byte[]> captured, Action cancelled, bool installHooks = true)
    {
        this.captured = captured; this.cancelled = cancelled; this.installHooks = installHooks;
        mouseProc = OnMouse;
        keyboardProc = OnKeyboard;
    }

    public void Start()
    {
        if (finished) return;
        frame = PerMonitor(() => { var window = new FrameWindow(); window.Prepare(); return window; });
        if (installHooks)
        {
            var module = GetModuleHandle(null);
            mouseHook = SetWindowsHookEx(WhMouseLl, mouseProc, module, 0);
            keyboardHook = SetWindowsHookEx(WhKeyboardLl, keyboardProc, module, 0);
        }
        timeout.Tick += (_, _) => Cancel();
        timeout.Start();
        var token = stop.Token;
        _ = Task.Run(() => TrackAsync(token));
    }

    public void Cancel()
    {
        if (finished) return;
        Dispose();
        cancelled();
    }

    private async Task TrackAsync(CancellationToken token)
    {
        var last = new CursorPoint { X = int.MinValue };
        var lastLevels = -1;
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(40, token); }
            catch (OperationCanceledException) { return; }
            var point = Cursor();
            ScreenRect? rect;
            if (pressed is { } start)
            {
                if (!dragging && Math.Abs(point.X - start.X) < DragThreshold && Math.Abs(point.Y - start.Y) < DragThreshold) continue;
                dragging = true;
                rect = PerMonitor(() => CaptureGeometry.FromDrag(start.X, start.Y, point.X, point.Y, VirtualScreen()));
            }
            else
            {
                if (Math.Abs(point.X - last.X) > 2 || Math.Abs(point.Y - last.Y) > 2) levelsUp = 0;
                else if (levelsUp == lastLevels) continue;
                last = point;
                lastLevels = levelsUp;
                rect = HitTest(point.X, point.Y, lastLevels);
            }
            if (token.IsCancellationRequested) return;
            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                if (finished) return;
                current = rect;
                if (rect is { } value) PerMonitor(() => { frame?.MoveTo(value); return true; });
                else frame?.Hide();
            }));
        }
    }

    // 모니터마다 배율이 달라도 커서·요소·캡처 좌표가 같은 물리 픽셀이 되도록 이 범위에서만 모니터별 DPI를 씁니다.
    private static T PerMonitor<T>(Func<T> action)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { return action(); }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }
    private static CursorPoint Cursor() => PerMonitor(() => { GetCursorPos(out var point); return point; });

    // 응답하지 않는 앱에 막힐 수 있어 UI 스레드에서 호출하지 않습니다.
    internal static ScreenRect? HitTest(int x, int y, int levels) => PerMonitor<ScreenRect?>(() =>
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element is null || element.Current.ProcessId == Environment.ProcessId) return null;
            var walker = TreeWalker.ControlViewWalker;
            for (var level = 0; level < levels; level++)
            {
                var parent = walker.GetParent(element);
                if (parent is null || parent == AutomationElement.RootElement) break;
                element = parent;
            }
            var bounds = element.Current.BoundingRectangle;
            return bounds.IsEmpty ? null : CaptureGeometry.ClampToScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height, VirtualScreen());
        }
        catch (Exception error) when (error is ElementNotAvailableException or COMException or InvalidOperationException or TimeoutException) { return null; }
    });

    internal static ScreenRect VirtualScreen() => PerMonitor(() => new ScreenRect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)));

    internal static byte[] CapturePng(ScreenRect rect) => PerMonitor(() =>
    {
        using var bitmap = new System.Drawing.Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(rect.X, rect.Y, 0, 0, new System.Drawing.Size(rect.Width, rect.Height));
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    });

    private async Task CompleteAsync()
    {
        if (finished) return;
        var point = Cursor();
        var rect = dragging && pressed is { } start ? PerMonitor(() => CaptureGeometry.FromDrag(start.X, start.Y, point.X, point.Y, VirtualScreen())) : current;
        if (rect is not { } target) { Cancel(); return; }
        Dispose();
        // 강조 테두리가 사진에 들어가지 않도록 화면에서 사라진 뒤 캡처합니다.
        _ = DwmFlush();
        await Task.Delay(60);
        byte[] png;
        try { png = CapturePng(target); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException) { cancelled(); return; }
        captured(png);
    }

    public void Dispose()
    {
        if (finished) return;
        finished = true;
        stop.Cancel();
        timeout.Stop();
        if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        frame?.Close();
        frame = null;
    }

    // 후크 안에서는 오래 걸리는 일을 하지 않습니다. 클릭은 대상 앱에 전달하지 않습니다.
    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, message, data);
        switch ((int)message)
        {
            case WmLButtonDown:
                var point = Cursor();
                pressed = new PressedPoint(point.X, point.Y);
                return 1;
            case WmRButtonUp: return 1;
            case WmLButtonUp:
                _ = dispatcher.BeginInvoke(new Action(() => _ = CompleteAsync()));
                return 1;
            case WmRButtonDown:
                _ = dispatcher.BeginInvoke(new Action(Cancel));
                return 1;
            case WmMouseWheel:
                var delta = (short)(Marshal.PtrToStructure<MouseHookData>(data).MouseData >> 16);
                levelsUp = delta > 0 ? Math.Min(MaxLevelsUp, levelsUp + 1) : Math.Max(0, levelsUp - 1);
                return 1;
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message == WmKeyDown && Marshal.ReadInt32(data) == VkEscape)
        {
            _ = dispatcher.BeginInvoke(new Action(Cancel));
            return 1;
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    // 클릭이 통과하는 강조 테두리입니다. 요소 검색에 잡히지 않아야 합니다.
    private sealed class FrameWindow : Window
    {
        private IntPtr handle;
        public FrameWindow()
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            Width = Height = 1; Left = Top = -32000;
            Content = new Border { BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.FromRgb(229, 56, 59)), Background = new SolidColorBrush(Color.FromArgb(40, 229, 56, 59)), IsHitTestVisible = false };
        }
        public void Prepare()
        {
            handle = new WindowInteropHelper(this).EnsureHandle();
            SetWindowLongPtr(handle, GwlExStyle, GetWindowLongPtr(handle, GwlExStyle) | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
        }
        public void MoveTo(ScreenRect rect)
        {
            if (!IsVisible) Show();
            SetWindowPos(handle, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, SwpNoActivate | SwpShowWindow);
        }
    }

    private const int WhMouseLl = 14, WhKeyboardLl = 13, WmLButtonDown = 0x201, WmLButtonUp = 0x202, WmRButtonDown = 0x204, WmRButtonUp = 0x205, WmMouseWheel = 0x20A, WmKeyDown = 0x100, VkEscape = 0x1B, GwlExStyle = -20;
    private const long WsExTransparent = 0x20, WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExNoActivate = 0x8000000;
    private const uint SwpNoActivate = 0x10, SwpShowWindow = 0x40;
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookData { public CursorPoint Point; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern long SetWindowLongPtr(IntPtr handle, int index, long value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
