using System.Runtime.InteropServices;

namespace WordStrip.App.Interop;

/// <summary>
/// One display's usable area and scale, in physical pixels.
///
/// <para><b>Why not <c>SystemParameters.WorkArea</c>.</b> That is the <em>primary</em> monitor's work area,
/// expressed at the primary monitor's scale. On a second display it is the wrong rectangle and often the
/// wrong scale as well, so a bar placed from it lands on the wrong screen, or lands on the right screen at
/// the wrong size. Everything here is asked of the monitor the user is actually typing on.</para>
///
/// <para>Physical pixels throughout, because that is what the caret arrives in and what
/// <c>SetWindowPos</c> takes. <see cref="Scale"/> converts to the device-independent units WPF lays out in.</para>
/// </summary>
internal readonly record struct MonitorLayout(
    int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, double Scale)
{
    public int WorkWidth => WorkRight - WorkLeft;
    public int WorkHeight => WorkBottom - WorkTop;

    /// <summary>The work area's width in device-independent units, for layout ceilings.</summary>
    public double WorkWidthDip => WorkWidth / Scale;

    /// <summary>The monitor holding a point given in physical screen pixels.</summary>
    public static MonitorLayout ForPoint(int x, int y) =>
        Describe(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    /// <summary>The monitor holding a window, or the nearest one.</summary>
    public static MonitorLayout ForWindow(nint hwnd) =>
        hwnd == 0 ? Primary() : Describe(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    /// <summary>The monitor holding whatever the user is typing into.</summary>
    public static MonitorLayout ForForegroundWindow() => ForWindow(GetForegroundWindow());

    public static MonitorLayout Primary() => Describe(MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY));

    private static MonitorLayout Describe(nint monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

        // A monitor can be removed between one call and the next, and a work area of zero would place the
        // bar at the origin of nothing. The 1920x1080 fallback is only ever reached in that window.
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return new MonitorLayout(0, 0, 1920, 1080, 1.0);

        return new MonitorLayout(
            info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom, ScaleOf(monitor));
    }

    /// <summary>
    /// The monitor's scale factor. <c>GetDpiForMonitor</c> is Windows 8.1+; on anything older, and if the
    /// call fails, 1.0 is the honest answer rather than a guess from the primary display.
    /// </summary>
    private static double ScaleOf(nint monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : 1.0;
        }
        catch (DllNotFoundException)
        {
            return 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private const int MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, int flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
