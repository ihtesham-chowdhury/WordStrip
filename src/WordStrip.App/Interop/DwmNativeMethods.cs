using System.Runtime.InteropServices;

namespace WordStrip.App.Interop;

/// <summary>P/Invoke declarations for the DWM composition and window-style APIs the glass bar window needs.</summary>
internal static class DwmNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // Win11 22H2+. Values: 0=Auto, 1=None, 2=Mica, 3=Acrylic-style ("transient"), 4=Mica Alt ("tabbed").
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica — subtle, wallpaper-derived
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic — full live blur of what's behind

    // Win11 21H2+. Values: 0=Default, 1=DoNotRound, 2=Round, 3=RoundSmall.
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    // Win10 20H1+/Win11. Forces the dark-mode compositing variant of the backdrop so the bar's white text
    // stays legible regardless of whether the user's system theme is light or dark.
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const nint MA_NOACTIVATE = 3;
}
