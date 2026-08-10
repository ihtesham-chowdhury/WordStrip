namespace WordStrip.Core.Settings;

/// <summary>
/// Where WordStrip keeps everything it knows about the person using it: settings, their personal
/// vocabulary, and anything learned from their typing.
///
/// <para>One place decides this, because the answer needs to be the same for all three files and because a
/// user asking "what have you stored about me?" deserves a single folder to be pointed at rather than a
/// list.</para>
/// </summary>
public static class UserDataLocation
{
    /// <summary>
    /// Overrides the folder. Set it and every file moves together.
    ///
    /// <para>Exists for the end-to-end regression, which needs to run against a known vocabulary without
    /// touching the real one — a test that seeds words into the user's own store, or asserts against
    /// whatever happens to be in it, is a test that either destroys data or passes by accident. It doubles
    /// as a way to run a genuinely portable copy from a stick.</para>
    /// </summary>
    public const string OverrideEnvironmentVariable = "WORDSTRIP_DATA_DIR";

    /// <summary>The data folder. Created on demand by whichever store writes first.</summary>
    public static string Directory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WordStrip");
        }
    }

    public static string File(string fileName) => Path.Combine(Directory, fileName);
}
