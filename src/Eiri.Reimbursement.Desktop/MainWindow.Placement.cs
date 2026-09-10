using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eiri.Reimbursement.Desktop;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ConstrainMaximizedBounds);
    }

    private nint ConstrainMaximizedBounds(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        if (message != wmGetMinMaxInfo) return 0;

        var monitor = MonitorFromWindow(window, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return 0;

        // Win32 expects physical pixels relative to the current monitor, including at mixed DPI.
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        bounds.MaxSize.X = info.Work.Right - info.Work.Left;
        bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        Marshal.StructureToPtr(bounds, lParam, false);
        // Let WPF continue applying its minimum/maximum resize constraints.
        return 0;
    }

    private void PositionArchiveWindow()
    {
        if (Owner is null) return;

        var ownerHandle = new WindowInteropHelper(Owner).Handle;
        var monitor = MonitorFromWindow(ownerHandle, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var workArea = SystemParameters.WorkArea;
        var source = HwndSource.FromHwnd(ownerHandle);
        if (GetMonitorInfo(monitor, ref info) && source?.CompositionTarget is { } target)
        {
            var transform = target.TransformFromDevice;
            workArea = new Rect(
                transform.Transform(new Point(info.Work.Left, info.Work.Top)),
                transform.Transform(new Point(info.Work.Right, info.Work.Bottom)));
        }

        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 32));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 32));
        var origin = source?.CompositionTarget?.TransformFromDevice.Transform(Owner.PointToScreen(new Point()))
            ?? new Point(Owner.Left, Owner.Top);
        Left = OffsetWithinWorkArea(origin.X, Width, workArea.Left, workArea.Right);
        Top = OffsetWithinWorkArea(origin.Y, Height, workArea.Top, workArea.Bottom);
    }

    private static double OffsetWithinWorkArea(double ownerPosition, double size, double start, double end)
    {
        const double offset = 32;
        double position = ownerPosition + offset;
        if (position + size > end) position = ownerPosition - offset;
        return Math.Clamp(position, start, Math.Max(start, end - size));
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
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
