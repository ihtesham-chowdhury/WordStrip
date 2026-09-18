using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using WordStrip.Core.Suggestions;
using WordStrip.Core.Text;

namespace WordStrip.Core.Tests;

/// <summary>
/// A text field, as both sides see it. <see cref="Text"/> is the control's real contents, changed only by
/// typing and by the injector. The provider side answers from its own shadow, changed by typing and by the
/// notes the controller sends — exactly the split a real provider has. Tests assert on <see cref="Text"/>.
///
/// <para>The injector refuses to replace text that is not actually at the end of the field, exactly as the
/// real one does for an edit control it can read, and counts each refusal. The cycle depends on that
/// property — WordStrip replaces exactly what it inserted, never a guess — so tests can assert both that a
/// stale edit was refused and that an ordinary session never needed a refusal at all.</para>
/// </summary>
internal sealed class FakeDocument : ITextContextProvider, ITextInjector
{
    private string _shadow = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public bool IsEditable { get; set; } = true;
    public bool IsPasswordField { get; set; }
    public bool IsSingleLine { get; set; }

    public TextContextSource Source => TextContextSource.KeyboardHook;
    public bool IsAvailable => true;

    public List<(string Existing, string Replacement)> Replacements { get; } = new();

    public int Refusals { get; private set; }

    public event EventHandler<string>? CurrentWordChanged;
    public event EventHandler<WordCommittedEventArgs>? WordCommitted;
    public event EventHandler? ContextLost;

    public string CurrentWord => WordAtEnd(_shadow);

    public bool ShadowMatchesText => string.Equals(_shadow, Text, StringComparison.Ordinal);

    public TextContext GetContext()
    {
        var current = WordAtEnd(_shadow);
        var before = _shadow[..^current.Length];

        var sentenceStart = before.LastIndexOfAny(new[] { '.', '!', '?', '\n' }) + 1;
        var segment = before[sentenceStart..];
        var words = System.Text.RegularExpressions.Regex.Split(segment, @"[^\p{L}'\-]+")
            .Where(w => w.Length > 0)
            .TakeLast(2)
            .ToArray();

        return new TextContext(
            IsEditable, IsPasswordField, current, words,
            IsAtSentenceStart: segment.Trim().Length == 0,
            Caret: null, Source, HasSelection: false, IsSingleLine);
    }

    // --- The provider's notes -------------------------------------------------------------------------

    public void NoteTextInserted(string text)
    {
        var current = WordAtEnd(_shadow);
        _shadow = _shadow[..^current.Length] + text + " ";
    }

    public void NoteWordCorrected(string correctedWord)
    {
        var trimmed = _shadow.TrimEnd(' ', ',', '.', '!', '?', ';', ':', '\n');
        var word = WordAtEnd(trimmed);
        var trailing = _shadow[trimmed.Length..];
        _shadow = trimmed[..^word.Length] + correctedWord + trailing;
    }

    public void NoteTextReplaced(string existing, string replacement)
    {
        if (!_shadow.EndsWith(existing, StringComparison.Ordinal))
            throw new InvalidOperationException($"Provider told to replace '{existing}' but its text ends '{_shadow}'.");

        _shadow = _shadow[..^existing.Length] + replacement;
    }

    // --- The injector ---------------------------------------------------------------------------------

    public bool ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace)
    {
        if (!Text.EndsWith(typedWord, StringComparison.Ordinal))
        {
            Refusals++;
            return false;
        }

        Text = Text[..^typedWord.Length] + CaseMatching.Apply(typedWord, replacement) + (appendTrailingSpace ? " " : "");
        return true;
    }

    public bool ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement)
    {
        if (!Text.EndsWith(typedWord + boundaryChar, StringComparison.Ordinal))
        {
            Refusals++;
            return false;
        }

        Text = Text[..^(typedWord.Length + 1)] + CaseMatching.Apply(typedWord, replacement) + boundaryChar;
        return true;
    }

    public bool ReplaceText(string existing, string replacement)
    {
        if (!Text.EndsWith(existing, StringComparison.Ordinal))
        {
            Refusals++;
            return false;
        }

        Replacements.Add((existing, replacement));
        Text = Text[..^existing.Length] + replacement;
        return true;
    }

    public void InvalidateContext() => _shadow = string.Empty;

    /// <summary>The application changes its own text with no keystroke — what WM_SETTEXT does, invisibly to a hook.</summary>
    public void ChangeInvisibly(string text) => Text = text;

    // --- The user -------------------------------------------------------------------------------------

    /// <summary>One character reaching the field, as TypingSession would see it after the router let it through.</summary>
    public void Receive(char c)
    {
        var wordBefore = WordAtEnd(_shadow);
        var preceding = GetContext().PrecedingWords.ToArray();

        Text += c;
        _shadow += c;

        if (KeyTranslator.IsWordCharacter(c))
        {
            CurrentWordChanged?.Invoke(this, WordAtEnd(_shadow));
            return;
        }

        if (wordBefore.Length == 0) return;

        WordCommitted?.Invoke(this, new WordCommittedEventArgs
        {
            Word = wordBefore,
            BoundaryChar = c,
            PrecedingWords = preceding,
        });
        CurrentWordChanged?.Invoke(this, string.Empty);
    }

    /// <summary>A character that reaches the field without being tracked as typing — Tab, which resets the buffer.</summary>
    public void ReceiveRaw(char c)
    {
        Text += c;
        _shadow += c;
    }

    public void ReceiveBackspace()
    {
        if (Text.Length == 0) return;
        Text = Text[..^1];
        if (_shadow.Length > 0) _shadow = _shadow[..^1];
        CurrentWordChanged?.Invoke(this, WordAtEnd(_shadow));
    }

    /// <summary>The caret moved somewhere we cannot see. The text is unchanged; what is known about it is not.</summary>
    public void LoseContext() => ContextLost?.Invoke(this, EventArgs.Empty);

    public void Clear()
    {
        Text = string.Empty;
        _shadow = string.Empty;
    }

    public void Dispose() { }

    private static string WordAtEnd(string text)
    {
        var start = text.Length;
        while (start > 0 && KeyTranslator.IsWordCharacter(text[start - 1])) start--;
        return text[start..];
    }
}

/// <summary>A clock that moves only when told to, and the timers that go with it.</summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<(DateTimeOffset Due, Action Action)> _pending = new();

    public DateTimeOffset Now { get; private set; } = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Schedule(TimeSpan delay, Action action) => _pending.Add((Now + delay, action));

    public void Advance(TimeSpan by)
    {
        Now += by;

        var due = _pending.Where(p => p.Due <= Now).ToList();
        foreach (var item in due)
        {
            _pending.Remove(item);
            item.Action();
        }
    }
}

/// <summary>
/// The controller wired to a <see cref="FakeDocument"/>, plus the key routing the app performs: each key is
/// offered to the controller first, and only reaches the field if the controller did not consume it.
///
/// <para>Text edits are queued and run after each key is handled, not inline. That is how the app works —
/// the controller is called from inside the keyboard hook and every edit is posted to the message loop — and
/// running them inline would test a different ordering than the one users get.</para>
/// </summary>
internal sealed class InteractionHarness : IDisposable
{
    public AppSettings Settings { get; } = new()
    {
        PersistentBar = true,
        SuggestionCount = 4,
        AutocorrectEnabled = false,
        PersonalLearningEnabled = false,
        EmojiSuggestionsEnabled = true,
    };

    public FakeDocument Doc { get; } = new();
    public ManualClock Clock { get; } = new();
    public SuggestionController Controller { get; }
    public List<SuggestionUpdate> Published { get; } = new();

    private readonly Queue<Action> _messageLoop = new();

    /// <summary>Identity of the focused control. Change it to simulate focus moving elsewhere.</summary>
    public nint Focus { get; set; } = 1;

    /// <summary>When set, the keystroke-built word disagrees with the provider's — a text service lagging.</summary>
    public string? KeySynchronousOverride { get; set; }

    public InteractionHarness(PredictionEngine? engine = null, PersonalLanguageModel? learning = null)
    {
        Controller = new SuggestionController(
            Doc, engine ?? InteractionTestEngine.Build(), Doc, Settings,
            postToMessageLoop: _messageLoop.Enqueue,
            personalLearning: learning,
            timeProvider: Clock,
            focusIdentity: () => Focus,
            keySynchronousWord: () => KeySynchronousOverride ?? Doc.CurrentWord,
            scheduleAfter: Clock.Schedule);

        Controller.SuggestionsChanged += (_, update) => Published.Add(update);
    }

    public SuggestionUpdate Last => Published.Count > 0 ? Published[^1] : SuggestionUpdate.Empty;

    public IReadOnlyList<string> LastWords => Last.Suggestions.Select(s => s.Word).ToList();

    public string Text => Doc.Text;

    // --- Keys -----------------------------------------------------------------------------------------

    public void Type(string keys)
    {
        foreach (var c in keys) Key(c);
    }

    public void Key(char c)
    {
        if (CompletionPolicy.IsCommitBoundary(c))
        {
            if (Controller.HandleBoundary(c))
            {
                Pump();
                return;
            }
        }
        else
        {
            Controller.HandleOtherKey(isLineBreak: false);
        }

        Doc.Receive(c);
        Pump();
    }

    /// <summary>Returns whether WordStrip took the Tab. If not, it reaches the field.</summary>
    public bool Tab(bool shift = false)
    {
        if (Controller.HandleTab(forward: !shift))
        {
            Pump();
            return true;
        }

        Doc.ReceiveRaw('\t');
        Doc.LoseContext();
        Pump();
        return false;
    }

    public void Enter()
    {
        Controller.HandleOtherKey(isLineBreak: true);
        Doc.Receive('\n');
        Pump();
    }

    public void Backspace()
    {
        if (!Controller.HandleBackspace()) Doc.ReceiveBackspace();
        Pump();
    }

    public bool Escape()
    {
        var consumed = Controller.HandleEscape();
        Pump();
        return consumed;
    }

    /// <summary>A click in the field: the mouse hook dismisses first, then the typing buffer resets.</summary>
    public void Click()
    {
        Controller.Dismiss();
        Doc.LoseContext();
        Pump();
    }

    /// <summary>A click on one of the bar's chips.</summary>
    public void Choose(Suggestion suggestion)
    {
        Controller.AcceptSuggestion(suggestion);
        Pump();
    }

    /// <summary>An arrow key: an ordinary key as far as the router is concerned, and a lost context.</summary>
    public void Arrow()
    {
        Controller.HandleOtherKey(isLineBreak: false);
        Doc.LoseContext();
        Pump();
    }

    public void Wait(int milliseconds)
    {
        Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Pump();
    }

    /// <summary>Runs whatever the controller posted, as the message loop would once the hook returned.</summary>
    public void Pump()
    {
        while (_messageLoop.Count > 0) _messageLoop.Dequeue()();
    }

    public void Dispose() => Controller.Dispose();
}
