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
    /// No-op, and correct rather than lazy. The hook provider has to be told about text it did not see typed,
    /// because its state is a guess it maintains itself. This one's state is the document, and the service
    /// reports the document again the moment it changes — so an insertion arrives as an ordinary update,
    /// already correct, without anyone having to remember to announce it.
    /// </summary>
    public void NoteTextInserted(string text) { }

    /// <summary>No-op, for the same reason as <see cref="NoteTextInserted"/>.</summary>
    public void NoteWordCorrected(string correctedWord) { }

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
