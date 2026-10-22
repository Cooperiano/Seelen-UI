using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MenuBar.Services;

public sealed class AppBarService : IDisposable
{
    private const int AbmNew = 0x00000000;
    private const int AbmRemove = 0x00000001;
    private const int AbmQueryPos = 0x00000002;
    private const int AbmSetPos = 0x00000003;
    private const int AbnPosChanged = 0x00000001;
    private const int AbeTop = 1;
    private const uint SwpNoActivate = 0x0010;

    private nint _windowHandle;
    private uint _callbackMessage;
    private double _heightInDip;
    private HwndSource? _source;
    private bool _registered;

    public void Attach(nint windowHandle, double heightInDip)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _heightInDip = heightInDip;
        _callbackMessage = RegisterWindowMessage($"MenuBar.AppBar.{Environment.ProcessId}");

        var data = CreateData();
        data.CallbackMessage = _callbackMessage;
        var registrationResult = SHAppBarMessage(AbmNew, ref data);
        _registered = registrationResult != nint.Zero;

        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WindowMessageHook);
        if (_registered)
        {
            SetPosition();
        }
    }

    public void Dispose()
    {
        _source?.RemoveHook(WindowMessageHook);
        _source = null;

        if (_registered)
        {
            var data = CreateData();
            _ = SHAppBarMessage(AbmRemove, ref data);
            _registered = false;
        }
    }

    public void Reposition()
    {
        if (_registered)
        {
            SetPosition();
        }
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == _callbackMessage && wParam.ToInt32() == AbnPosChanged)
        {
            SetPosition();
            handled = true;
        }

        return nint.Zero;
    }

    private void SetPosition()
    {
        var monitor = MonitorFromWindow(_windowHandle, 2);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var dpi = GetDpiForWindow(_windowHandle);
        var heightInPixels = Math.Max(1, (int)Math.Round(_heightInDip * dpi / 96d));
        var rectangle = new NativeRect
        {
            Left = monitorInfo.Monitor.Left,
            Top = monitorInfo.Monitor.Top,
            Right = monitorInfo.Monitor.Right,
            Bottom = monitorInfo.Monitor.Top + heightInPixels
        };

        var data = CreateData();
        data.Edge = AbeTop;
        data.Rectangle = rectangle;
        _ = SHAppBarMessage(AbmQueryPos, ref data);
        data.Rectangle.Bottom = data.Rectangle.Top + heightInPixels;
        _ = SHAppBarMessage(AbmSetPos, ref data);

        _ = SetWindowPos(
            _windowHandle,
            new nint(-1),
            data.Rectangle.Left,
            data.Rectangle.Top,
            data.Rectangle.Right - data.Rectangle.Left,
            data.Rectangle.Bottom - data.Rectangle.Top,
            SwpNoActivate);
    }

    private AppBarData CreateData() => new()
    {
        Size = Marshal.SizeOf<AppBarData>(),
        WindowHandle = _windowHandle
    };

    [DllImport("shell32.dll")]
    private static extern nint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public nint WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rectangle;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
