using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MenuBar.Services;
using MenuBar.ViewModels;

namespace MenuBar;

public partial class ModulePopupWindow : Window
{
    private const int WhMouseLl = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private readonly ModulePopupViewModel _viewModel;
    private readonly MetricAnchorReader _anchorReader;
    private readonly DispatcherTimer _positionTimer;
    private readonly Queue<double> _history = new();
    private readonly LowLevelMouseProc _mouseHookCallback;
    private nint _mouseHook;
    private nint _windowHandle;
    private (int Left, int Top)? _lastAnchorPosition;

    public ModulePopupWindow(string metricId, LiveMetricsHub? liveMetrics = null)
    {
        InitializeComponent();
        _mouseHookCallback = OnLowLevelMouse;
        _anchorReader = new MetricAnchorReader(metricId);
        _positionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Background,
            (_, _) => TryPositionAtAnchor(),
            Dispatcher);
        _viewModel = new ModulePopupViewModel(metricId, liveMetrics: liveMetrics);
        DataContext = _viewModel;
        foreach (var value in _viewModel.InitialHistory)
        {
            _history.Enqueue(value);
        }
        ProcessSection.Visibility = _viewModel.ShowsProcesses ? Visibility.Visible : Visibility.Collapsed;
        CoreSection.Visibility = _viewModel.ShowsCores ? Visibility.Visible : Visibility.Collapsed;
        DetailsSection.Visibility = _viewModel.ShowsDetails ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.SampleUpdated += OnSampleUpdated;
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;
    }

    public event EventHandler? SettingsRequested;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (!TryPositionAtAnchor())
        {
            PositionAtFallback();
        }
        _positionTimer.Start();
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookCallback, nint.Zero, 0);
        _viewModel.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Deactivated -= OnDeactivated;
        PreviewKeyDown -= OnPreviewKeyDown;
        _positionTimer.Stop();
        _anchorReader.Dispose();
        if (_mouseHook != nint.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }
        _viewModel.SampleUpdated -= OnSampleUpdated;
        _viewModel.Dispose();
    }

    private bool TryPositionAtAnchor()
    {
        if (_windowHandle == nint.Zero ||
            !_anchorReader.TryRead(out var anchor) ||
            !GetWindowRect(_windowHandle, out var windowBounds))
        {
            return false;
        }

        var anchorBounds = new NativeRect
        {
            Left = anchor.Left,
            Top = anchor.Top,
            Right = anchor.Right,
            Bottom = anchor.Bottom
        };
        var monitor = MonitorFromRect(ref anchorBounds, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var workArea = new ScreenRect(
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Right,
            monitorInfo.WorkArea.Bottom);
        var position = PopupPlacement.Calculate(
            anchor,
            workArea,
            windowBounds.Right - windowBounds.Left,
            windowBounds.Bottom - windowBounds.Top);
        if (_lastAnchorPosition == position)
        {
            return true;
        }

        if (!SetWindowPos(
                _windowHandle,
                nint.Zero,
                position.Left,
                position.Top,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            return false;
        }

        _lastAnchorPosition = position;
        return true;
    }

    private void PositionAtFallback()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var workArea = SystemParameters.WorkArea;
        var workAreaLeft = workArea.Left / dpi.DpiScaleX;
        var workAreaRight = workArea.Right / dpi.DpiScaleX;
        var workAreaTop = workArea.Top / dpi.DpiScaleY;
        Left = Math.Max(workAreaLeft + 12, workAreaRight - ActualWidth - 18);
        Top = workAreaTop + 34 / dpi.DpiScaleY;
    }

    private nint OnLowLevelMouse(int code, nint message, nint data)
    {
        if (code >= 0 && IsMouseDownMessage((int)message) && _windowHandle != nint.Zero &&
            GetWindowRect(_windowHandle, out var bounds))
        {
            var mouse = Marshal.PtrToStructure<MouseHookData>(data).Point;
            if (mouse.X < bounds.Left || mouse.X >= bounds.Right ||
                mouse.Y < bounds.Top || mouse.Y >= bounds.Bottom)
            {
                Dispatcher.BeginInvoke(Close);
            }
        }

        return CallNextHookEx(_mouseHook, code, message, data);
    }

    private static bool IsMouseDownMessage(int message) => message is
        WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown or WmXButtonDown;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnSampleUpdated(double? value)
    {
        if (value is not null && double.IsFinite(value.Value))
        {
            _history.Enqueue(value.Value);
            while (_history.Count > _viewModel.MaxHistorySamples)
            {
                _history.Dequeue();
            }
        }

        DrawHistory();
    }

    private void DrawHistory()
    {
        HistoryCanvas.Children.Clear();
        if (_history.Count < 2 || HistoryCanvas.ActualWidth <= 1 || HistoryCanvas.ActualHeight <= 1)
        {
            return;
        }

        var values = _history.ToArray();
        var maximum = Math.Max(100d, values.Max());
        var points = new PointCollection(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            var x = index * HistoryCanvas.ActualWidth / Math.Max(1, values.Length - 1);
            var y = HistoryCanvas.ActualHeight - values[index] / maximum * (HistoryCanvas.ActualHeight - 8) - 4;
            points.Add(new Point(x, y));
        }

        var color = (Color)ColorConverter.ConvertFromString(_viewModel.Accent);
        HistoryCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round
        });
    }

    private void HistoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawHistory();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private delegate nint LowLevelMouseProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, LowLevelMouseProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect bounds, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
