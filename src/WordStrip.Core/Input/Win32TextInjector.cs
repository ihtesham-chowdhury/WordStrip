using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Input;

/// <summary>
/// Injects text via SendInput, editing as little as possible: the shared prefix between what the user typed
/// and the replacement is left in place, and only the differing tail is backspaced and retyped. Every event
/// carries <see cref="NativeMethods.OwnInjectionMarker"/> in dwExtraInfo so <see cref="LowLevelKeyboardHook"/>
/// can recognise our own output and not mistake it for the user typing.
///
/// <para><b>The backspaces and the replacement text go in one SendInput call.</b> This is the whole
/// correctness story of this class and it is not optional. Windows guarantees that the events inside a
/// single SendInput call are delivered to the target serially and are <em>not</em> interleaved with any
/// other input; it makes no such promise between two calls. Sending the deletions and then the text
/// separately lets the target begin processing the backspaces while the text is already arriving, and the
/// still-draining deletions eat the front of it.</para>
///
/// <para>That bug shipped, and it was invisible for exactly as long as the common case had nothing to
/// delete: completing "wor" to "world" shares a prefix, so there were no backspaces and only one call.
/// A personal-vocabulary entry like "Alexandra Fairbourne Reed" shares no prefix with the typed "iht" — the
/// capital I does not match — so three backspaces were sent, and the user got "exandra Fairbourne Reed".</para>
///
/// Callers must not invoke this from inside the keyboard hook callback while suppressing the triggering
/// key — input injected from there is discarded. Post it back to the message loop first.
/// </summary>
public sealed class Win32TextInjector : ITextInjector
{
    /// <summary>
    /// Longest run of characters sent in one <c>SendInput</c> call.
    ///
    /// <para>Length is the one variable that tracks the partial-insertion reports: an ordinary completion
    /// sends a handful of characters and has never misbehaved, while a personal-vocabulary address is fifty,
    /// which is a hundred key events in a single burst. Whatever swallows the tail — a low-level hook
    /// somewhere in the chain exceeding its timeout, or a target coalescing a burst it did not expect —
    /// giving it less to swallow at once is the mitigation that does not depend on knowing which.</para>
    ///
    /// <para>Twenty-four keeps every ordinary word and most phrases in a single call, so the common path is
    /// unchanged and still atomic.</para>
    /// </summary>
    public const int MaxCharactersPerBatch = 24;

    public void ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace)
    {
        var final = MatchCase(typedWord, replacement);
        var keep = CommonPrefixLength(typedWord, final);

        SendReplacement(
            backspaces: typedWord.Length - keep,
            text: final[keep..] + (appendTrailingSpace ? " " : string.Empty));
    }

    public void ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement)
    {
        var final = MatchCase(typedWord, replacement);
        var keep = CommonPrefixLength(typedWord, final);

        // +1 for the boundary character the user already typed, which we re-append after the correction.
        SendReplacement(
            backspaces: typedWord.Length - keep + 1,
            text: final[keep..] + boundaryChar);
    }

    /// <summary>
    /// Sends the deletions and the text, splitting only when the text is long enough to be worth splitting.
    ///
    /// <para>The deletions always travel with the first chunk. That ordering is what the single-batch rule
    /// above is protecting: separating them lets the target start deleting while the text is arriving and
    /// eat its front. Splitting <em>within</em> the text is a different and safer thing — every chunk is
    /// still ordered and atomic, and the user is not typing at this instant because the key that triggered
    /// the replacement was swallowed.</para>
    /// </summary>
    private static void SendReplacement(int backspaces, string text)
    {
        var stopwatch = InjectionLog.IsEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;
        var deletions = Math.Max(0, backspaces);

        // Deliver straight to the control when there is one. See PostToControl for why this is the primary
        // path and synthetic keystrokes the fallback.
        var focus = Automation.FocusedControlInspector.GetFocusedControlInfo();
        if (focus.IsStandardEditControl && !focus.IsPasswordField && focus.Handle != 0 &&
            PostToControl(focus.Handle, deletions, text))
        {
            stopwatch?.Stop();
            InjectionLog.Record(text, deletions, deletions + text.Length, (uint)(deletions + text.Length),
                stopwatch?.Elapsed.TotalMilliseconds ?? 0, chunks: 1, method: "WM_CHAR");
            return;
        }

        var totalEvents = (deletions + text.Length) * 2;
        uint inserted = 0;
        var chunks = 0;

        var offset = 0;
        while (offset < text.Length || chunks == 0)
        {
            var length = Math.Min(MaxCharactersPerBatch, text.Length - offset);
            if (length < 0) length = 0;

            var batch = BuildReplacement(chunks == 0 ? deletions : 0, text.Substring(offset, length));
            inserted += Send(batch);

            chunks++;
            offset += length;

            if (length == 0) break;
        }

        stopwatch?.Stop();
        InjectionLog.Record(text, deletions, totalEvents, inserted, stopwatch?.Elapsed.TotalMilliseconds ?? 0, chunks, "SendInput");
    }

    /// <summary>
    /// Posts the deletions and the text to the control as ordinary typed characters.
    ///
    /// <para><b>Why this replaced synthetic keystrokes as the primary path.</b> SendInput pretends to be a
    /// keyboard, so every character is dragged through every low-level keyboard hook on the machine — ours,
    /// and whatever else the user runs. Measured on a real machine that came to roughly two milliseconds per
    /// character: eighty milliseconds for a name, two hundred and thirty for an address. Windows accepted
    /// every event and the app sent exactly the right text, but Windows 11 Notepad mangled a burst that
    /// long, dropping characters and repeating others. A window message goes straight to the control, in
    /// order, in microseconds, and no hook ever sees it.</para>
    ///
    /// <para>Restricted to Edit and RichEdit because those are the only surfaces this app supports and the
    /// only ones certain to treat WM_CHAR exactly as real typing — including 0x08 as a backspace. Anything
    /// else falls back to SendInput, which is universal even if it is slower.</para>
    ///
    /// <para>Posted rather than sent, so a busy target can never block the app; a thread's message queue is
    /// ordered, so the characters still arrive in sequence.</para>
    /// </summary>
    private static bool PostToControl(nint control, int backspaces, string text)
    {
        // No backspaces at all: select the characters to be replaced and let the first typed one overwrite
        // them, which is what happens when a person selects a word and types over it.
        //
        // Backspace is the single thing the two control types disagree about, and both disagreements were
        // observed rather than guessed. A plain EDIT deletes on WM_CHAR 0x08; RichEdit ignores the character
        // and wants WM_KEYDOWN — and sending that key to an EDIT lost characters from the replacement
        // outright ("Alexandra Haq" arriving as "htshml a"). Selecting the range sidesteps the
        // disagreement instead of branching on which control happens to have focus.
        if (backspaces > 0 && !SelectPrecedingCharacters(control, backspaces)) return false;

        foreach (var c in text)
        {
            if (!PostMessage(control, WM_CHAR, c, 1)) return false;
        }

        return true;
    }

    /// <summary>
    /// Selects the given number of characters immediately before the caret, so the next typed character
    /// replaces them. False when the caret cannot be established, which sends the caller to SendInput.
    /// </summary>
    private static bool SelectPrecedingCharacters(nint control, int count)
    {
        // Asked for by value rather than through a pointer: EM_GETSEL can report the selection in its return
        // value, packed into two words. The richer messages take a struct pointer, which would be an address
        // in this process and meaningless in the one that owns the control.
        if (SendMessageTimeout(control, EM_GETSEL, 0, 0, SMTO_ABORTIFHUNG, SelectionTimeoutMs, out var packed) == 0)
        {
            InjectionLog.RecordSelection("EM_GETSEL failed", 0, 0, count);
            return false;
        }

        var value = (long)packed;
        if (value < 0) return false;   // beyond 64K of text; the packed form cannot express it

        var low = (int)(value & 0xFFFF);
        var high = (int)((value >> 16) & 0xFFFF);

        InjectionLog.RecordSelection("EM_GETSEL", low, high, count);

        // The caret is the larger of the two words, and which word carries it is not something to rely on.
        // The documented packing is start in the low word and end in the high one, but a real Edit control
        // with the caret at position three and nothing selected was observed returning plain 3 — low word
        // three, high word zero. Reading that as "start 3, end 0" made the code conclude there was already a
        // selection and skip selecting anything at all, which is precisely the bug this replaced.
        //
        // Taking the maximum is correct under either packing and for an empty selection, where both words
        // hold the same value anyway.
        var caret = Math.Max(low, high);
        if (caret <= 0) return false;

        var from = Math.Max(0, caret - count);
        return PostMessage(control, EM_SETSEL, from, caret);
    }

    /// <summary>Long enough for a healthy application, short enough that a wedged one costs a dropped suggestion rather than a freeze.</summary>
    private const uint SelectionTimeoutMs = 120;

    /// <summary>
    /// Builds the deletions and the replacement text as one contiguous event batch.
    /// </summary>
    /// <remarks>
    /// Exposed to tests so the ordering and composition of the batch can be asserted without a real
    /// keyboard. What goes wrong here does not throw — it produces subtly wrong text in another
    /// application, which no unit test can observe after the fact.
    /// </remarks>
    internal static INPUT[] BuildReplacement(int backspaces, string text)
    {
        var deletions = Math.Max(0, backspaces);
        var inputs = new INPUT[(deletions + text.Length) * 2];
        var at = 0;

        for (var i = 0; i < deletions; i++)
        {
            inputs[at++] = KeyInput(VK_BACK, keyUp: false);
            inputs[at++] = KeyInput(VK_BACK, keyUp: true);
        }

        foreach (var c in text)
        {
            inputs[at++] = UnicodeInput(c, keyUp: false);
            inputs[at++] = UnicodeInput(c, keyUp: true);
        }

        return inputs;
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


    /// <summary>
    /// Wraps SendInput so a rejected batch surfaces instead of silently doing nothing. SendInput returns the
    /// number of events actually inserted; anything short of the full batch means the OS refused it (a wrong
    /// cbSize or a blocked injection, e.g. UIPI against an elevated target window).
    /// </summary>
    private static uint Send(INPUT[] inputs)
    {
        if (inputs.Length == 0) return 0;

        var inserted = SendInput((uint)inputs.Length, inputs, InputSize);
        if (inserted != (uint)inputs.Length)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput inserted {inserted} of {inputs.Length} events (cbSize={InputSize}, Win32 error {error}).");
        }

        return inserted;
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
