using System.Text;
using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Input;

/// <summary>
/// Translates a raw virtual-key code from the low-level keyboard hook into the printable character it
/// actually produces — honoring the current keyboard layout (so this works with non-US layouts) and
/// modifier state (shift, caps lock, AltGr). A low-level hook only gives you vkCodes, not characters,
/// so this is the same technique keyloggers/IMEs use to reconstruct real text.
/// </summary>
public static class KeyTranslator
{
    /// <summary>Returns the character the given key press would type in the foreground window's layout, or null if it's not a printable character (e.g. arrow keys, function keys, modifiers alone).</summary>
    public static char? TryTranslateToChar(int virtualKeyCode, uint scanCode)
    {
        var keyboardState = new byte[256];
        if (!GetKeyboardState(keyboardState))
            return null;

        var foregroundWindow = GetForegroundWindow();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        var layout = GetKeyboardLayout(foregroundThreadId);

        var buffer = new StringBuilder(8);
        var result = ToUnicodeEx((uint)virtualKeyCode, scanCode, keyboardState, buffer, buffer.Capacity, 0, layout);

        // result > 0: buffer holds `result` translated characters.
        // result == 0: key has no translation in this layout (e.g. a bare modifier key).
        // result < 0: dead key (accent) — consumed by the layout, produces no visible character yet.
        if (result <= 0 || buffer.Length == 0)
            return null;

        var ch = buffer[0];
        return char.IsControl(ch) ? null : ch;
    }

    public static bool IsWordCharacter(char c) => char.IsLetter(c) || c == '\'' || c == '-';

    /// <summary>
    /// Space/Enter commit the word being typed (triggers autocorrect). Tab deliberately does NOT commit —
    /// it's reserved exclusively for cycling the suggestion bar (see the app-level bar input router), so
    /// treating it as a boundary here would race with that and commit/clear the buffer out from under it.
    /// </summary>
    public static bool IsWordBoundaryKey(int virtualKeyCode) =>
        virtualKeyCode is VK_SPACE or VK_RETURN;

    /// <summary>Keys that mean "the caret context may have moved" — our tracked buffer should be dropped rather than trusted. Includes Tab (see <see cref="IsWordBoundaryKey"/>).</summary>
    public static bool IsContextInvalidatingKey(int virtualKeyCode) => virtualKeyCode is
        VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN or
        VK_HOME or VK_END or VK_PRIOR or VK_NEXT or
        VK_DELETE or VK_ESCAPE or VK_TAB;
}
