using System.Runtime.InteropServices;
using CoreGraphics;
using DevPostIt.EditorHost;
using ObjCRuntime;
using ScreenCaptureKit;

namespace DevPostIt.Mac;

// 커서 아래 요소를 찾습니다. 손쉬운 사용 권한이 있으면 창 안의 요소까지, 없으면 창 단위로 찾습니다.
internal static class MacWindowLookup
{
    private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ServicesLibrary = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint OnScreenOnly = 1, ExcludeDesktop = 16;
    private const int PointType = 1, SizeType = 2;
    private const float MessagingTimeout = 0.2f;

    public static ScreenRect? Find(int x, int y, int levels) => FindElement(x, y, levels) ?? FindWindow(x, y);

    // 앞쪽 창부터 나옵니다. 이 앱의 창과 화면 전체를 덮는 시스템 창은 고르지 않습니다.
    internal static ScreenRect? FindWindow(int x, int y)
    {
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktop, 0);
        if (list == IntPtr.Zero) return null;
        using var windows = Runtime.GetNSObject<NSArray>(list, true);
        if (windows is null) return null;
        for (nuint index = 0; index < windows.Count; index++)
        {
            if (windows.GetItem<NSDictionary>(index) is not { } window) continue;
            if (Number(window, "kCGWindowOwnerPID") == Environment.ProcessId || Number(window, "kCGWindowAlpha") == 0) continue;
            if (window["kCGWindowOwnerName"]?.ToString() == "Window Server") continue;
            if (window["kCGWindowBounds"] is not NSDictionary bounds) continue;
            var (left, top, width, height) = (Number(bounds, "X"), Number(bounds, "Y"), Number(bounds, "Width"), Number(bounds, "Height"));
            if (x >= left && y >= top && x < left + width && y < top + height) return new ScreenRect((int)left, (int)top, (int)width, (int)height);
        }
        return null;
    }

    private static double Number(NSDictionary dictionary, string key) => (dictionary[key] as NSNumber)?.DoubleValue ?? 0;

    private static ScreenRect? FindElement(int x, int y, int levels)
    {
        if (!AXIsProcessTrusted()) return null;
        var system = AXUIElementCreateSystemWide();
        try
        {
            AXUIElementSetMessagingTimeout(system, MessagingTimeout);
            if (AXUIElementCopyElementAtPosition(system, x, y, out var element) != 0 || element == IntPtr.Zero) return null;
            try
            {
                if (AXUIElementGetPid(element, out var pid) != 0 || pid == Environment.ProcessId) return null;
                for (var level = 0; level < levels; level++)
                {
                    using var parentKey = new NSString("AXParent");
                    if (AXUIElementCopyAttributeValue(element, parentKey.Handle, out var parent) != 0 || parent == IntPtr.Zero) break;
                    CFRelease(element);
                    element = parent;
                }
                return Bounds(element);
            }
            finally { CFRelease(element); }
        }
        finally { CFRelease(system); }
    }

    private static ScreenRect? Bounds(IntPtr element)
    {
        using var positionKey = new NSString("AXPosition");
        using var sizeKey = new NSString("AXSize");
        if (AXUIElementCopyAttributeValue(element, positionKey.Handle, out var position) != 0 || position == IntPtr.Zero) return null;
        try
        {
            if (AXUIElementCopyAttributeValue(element, sizeKey.Handle, out var size) != 0 || size == IntPtr.Zero) return null;
            try
            {
                if (!AXValueGetValue(position, PointType, out CGPoint point) || !AXValueGetValue(size, SizeType, out CGSize extent)) return null;
                return new ScreenRect((int)point.X, (int)point.Y, (int)extent.Width, (int)extent.Height);
            }
            finally { CFRelease(size); }
        }
        finally { CFRelease(position); }
    }

    [DllImport(CoreGraphicsLibrary)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(ServicesLibrary)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
    [DllImport(ServicesLibrary)] private static extern IntPtr AXUIElementCreateSystemWide();
    [DllImport(ServicesLibrary)] private static extern int AXUIElementSetMessagingTimeout(IntPtr element, float seconds);
    [DllImport(ServicesLibrary)] private static extern int AXUIElementCopyElementAtPosition(IntPtr application, float x, float y, out IntPtr element);
    [DllImport(ServicesLibrary)] private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);
    [DllImport(ServicesLibrary)] private static extern int AXUIElementGetPid(IntPtr element, out int pid);
    [DllImport(ServicesLibrary)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXValueGetValue(IntPtr value, int type, out CGPoint point);
    [DllImport(ServicesLibrary)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXValueGetValue(IntPtr value, int type, out CGSize size);
    [DllImport(CoreFoundationLibrary)] private static extern void CFRelease(IntPtr value);
}

internal static class MacScreenCapture
{
    private const string Library = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string PermissionPage = "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_ScreenCapture";

    // 권한이 없으면 이유를 말하고 시스템 설정으로 안내합니다. 조용히 실패하지 않습니다.
    public static bool EnsurePermission()
    {
        if (CGPreflightScreenCaptureAccess()) return true;
        CGRequestScreenCaptureAccess();
        if (MacDialogs.Ask("화면 캡처에는 화면 기록 권한이 필요합니다.", "시스템 설정의 ‘화면 및 시스템 오디오 녹음’에서 이 앱을 허용한 뒤 앱을 다시 시작해 주세요. 빌드가 바뀌면 다시 허용해야 할 수 있습니다.", "닫기", "시스템 설정 열기") == 1)
            NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(PermissionPage));
        return false;
    }

    // 모니터를 걸친 영역은 모니터별로 찍어 합칩니다. 이 앱의 창은 사진에 넣지 않습니다.
    public static async Task<(byte[] Png, double Density)> CaptureAsync(ScreenRect rect)
    {
        var content = await SCShareableContent.GetShareableContentAsync();
        var own = content.Windows.Where(window => window.OwningApplication?.ProcessId == Environment.ProcessId).ToArray();
        var area = new CGRect(rect.X, rect.Y, rect.Width, rect.Height);
        var parts = new List<(CGImage Image, CGRect Frame)>();
        double density = 1;
        foreach (var display in content.Displays)
        {
            var shared = CGRect.Intersect(area, display.Frame);
            if (shared.IsEmpty) continue;
            using var filter = new SCContentFilter(display, own, SCContentFilterOption.Exclude);
            var scale = (double)filter.PointPixelScale;
            density = Math.Max(density, scale);
            using var configuration = new SCStreamConfiguration
            {
                SourceRect = new CGRect(shared.X - display.Frame.X, shared.Y - display.Frame.Y, shared.Width, shared.Height),
                Width = (nuint)(shared.Width * scale),
                Height = (nuint)(shared.Height * scale),
                ShowsCursor = false,
            };
            parts.Add((await SCScreenshotManager.CaptureImageAsync(filter, configuration), shared));
        }
        if (parts.Count == 0) throw new InvalidOperationException("캡처할 화면을 찾지 못했습니다.");
        return (Combine(parts, area, density), density);
    }

    private static byte[] Combine(List<(CGImage Image, CGRect Frame)> parts, CGRect area, double density)
    {
        var (width, height) = ((int)(area.Width * density), (int)(area.Height * density));
        using var space = CGColorSpace.CreateSrgb();
        using var context = new CGBitmapContext(null, width, height, 8, width * 4, space, CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);
        context.SetFillColor(1, 1, 1, 1);
        context.FillRect(new CGRect(0, 0, width, height));
        foreach (var (image, frame) in parts)
        {
            var (left, top) = ((frame.X - area.X) * density, (frame.Y - area.Y) * density);
            context.DrawImage(new CGRect(left, height - top - frame.Height * density, frame.Width * density, frame.Height * density), image);
            image.Dispose();
        }
        using var combined = context.ToImage();
        return CaptureImage.ToPng(combined!);
    }

    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestScreenCaptureAccess();
}

// 어디서나 캡처를 시작하는 ⌃⇧⌘C입니다. 후크가 아니라 시스템에 키 하나만 등록하며 권한이 필요 없습니다. 다른 앱이 쓰고 있으면 넘어갑니다.
internal sealed class MacCaptureHotkey : IDisposable
{
    private const string Library = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint KeyC = 8, CommandKey = 1 << 8, ShiftKey = 1 << 9, ControlKey = 1 << 12;
    private const uint KeyboardClass = 0x6B657962, HotKeyPressed = 5, Signature = 0x4450;
    private delegate int EventHandler(IntPtr caller, IntPtr theEvent, IntPtr userData);
    private readonly EventHandler handler;
    private readonly Action pressed;
    private IntPtr hotKey, installed;
    public bool IsRegistered => hotKey != IntPtr.Zero;

    public MacCaptureHotkey(Action pressed)
    {
        this.pressed = pressed;
        handler = (_, _, _) => { this.pressed(); return 0; };
        var spec = new EventTypeSpec { Class = KeyboardClass, Kind = HotKeyPressed };
        if (InstallEventHandler(GetApplicationEventTarget(), Marshal.GetFunctionPointerForDelegate(handler), 1, ref spec, IntPtr.Zero, out installed) != 0) return;
        var id = new EventHotKeyId { Signature = Signature, Id = 1 };
        if (RegisterEventHotKey(KeyC, CommandKey | ShiftKey | ControlKey, id, GetEventDispatcherTarget(), 0, out var registered) == 0) hotKey = registered;
    }

    public void Dispose()
    {
        if (hotKey != IntPtr.Zero) UnregisterEventHotKey(hotKey);
        if (installed != IntPtr.Zero) RemoveEventHandler(installed);
        hotKey = installed = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct EventTypeSpec { public uint Class, Kind; }
    [StructLayout(LayoutKind.Sequential)] private struct EventHotKeyId { public uint Signature, Id; }
    [DllImport(Library)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Library)] private static extern IntPtr GetEventDispatcherTarget();
    [DllImport(Library)] private static extern int InstallEventHandler(IntPtr target, IntPtr handler, uint count, ref EventTypeSpec types, IntPtr userData, out IntPtr reference);
    [DllImport(Library)] private static extern int RemoveEventHandler(IntPtr reference);
    [DllImport(Library)] private static extern int RegisterEventHotKey(uint key, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr reference);
    [DllImport(Library)] private static extern int UnregisterEventHotKey(IntPtr reference);
}
