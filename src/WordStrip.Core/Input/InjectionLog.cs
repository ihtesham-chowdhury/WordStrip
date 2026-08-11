using System.Text;
using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Input;

/// <summary>
/// Opt-in record of what the text injector actually did, written to
/// <c>%TEMP%\wordstrip_injection.log</c> when <c>WORDSTRIP_INJECTLOG=1</c> is set.
///
/// <para>Exists because a partial-insertion bug has been reported twice from real use and reproduced zero
/// times here — not against a plain <c>EDIT</c>, not against a <c>RICHEDIT50W</c>, not at any typing speed,
/// not at any length. When a failure only happens on someone else's machine in an application that cannot
/// safely be driven by a test, the honest next step is to record what happened there rather than keep
/// guessing from symptoms.</para>
///
/// <para>Records the intended text, the batch size, what <c>SendInput</c> reported inserting, how long the
/// call took, and which window class received it — the class matters because the reports come from Windows
/// 11 Notepad, whose editor behaves differently from every target available to test against.</para>
///
/// <para>Off unless the environment variable is set, and it never records what the user typed: only the
/// replacement text the app chose, which the app already knows and which came from the user's own
/// vocabulary or the bundled dictionary.</para>
/// </summary>
public static class InjectionLog
{
    private static readonly object Gate = new();
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("WORDSTRIP_INJECTLOG"), "1", StringComparison.Ordinal);

    private static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "wordstrip_injection.log");

    public static bool IsEnabled => Enabled;

    public static string FilePath => Path;

    /// <summary>Records one injection attempt. Cheap no-op when logging is off.</summary>
    public static void Record(string text, int backspaces, int events, uint inserted, double elapsedMs, int chunks)
    {
        if (!Enabled) return;

        try
        {
            var focusClass = DescribeFocus();

            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append("  chunks=").Append(chunks)
                .Append(" backspaces=").Append(backspaces)
                .Append(" chars=").Append(text.Length)
                .Append(" events=").Append(events)
                .Append(" inserted=").Append(inserted)
                .Append(inserted == events ? "" : "  *** SHORT ***")
                .Append(" ms=").Append(elapsedMs.ToString("0.0"))
                .Append(" focus=").Append(focusClass)
                .Append("  text=[").Append(text).Append(']')
                .ToString();

            lock (Gate) File.AppendAllText(Path, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never be able to break the thing they are diagnosing.
        }
    }

    /// <summary>Records what the target actually ended up containing, when that can be read back.</summary>
    public static void RecordResult(string readBack)
    {
        if (!Enabled) return;

        try
        {
            lock (Gate) File.AppendAllText(Path, $"           result=[{readBack}]{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string DescribeFocus()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == 0) return "(none)";

            var threadId = GetWindowThreadProcessId(foreground, out _);
            var info = new GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == 0) return "(no focus)";

            var buffer = new StringBuilder(256);
            var length = GetClassName(info.hwndFocus, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString(0, length) : "(unnamed)";
        }
        catch
        {
            return "(error)";
        }
    }
}
