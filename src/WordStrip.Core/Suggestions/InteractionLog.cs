namespace WordStrip.Core.Suggestions;

/// <summary>
/// An opt-in trace of what the interaction model decided and why, for diagnosing behaviour that only shows up
/// in real applications. Off unless <c>WORDSTRIP_INTERACTIONLOG=1</c>; written to
/// <c>%TEMP%\wordstrip_interaction.log</c>.
///
/// <para>Deliberately records decisions, not text: the word in progress appears, as it must for the trace to
/// mean anything, but nothing before it does. The same privacy line the rest of the app holds.</para>
/// </summary>
public static class InteractionLog
{
    private static readonly object Gate = new();

    public static bool IsEnabled { get; } =
        string.Equals(Environment.GetEnvironmentVariable("WORDSTRIP_INTERACTIONLOG"), "1", StringComparison.Ordinal);

    public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "wordstrip_interaction.log");

    public static void Write(string message)
    {
        if (!IsEnabled) return;

        try
        {
            lock (Gate) File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
