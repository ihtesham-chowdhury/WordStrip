using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Input;

/// <summary>
/// Injects text via SendInput, editing as little as possible: the shared prefix between what the user typed
/// and the replacement is left in place, and only the differing tail is backspaced and retyped. Every event
/// carries <see cref="NativeMethods.OwnInjectionMarker"/> in dwExtraInfo so <see cref="LowLevelKeyboardHook"/>
/// can recognise our own output and not mistake it for the user typing.
///
/// Callers must not invoke this from inside the keyboard hook callback while suppressing the triggering
/// key — input injected from there is discarded. Post it back to the message loop first.
/// </summary>
public sealed class Win32TextInjector : ITextInjector
{
    public void ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace)
    {
        var final = MatchCase(typedWord, replacement);
        var keep = CommonPrefixLength(typedWord, final);

        SendBackspaces(typedWord.Length - keep);
        SendText(final[keep..] + (appendTrailingSpace ? " " : string.Empty));
    }

    public void ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement)
    {
        var final = MatchCase(typedWord, replacement);
        var keep = CommonPrefixLength(typedWord, final);

        // +1 for the boundary character the user already typed, which we re-append after the correction.
        SendBackspaces(typedWord.Length - keep + 1);
        SendText(final[keep..] + boundaryChar);
    }

    /// <summary>
    /// How many leading characters the typed text and the replacement already share.
    /// Everything up to that point is left untouched instead of being deleted and retyped — for the common
    /// case of completing a word ("wor" → "world") that means zero backspaces. Beyond being faster and
    /// flicker-free, not deleting text avoids disturbing the character formatting of the surrounding run,
    /// which some rich-text controls re-derive when a run is removed and reinserted.
    /// </summary>
    private static int CommonPrefixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && a[i] == b[i]) i++;
        return i;
    }

    /// <summary>
    /// Re-applies the capitalisation the user actually typed to the dictionary's (lower-case) word, so
    /// accepting a suggestion after typing "Hel" yields "Help" rather than silently downcasing to "help".
    /// </summary>
    private static string MatchCase(string typedWord, string replacement)
    {
        if (typedWord.Length == 0 || replacement.Length == 0) return replacement;

        var hasLetters = typedWord.Any(char.IsLetter);
        if (hasLetters && typedWord.Where(char.IsLetter).All(char.IsUpper) && typedWord.Count(char.IsLetter) > 1)
            return replacement.ToUpperInvariant();

        if (char.IsUpper(typedWord[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    private static void SendBackspaces(int count)
    {
        if (count <= 0) return;

        var inputs = new INPUT[count * 2];
        for (var i = 0; i < count; i++)
        {
            inputs[i * 2] = KeyInput(VK_BACK, keyUp: false);
            inputs[i * 2 + 1] = KeyInput(VK_BACK, keyUp: true);
        }

        Send(inputs);
    }

    private static void SendText(string text)
    {
        if (text.Length == 0) return;

        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = UnicodeInput(text[i], keyUp: false);
            inputs[i * 2 + 1] = UnicodeInput(text[i], keyUp: true);
        }

        Send(inputs);
    }

    /// <summary>
    /// Wraps SendInput so a rejected batch surfaces instead of silently doing nothing. SendInput returns the
    /// number of events actually inserted; anything short of the full batch means the OS refused it (a wrong
    /// cbSize or a blocked injection, e.g. UIPI against an elevated target window).
    /// </summary>
    private static void Send(INPUT[] inputs)
    {
        var inserted = SendInput((uint)inputs.Length, inputs, InputSize);
        if (inserted != (uint)inputs.Length)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput inserted {inserted} of {inputs.Length} events (cbSize={InputSize}, Win32 error {error}).");
        }
    }

    private static readonly int InputSize = System.Runtime.InteropServices.Marshal.SizeOf<INPUT>();

    private static INPUT KeyInput(int vk, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = OwnInjectionMarker,
            },
        },
    };

    private static INPUT UnicodeInput(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = OwnInjectionMarker,
            },
        },
    };
}
