using WordStrip.Core.Input;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Suggestions;

/// <summary>The thresholds <see cref="CompletionPolicy"/> applies. Kept as a value so tests can state them outright.</summary>
public readonly record struct CompletionThresholds(int MinPrefixLength, double MinConfidence, double MinScoreMargin);

/// <summary>
/// Decides whether a boundary key — Space, or closing punctuation — should finish the partly typed word with
/// its strongest completion instead of simply being typed.
///
/// <para><b>The whole feature is only as good as its refusals.</b> Space is pressed thousands of times a day,
/// and every one that rewrites a word the user did not mean to change is remembered far longer than the
/// hundred it saved. So this says yes only when every one of these holds, and a plain space is always the
/// fallback:</para>
/// <list type="number">
/// <item>There is a word in progress, at least <see cref="CompletionThresholds.MinPrefixLength"/> letters long.</item>
/// <item>What has been typed is not already a word. "the", "work" and "can" are finished words; completing
/// them to "there", "world" and "candle" would be rewriting, not completing.</item>
/// <item>The candidate in the first slot genuinely completes it — the typed letters are its start, case
/// aside. A fuzzy repair or an emoji is never committed by Space.</item>
/// <item>It holds at least <see cref="CompletionThresholds.MinConfidence"/> of the candidates' combined
/// likelihood, and leads the runner-up by at least <see cref="CompletionThresholds.MinScoreMargin"/>.</item>
/// </list>
///
/// <para>Scores are the ranker's, which are log-scaled (about log₁₀ of frequency, plus context), so
/// likelihood is recovered as 10^score before being normalised. Because the first slot is what the bar
/// <em>shows</em> — possibly held in place by ranking hysteresis — a candidate that is on screen but no
/// longer the model's favourite fails the margin test and Space stays a space. Display stability can
/// therefore never cause a completion the model did not actually back.</para>
/// </summary>
public static class CompletionPolicy
{
    /// <summary>
    /// Keys that end a word and may carry a completion with them. Enter is deliberately absent: it submits
    /// forms and sends messages, and rewriting text at the instant it is sent is the one place a surprise
    /// cannot be taken back.
    /// </summary>
    public static bool IsCommitBoundary(char c) => c is ' ' or '.' or ',' or '!' or '?' or ':' or ';' or ')' or ']' or '}';

    /// <summary>The completion to commit, or null when the boundary should be typed as itself.</summary>
    public static Suggestion? SelectForBoundary(
        string typed,
        IReadOnlyList<Suggestion> displayed,
        Func<string, bool> isKnownWord,
        CompletionThresholds thresholds)
    {
        if (string.IsNullOrEmpty(typed) || typed.Length < thresholds.MinPrefixLength) return null;
        if (!typed.All(KeyTranslator.IsWordCharacter)) return null;
        if (displayed.Count == 0) return null;
        if (isKnownWord(typed)) return null;

        var top = displayed[0];
        if (!IsTrueCompletion(typed, top)) return null;

        var confidence = Confidence(displayed, out var margin);
        if (confidence < thresholds.MinConfidence) return null;
        if (margin < thresholds.MinScoreMargin) return null;

        return top;
    }

    /// <summary>Whether <paramref name="candidate"/> extends <paramref name="typed"/> rather than repairing or replacing it.</summary>
    public static bool IsTrueCompletion(string typed, Suggestion candidate)
    {
        if (candidate.IsEmoji) return false;
        if (candidate.Source is not (SuggestionSource.PrefixCompletion or SuggestionSource.Phrase)) return false;

        return candidate.Word.Length > typed.Length
            && candidate.Word.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The first candidate's share of the combined likelihood of every word candidate on screen, and its lead
    /// over the best of the rest. Emoji are excluded — they carry no score to compare. A lone candidate is
    /// fully confident with an unbounded lead.
    /// </summary>
    public static double Confidence(IReadOnlyList<Suggestion> displayed, out double margin)
    {
        margin = double.PositiveInfinity;
        if (displayed.Count == 0) return 0;

        var top = displayed[0].Score;
        var denominator = 1.0;

        for (var i = 1; i < displayed.Count; i++)
        {
            var rival = displayed[i];
            if (rival.IsEmoji) continue;

            margin = Math.Min(margin, top - rival.Score);

            // Clamped so a wildly stronger rival reads as zero confidence rather than overflowing.
            denominator += Math.Pow(10, Math.Min(rival.Score - top, 12));
        }

        return 1.0 / denominator;
    }
}
