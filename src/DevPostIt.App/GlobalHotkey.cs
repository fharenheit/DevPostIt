using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DevPostIt.App;

// 다른 앱을 쓰는 중에도 캡처를 시작하는 전역 단축키입니다. 다른 프로그램이 쓰고 있으면 등록하지 않고 넘어갑니다.
internal sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x312, HotkeyId = 0x4450;
    private const uint ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000, VkSnapshot = 0x2C;
    private readonly HwndSource source;
    private readonly Action pressed;
    public bool IsRegistered { get; }

    public GlobalHotkey(Action pressed)
    {
        this.pressed = pressed;
        source = new HwndSource(new HwndSourceParameters("DevPostItHotkey") { Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(OnMessage);
        IsRegistered = RegisterHotKey(source.Handle, HotkeyId, ModControl | ModShift | ModNoRepeat, VkSnapshot);
    }
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId) { handled = true; pressed(); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (IsRegistered) UnregisterHotKey(source.Handle, HotkeyId);
        source.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
