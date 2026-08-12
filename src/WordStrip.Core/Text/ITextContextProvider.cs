using WordStrip.Core.Input;

namespace WordStrip.Core.Text;

/// <summary>
/// Where the text around the caret comes from, and how a replacement gets back into the document.
///
/// <para>This is the seam Phase 7 is built on. Today one implementation exists — the keyboard hook path that
/// WordStrip has always used. A Text Services Framework implementation is intended to sit beside it, not
/// replace it: TSF is unavailable in plenty of applications, and the existing path must keep working in all
/// of them. Anything above this interface must therefore be written so that it cannot tell which provider it
/// has.</para>
///
/// <para><b>Why the events look the way they do.</b> They mirror what a text editor can actually tell you
/// about: the word being typed changed, a word was finished, or we no longer know where we are. A hook infers
/// all three from keystrokes; TSF is told them by the application. The consumer does not care which.</para>
/// </summary>
public interface ITextContextProvider : IDisposable
{
    /// <summary>Which mechanism this provider speaks for. Used for diagnostics and for deciding precedence between providers.</summary>
    TextContextSource Source { get; }

    /// <summary>
    /// Whether this provider can serve the surface that currently has focus.
    ///
    /// <para>Expected to change from moment to moment as the user moves between applications, which is the
    /// entire reason a fallback exists: a TSF provider answers <c>false</c> the instant focus lands somewhere
    /// it has no text store for, and the hook provider takes over without anything above noticing.</para>
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Reads the current state. Called on the typing path, so it must be cheap and must never block — no disk,
    /// no model loading, no inference. The hook implementation answers from memory plus one Win32 focus query.
    /// </summary>
    TextContext GetContext();

    /// <summary>The word being typed changed. An empty string means there is no longer a word in progress.</summary>
    event EventHandler<string>? CurrentWordChanged;

    /// <summary>A word was finished by a space, newline, tab or punctuation. Carries the context it was typed in.</summary>
    event EventHandler<WordCommittedEventArgs>? WordCommitted;

    /// <summary>
    /// We no longer know what is around the caret — a click, an arrow key, a paste, a modifier combo. Anything
    /// derived from the previous context must be discarded rather than aged.
    /// </summary>
    event EventHandler? ContextLost;

    /// <summary>
    /// Tells the provider that text reached the document without being typed key by key, so its notion of
    /// what is behind the caret stays correct. A hook provider has no other way to find out; a TSF provider
    /// may be able to ignore this and re-read the document instead.
    /// </summary>
    void NoteTextInserted(string text);

    /// <summary>
    /// Tells the provider that the last finished word was rewritten by autocorrect, so predictions follow what
    /// ended up on screen rather than the typo that was replaced.
    /// </summary>
    void NoteWordCorrected(string correctedWord);
}
