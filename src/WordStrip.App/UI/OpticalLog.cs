using System.IO;
using WordStrip.Core.Presentation;

namespace WordStrip.App.UI;

/// <summary>
/// Records what automatic sizing decided and why. Off unless <c>WORDSTRIP_OPTICALLOG=1</c> is set, and
/// deliberately not exposed in Settings: this is for tuning the multipliers against real applications, which
/// is the only way the numbers in <see cref="OpticalSizing"/> can be judged.
///
/// <para>One line per accepted change, never per keystroke — the sizer refuses almost every measurement, and
/// a log that recorded refusals would be both enormous and useless.</para>
/// </summary>
internal static class OpticalLog
{
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wordstrip_optical.log");

    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable("WORDSTRIP_OPTICALLOG") == "1";

    public static void Write(OpticalSizer sizer, GlassMetrics metrics, HostTextMetrics measured)
    {
        if (!IsEnabled) return;

        try
        {
            var density = sizer.Current;

            File.AppendAllText(Path,
                $"{DateTime.Now:HH:mm:ss.fff}  " +
                $"caret={measured.CaretHeight:F1}  line={measured.LineHeight:F1}  dpi={measured.DpiScale:F2}  " +
                $"-> bar={density.BarHeight:F0}  font={density.FontSize:F1}  padY={density.PaddingY:F1}  " +
                $"padX={density.PaddingX:F1}  gap={density.CandidateGap:F1}  radius={metrics.PlateRadius:F0}  " +
                $"shadow={density.ShadowScale:F2}  slot={metrics.MinSlotWidth:F0}  {density.NearestDensity}" +
                Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never be the reason something fails.
        }
    }
}
