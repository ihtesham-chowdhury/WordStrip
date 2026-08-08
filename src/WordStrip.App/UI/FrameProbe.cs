using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace WordStrip.App.UI;

/// <summary>
/// Temporary diagnostic: samples the interval between composition frames during an animation so smoothness
/// can be measured rather than eyeballed. Dormant unless the WORDSTRIP_FRAMELOG environment variable is set
/// to 1, so it costs nothing in a normal run.
/// </summary>
internal static class FrameProbe
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("WORDSTRIP_FRAMELOG") == "1";

    private static readonly List<double> Intervals = new(256);
    private static readonly Stopwatch Clock = new();
    private static string _label = string.Empty;
    private static double _lastMs;
    private static bool _recording;

    public static void Record(string label, TimeSpan window)
    {
        if (!Enabled || _recording) return;

        _recording = true;
        _label = label;
        Intervals.Clear();
        Clock.Restart();
        _lastMs = 0;

        CompositionTarget.Rendering += OnRendering;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = window };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CompositionTarget.Rendering -= OnRendering;
            _recording = false;
            Dump();
        };
        timer.Start();
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = Clock.Elapsed.TotalMilliseconds;
        if (_lastMs > 0) Intervals.Add(now - _lastMs);
        _lastMs = now;
    }

    private static void Dump()
    {
        if (Intervals.Count == 0) return;

        var sorted = Intervals.OrderBy(v => v).ToList();
        double Percentile(double p) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];

        // A frame budget of 16.7ms is 60fps; anything over ~33ms is a visibly dropped frame.
        var dropped = Intervals.Count(v => v > 33);

        var line = string.Format(
            "{0:HH:mm:ss} {1,-12} frames={2,3} median={3,5:0.0}ms p95={4,5:0.0}ms max={5,6:0.0}ms dropped(>33ms)={6}",
            DateTime.Now, _label, Intervals.Count, Percentile(0.5), Percentile(0.95), sorted[^1], dropped);

        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "wordstrip_frames.log"), line + Environment.NewLine);
        }
        catch (IOException) { }
    }
}
