using WordStrip.Core.Prediction;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// Chooses the order the bar shows a fresh candidate list in, given what it showed a keystroke ago.
///
/// <para><b>Why.</b> Adjacent keystrokes routinely produce near-identical scores in a different order —
/// "going" and "gone" trading places on a difference of a few hundredths. The model is right to be unsure,
/// but showing that as chips swapping places makes the reader chase moving targets. This changes nothing
/// about the scores, only the order they are displayed in, and it yields the moment the model becomes
/// decisively more confident.</para>
///
/// <para><b>Rules.</b> (1) The first slot keeps its word unless something else now outscores it by more than
/// the hysteresis. (2) Every other word that is still a candidate stays in the slot it was in. (3) Slots left
/// empty are filled in rank order. (4) Among the alternatives, a word may still move ahead of the one before
/// it, but only on a lead greater than the hysteresis. Emoji stay where the engine put them — last — because
/// their position is a policy, not a score.</para>
///
/// <para>Only meaningful between two lists describing the same thing: consecutive states of one word being
/// typed. The caller decides that; given unrelated lists this would preserve an order that means nothing.</para>
/// </summary>
public static class CandidateStabilizer
{
    public static IReadOnlyList<Suggestion> Stabilize(
        IReadOnlyList<Suggestion> previous,
        IReadOnlyList<Suggestion> next,
        double hysteresis)
    {
        if (hysteresis <= 0 || previous.Count == 0 || next.Count < 2) return next;

        var words = next.Where(s => !s.IsEmoji).ToList();
        var emoji = next.Where(s => s.IsEmoji).ToList();
        if (words.Count < 2) return next;

        var byWord = new Dictionary<string, Suggestion>(StringComparer.Ordinal);
        foreach (var w in words) byWord.TryAdd(w.Word, w);

        var best = words.MaxBy(s => s.Score);
        var slots = new Suggestion?[words.Count];
        var placed = new HashSet<string>(StringComparer.Ordinal);

        // (1) The first slot.
        var top = best;
        if (!previous[0].IsEmoji && byWord.TryGetValue(previous[0].Word, out var incumbent)
            && best.Score - incumbent.Score <= hysteresis)
        {
            top = incumbent;
        }

        slots[0] = top;
        placed.Add(top.Word);

        // (2) Everything else that is still a candidate keeps its slot.
        for (var i = 1; i < slots.Length && i < previous.Count; i++)
        {
            if (previous[i].IsEmoji) continue;
            if (!byWord.TryGetValue(previous[i].Word, out var kept) || placed.Contains(kept.Word)) continue;

            slots[i] = kept;
            placed.Add(kept.Word);
        }

        // (3) Holes, in rank order. `words` arrives ranked by the engine.
        var queue = new Queue<Suggestion>(words.Where(w => !placed.Contains(w.Word)));
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is null && queue.Count > 0) slots[i] = queue.Dequeue();
        }

        var ordered = slots.Where(s => s is not null).Select(s => s!.Value).ToList();

        // (4) A decisive lead may still reorder the alternatives. Bubble passes are fine at this size.
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 1; i < ordered.Count - 1; i++)
            {
                if (ordered[i + 1].Score - ordered[i].Score <= hysteresis) continue;
                (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
                changed = true;
            }
        }

        ordered.AddRange(emoji);
        return ordered;
    }
}
