using WordStrip.Core.Input;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Text.Tsf;

/// <summary>
/// Context read from the document by the TSF text service, rather than inferred from keystrokes.
///
/// <para>The difference is the whole point of the phase. <see cref="KeyboardHookTextContextProvider"/>
/// maintains a shadow of what it believes is near the caret and gives up whenever anything might have moved
/// it — a click, an arrow key, a paste. This one is told what is actually there, by the application, which
/// means it is correct after a click, correct in text the user typed before WordStrip started, and
/// structurally incapable of the dropped-keystroke drift recorded as section 12 item 8.</para>
///
/// <para><b>It does not raise <see cref="WordCommitted"/>, and that is deliberate for this stage.</b> That
/// event drives autocorrect and personal learning, both of which end in rewriting text in the focused
/// application. Committing through TSF is Stage 3 and has not been built or tested; raising the event now
/// would ask the injector to rewrite text in Chrome and Word on the strength of a mechanism nobody has
/// verified. Suggestions appear and can be read; autocorrect and learning stay with the hook path until the
/// commit path is proven. This is a real limitation of Stage 2, not an oversight.</para>
/// </summary>
public sealed class TsfTextContextProvider : ITextContextProvider
{
    private readonly Action<Action> _post;
    private readonly object _gate = new();

    private TextContext _context = TextContext.None;
    private string _lastWord = string.Empty;
    private bool _connected;

    /// <summary>
    /// The raw text the service last reported, kept so an accepted suggestion can be applied to it
    /// optimistically. See <see cref="NoteTextInserted"/> for why that is worth the extra field.
    /// </summary>
    private string _textBeforeCaret = string.Empty;

    /// <param name="post">
    /// Marshals events onto the thread the UI expects. Messages arrive on a background pipe thread, while
    /// everything downstream — the controller, the bar — has always been driven from the UI thread by a
    /// keyboard hook callback. Defaults to running inline, which suits tests and nothing else. Mirrors
    /// <c>SuggestionController</c>'s <c>postToMessageLoop</c>.
    /// </param>
    public TsfTextContextProvider(Action<Action>? post = null)
    {
        _post = post ?? (action => action());
    }

    public TextContextSource Source => TextContextSource.TextServices;

    /// <summary>
    /// True only while a text service is connected <b>and</b> the last thing it reported was an editable
    /// context.
    ///
    /// <para>The second half matters more than it looks. Reporting "available" whenever a service happens to
    /// be connected would mean that focusing a classic Win32 dialog — where the service is not loaded, so its
    /// last word on the subject is "not editable" — would still win the composite's selection, and the
    /// keyboard hook that could have served that dialog would never get a turn.</para>
    /// </summary>
    public bool IsAvailable
    {
        get { lock (_gate) return _connected && _context.IsEditable; }
    }

    public event EventHandler<string>? CurrentWordChanged;
    public event EventHandler? ContextLost;

    // Required by ITextContextProvider and deliberately never raised in this stage - see the class remarks.
    // The warning is suppressed rather than worked around because the honest state of affairs is exactly
    // what it describes: the member exists to satisfy the interface, and firing it before the commit path
    // is built would start autocorrect rewriting text in applications nobody has verified we can write to.
#pragma warning disable CS0067
    public event EventHandler<WordCommittedEventArgs>? WordCommitted;
#pragma warning restore CS0067

    public TextContext GetContext()
    {
        lock (_gate) return _context;
    }

    /// <summary>
    /// Applies an accepted suggestion to the cached context immediately, without waiting for the document to
    /// report back.
    ///
    /// <para><b>This was a no-op, on the reasoning that the document is the source of truth and the service
    /// re-reports it the moment it changes. That reasoning is correct and the behaviour was still wrong.</b>
    /// The controller publishes the between-words list synchronously the instant a suggestion is accepted,
    /// and the document's own update has to travel through a deferred injection, the host application, a TSF
    /// edit notification, a pipe and a dispatcher before it lands. In that window the bar was being filled
    /// from a context one word out of date — so accepting a word showed predictions for the word before it,
    /// which then corrected themselves a moment later. It reads as the bar stalling.</para>
    ///
    /// <para>So: predict what the document is about to say, and let the next real snapshot confirm or
    /// correct it. The document remains the source of truth; this only stops the bar guessing from stale
    /// text while the truth is in flight.</para>
    /// </summary>
    public void NoteTextInserted(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lock (_gate)
        {
            if (!_context.IsEditable) return;

            // The controller replaces the word in progress and appends a space, so mirror exactly that.
            var kept = _textBeforeCaret.Length >= _context.CurrentWord.Length
                ? _textBeforeCaret[..^_context.CurrentWord.Length]
                : string.Empty;

            ApplyTextLocked(kept + text + " ");
        }
    }

    /// <summary>
    /// Rewrites the last finished word after autocorrect changed it, for the same reason as
    /// <see cref="NoteTextInserted"/>.
    ///
    /// <para>Unreachable today — autocorrect fires from <c>WordCommitted</c>, which this provider never
    /// raises. Implemented anyway so that turning that event on in Stage 3 does not silently leave the
    /// context describing the typo instead of the correction.</para>
    /// </summary>
    public void NoteWordCorrected(string correctedWord)
    {
        if (string.IsNullOrWhiteSpace(correctedWord)) return;

        lock (_gate)
        {
            if (!_context.IsEditable) return;

            var trimmed = _textBeforeCaret.TrimEnd();
            var start = trimmed.Length;
            while (start > 0 && KeyTranslator.IsWordCharacter(trimmed[start - 1])) start--;
            if (start == trimmed.Length) return;  // nothing word-shaped to replace

            var trailing = _textBeforeCaret[trimmed.Length..];
            ApplyTextLocked(trimmed[..start] + correctedWord + trailing);
        }
    }

    /// <summary>Re-parses cached text into the context. Caller holds <see cref="_gate"/>.</summary>
    private void ApplyTextLocked(string textBeforeCaret)
    {
        if (textBeforeCaret.Length > TsfContextMessage.MaxTextChars)
            textBeforeCaret = textBeforeCaret[^TsfContextMessage.MaxTextChars..];

        _textBeforeCaret = textBeforeCaret;

        var (preceding, currentWord, atSentenceStart) = Parse(textBeforeCaret);

        _context = _context with
        {
            CurrentWord = currentWord,
            PrecedingWords = preceding,
            IsAtSentenceStart = atSentenceStart,
            HasSelection = false,
        };

        _lastWord = currentWord;
    }

    /// <summary>Called when a text service connects or drops. A disconnect must take availability with it.</summary>
    public void SetConnected(bool connected)
    {
        bool lost;

        lock (_gate)
        {
            if (_connected == connected) return;
            _connected = connected;

            lost = !connected && _context.IsEditable;
            if (!connected)
            {
                _context = TextContext.None;
                _lastWord = string.Empty;
                _textBeforeCaret = string.Empty;
            }
        }

        if (lost) _post(() => ContextLost?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Applies one snapshot from the text service and raises whatever it implies.
    ///
    /// <para>Events are derived by comparing snapshots rather than sent over the wire. The service reports
    /// what is there; deciding what that means — a new word, a lost context — is a judgement that belongs on
    /// this side, next to the tokenizer that defines what a word is in the first place.</para>
    /// </summary>
    public void Apply(TsfContextMessage message)
    {
        var (precedingWords, currentWord, atSentenceStart) = Parse(message.TextBeforeCaret);

        bool wordChanged;
        bool lost;

        lock (_gate)
        {
            var wasEditable = _context.IsEditable;

            // Kept so an accepted suggestion can be applied to it before the document reports back.
            _textBeforeCaret = message.TextBeforeCaret ?? string.Empty;

            _context = new TextContext(
                IsEditable: message.IsEditable,
                IsPasswordField: message.IsPasswordField,
                CurrentWord: currentWord,
                PrecedingWords: precedingWords,
                IsAtSentenceStart: atSentenceStart,
                Caret: message.Caret,
                Source: TextContextSource.TextServices,
                HasSelection: message.HasSelection);

            // Focus left a text surface. The bar has to come down, and the idle list must not be published
            // for a document that is no longer in front of the user.
            lost = wasEditable && !message.IsEditable;

            wordChanged = !lost
                && message.IsEditable
                && !string.Equals(currentWord, _lastWord, StringComparison.Ordinal);

            _lastWord = message.IsEditable ? currentWord : string.Empty;
        }

        if (lost)
        {
            _post(() => ContextLost?.Invoke(this, EventArgs.Empty));
            return;
        }

        if (wordChanged) _post(() => CurrentWordChanged?.Invoke(this, currentWord));
    }

    /// <summary>
    /// Splits the text before the caret into the word in progress and the finished words behind it.
    ///
    /// <para>The word in progress is returned raw rather than normalised, because it is matched as a prefix
    /// against the dictionary and the user's capitalisation is a signal the ranker uses. The preceding words
    /// go through <see cref="NGramTokenizer"/>, which is the same code the offline model builder used — the
    /// one rule this whole path must not break.</para>
    /// </summary>
    internal static (IReadOnlyList<string> Preceding, string CurrentWord, bool AtSentenceStart) Parse(string? textBeforeCaret)
    {
        var text = textBeforeCaret ?? string.Empty;
        if (text.Length == 0) return (Array.Empty<string>(), string.Empty, true);

        // Walk back over the word being typed. KeyTranslator's definition, not the tokenizer's, so that the
        // two input paths agree on where a word starts - a hyphen counts here and does not in the model.
        var start = text.Length;
        while (start > 0 && KeyTranslator.IsWordCharacter(text[start - 1])) start--;

        var currentWord = text[start..];
        var committed = text[..start];

        // Two words is what a trigram model consumes; more would be carrying the user's text around for no
        // purpose. Taken from the end because it is the nearest context that predicts.
        var preceding = new List<string>(2);
        foreach (var token in NGramTokenizer.Tokenize(committed))
        {
            if (token == NGramFormat.SentenceStart) continue;

            preceding.Add(token);
            if (preceding.Count > 2) preceding.RemoveAt(0);
        }

        return (preceding, currentWord, IsAtSentenceStart(committed));
    }

    /// <summary>
    /// Whether the caret sits at the start of a sentence: nothing behind it, or nothing but whitespace since
    /// a full stop. Drives capitalisation and lets the model answer with sentence openers rather than
    /// conditioning on the previous sentence's tail.
    /// </summary>
    private static bool IsAtSentenceStart(string committed)
    {
        for (var i = committed.Length - 1; i >= 0; i--)
        {
            var c = committed[i];
            if (char.IsWhiteSpace(c)) continue;

            return NGramTokenizer.IsSentenceTerminator(c);
        }

        return true;
    }

    public void Dispose()
    {
        CurrentWordChanged = null;
        ContextLost = null;
    }
}
