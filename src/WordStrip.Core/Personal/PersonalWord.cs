namespace WordStrip.Core.Personal;

/// <summary>
/// One entry in the personal vocabulary: the form to look it up by, the form to show, and how much the user
/// has used it.
/// </summary>
/// <param name="Key">
/// Lower-cased lookup form. Everything that matches or compares words uses this, so "QNAP", "qnap" and
/// "QNap" are one entry rather than three.
/// </param>
/// <param name="Display">
/// How the word should actually appear on the bar and be inserted. Kept separately because the whole point
/// of a personal vocabulary is words the general dictionary gets wrong, and half of those are wrong about
/// capitalisation rather than spelling — "GitHub", "QNAP", "iPhone" all lose their identity if lower-cased.
/// </param>
/// <param name="Frequency">
/// How often the user has used it. Starts at 1 on a manual add; Phase 4's learning is what makes this a real
/// signal rather than a tiebreak.
/// </param>
/// <param name="LastUsedUtc">
/// When it was last used, for recency. Stored as a date rather than a timestamp — a personal vocabulary does
/// not need to know the minute someone typed a word, and not recording it is the cheapest way to guarantee
/// the file can never become a timeline of someone's working day.
/// </param>
public readonly record struct PersonalWord(
    string Key,
    string Display,
    int Frequency = 1,
    DateOnly? LastUsedUtc = null);
