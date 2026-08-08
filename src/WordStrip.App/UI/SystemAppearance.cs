using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace WordStrip.App.UI;

/// <summary>
/// Reads the Windows equivalents of the accessibility settings Apple's guidance says a glass interface must
/// respect: reduce transparency, reduce motion, and increase contrast. Translucency and fluid animation are
/// presentation, not function — when the user has asked the system to tone them down, the bar degrades to a
/// solid, high-contrast, still-usable surface rather than ignoring the preference.
/// </summary>
public static class SystemAppearance
{
    /// <summary>Windows Settings → Personalisation → Colours → "Transparency effects".</summary>
    public static bool TransparencyEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return true; // Preference unreadable — assume the richer default rather than degrading needlessly.
            }
        }
    }

    /// <summary>
    /// Windows Settings → Accessibility → Visual effects → "Animation effects". Exposed to apps as the
    /// SPI_GETCLIENTAREAANIMATION system parameter.
    /// </summary>
    public static bool AnimationsEnabled
    {
        get
        {
            try
            {
                var enabled = true;
                if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref enabled, 0))
                    return enabled;
            }
            catch (EntryPointNotFoundException)
            {
                // Very old Windows without the parameter; fall through to the default.
            }

            return true;
        }
    }

    /// <summary>High Contrast themes demand solid backgrounds and system-defined colours.</summary>
    public static bool HighContrast => SystemParameters.HighContrast;

    /// <summary>True when the bar should render as a translucent glass material rather than a solid panel.</summary>
    public static bool UseGlass => TransparencyEnabled && !HighContrast;

    /// <summary>True when the bar should animate rather than appear and move instantly.</summary>
    public static bool UseMotion => AnimationsEnabled;

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
}
