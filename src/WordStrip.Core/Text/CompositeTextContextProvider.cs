using WordStrip.Core.Input;

namespace WordStrip.Core.Text;

/// <summary>
/// Chooses between input mechanisms, one keystroke at a time, and guarantees that one of them always answers.
///
/// <para>This is Stage 4 of the phase brief, and it is deliberately built before the thing it is meant to
/// fall back from. The requirement it exists to satisfy is stated three separate ways in the brief —
/// "WordStrip should remain usable in applications where TSF integration is unavailable", "never make the
/// whole application fail because one input mechanism is unsupported", and "the application must never
/// prevent normal typing because the prediction system encountered an error". Writing the fallback last, as
/// a wrapper hurriedly added around a provider that turned out to be flaky, is how that requirement gets
/// half-met.</para>
///
/// <para><b>Selection is per call, not per session.</b> Providers are consulted in order and the first one
/// reporting <see cref="ITextContextProvider.IsAvailable"/> wins. TSF availability is a property of the
/// focused application, not of the machine, so this has to be re-evaluated constantly: the user alt-tabs
/// from Word to Notepad and the answer changes with no event to announce it.</para>
/// </summary>
public sealed class CompositeTextContextProvider : ITextContextProvider
{
    private readonly IReadOnlyList<ITextContextProvider> _providers;

    /// <summary>
    /// Providers that threw and have been taken out of service.
    ///
    /// <para>Demotion is permanent for the life of the process, which is a deliberate choice rather than an
    /// omission. A provider throwing from <see cref="GetContext"/> is a bug in that provider, and the
    /// alternative — retrying it on the next keystroke — means paying for the same exception dozens of times
    /// a second while the user types, which is worse than losing the richer context until restart. The cost
    /// is that a genuinely transient failure is not recovered from; if that turns out to matter, a cooldown
    /// is the change to make, not removing the demotion.</para>
    /// </summary>
    private readonly HashSet<ITextContextProvider> _failed = new();

    /// <summary>
    /// Which provider last answered. Events are only forwarded from this one, which is what stops two
    /// providers watching the same typing from each announcing the same word and publishing the bar twice.
    /// </summary>
    private ITextContextProvider? _active;

    /// <param name="providers">
    /// In order of preference, richest first. The last one is the fallback and is expected to be available
    /// everywhere — for WordStrip that is
    /// <see cref="KeyboardHookTextContextProvider"/>, which watches the whole desktop.
    /// </param>
    public CompositeTextContextProvider(params ITextContextProvider[] providers)
    {
        if (providers is null || providers.Length == 0)
            throw new ArgumentException("At least one provider is required.", nameof(providers));

        _providers = providers;

        foreach (var provider in _providers)
        {
            provider.CurrentWordChanged += OnCurrentWordChanged;
            provider.WordCommitted += OnWordCommitted;
            provider.ContextLost += OnContextLost;
        }
    }

    /// <summary>Which mechanism answered most recently. Reports the preferred provider's source before anything has been asked.</summary>
    public TextContextSource Source => (_active ?? Select() ?? _providers[0]).Source;

    /// <summary>True while any provider can still answer. False only if every one of them has failed.</summary>
    public bool IsAvailable => Select() is not null;

    /// <summary>Raised when the mechanism in use changes, so a switch can be logged without polling for it.</summary>
    public event EventHandler<TextContextSource>? ActiveSourceChanged;

    public event EventHandler<string>? CurrentWordChanged;
    public event EventHandler<WordCommittedEventArgs>? WordCommitted;
    public event EventHandler? ContextLost;

    /// <summary>
    /// Asks the best available provider. A provider that throws is taken out of service and the next one is
    /// asked instead, so a broken provider costs the user context rather than the ability to type.
    /// </summary>
    public TextContext GetContext()
    {
        foreach (var provider in _providers)
        {
            if (_failed.Contains(provider)) continue;

            bool available;
            try
            {
                available = provider.IsAvailable;
            }
            catch
            {
                Demote(provider);
                continue;
            }

            if (!available) continue;

            try
            {
                var context = provider.GetContext();
                SetActive(provider);
                return context;
            }
            catch
            {
                Demote(provider);
            }
        }

        // Everything failed or nothing is available. "We know nothing about the surroundings" is a valid
        // answer that the whole stack already handles — it is what focus on a button looks like — so the bar
        // simply hides and typing is untouched.
        return TextContext.None;
    }

    /// <summary>
    /// Tells <b>every</b> provider, not just the active one.
    ///
    /// <para>A dormant provider still has to be correct the moment it takes over. The keyboard hook's shadow
    /// buffer is the case that matters: injected keystrokes are deliberately filtered out of it, so a word
    /// inserted while TSF was driving would be invisible to the hook, and switching back mid-sentence would
    /// hand the model a context missing the word the user just accepted.</para>
    /// </summary>
    public void NoteTextInserted(string text) => ForEachLiveProvider(p => p.NoteTextInserted(text));

    public void NoteWordCorrected(string correctedWord) => ForEachLiveProvider(p => p.NoteWordCorrected(correctedWord));

    private void ForEachLiveProvider(Action<ITextContextProvider> action)
    {
        foreach (var provider in _providers)
        {
            if (_failed.Contains(provider)) continue;

            try { action(provider); }
            catch { Demote(provider); }
        }
    }

    /// <summary>The first provider that is not out of service and says it can answer.</summary>
    private ITextContextProvider? Select()
    {
        foreach (var provider in _providers)
        {
            if (_failed.Contains(provider)) continue;

            try
            {
                if (provider.IsAvailable) return provider;
            }
            catch
            {
                Demote(provider);
            }
        }

        return null;
    }

    private void SetActive(ITextContextProvider provider)
    {
        if (ReferenceEquals(_active, provider)) return;

        _active = provider;
        ActiveSourceChanged?.Invoke(this, provider.Source);
    }

    private void Demote(ITextContextProvider provider)
    {
        _failed.Add(provider);
        if (ReferenceEquals(_active, provider)) _active = null;
    }

    /// <summary>
    /// Whether an event from this provider should be passed on.
    ///
    /// <para>Providers observe the same typing simultaneously, so without this the hook and a TSF provider
    /// would each announce every word and the bar would be published twice per keystroke. Deciding by
    /// "is this the provider that would be selected right now" rather than by "is this the one that last
    /// answered" means the very first event after a switch is attributed correctly, instead of being dropped
    /// because nothing had asked for context yet.</para>
    /// </summary>
    private bool ShouldForwardFrom(object? sender)
    {
        if (sender is not ITextContextProvider provider) return false;
        if (_failed.Contains(provider)) return false;

        var selected = Select();
        if (selected is null) return false;
        if (!ReferenceEquals(selected, provider)) return false;

        SetActive(provider);
        return true;
    }

    private void OnCurrentWordChanged(object? sender, string word)
    {
        if (ShouldForwardFrom(sender)) CurrentWordChanged?.Invoke(this, word);
    }

    private void OnWordCommitted(object? sender, WordCommittedEventArgs e)
    {
        if (ShouldForwardFrom(sender)) WordCommitted?.Invoke(this, e);
    }

    private void OnContextLost(object? sender, EventArgs e)
    {
        if (ShouldForwardFrom(sender)) ContextLost?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Unsubscribes but does <b>not</b> dispose the providers it was given. Whoever constructed them owns
    /// them — the same rule <see cref="SuggestionController"/> and
    /// <see cref="KeyboardHookTextContextProvider"/> follow.
    /// </summary>
    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.CurrentWordChanged -= OnCurrentWordChanged;
            provider.WordCommitted -= OnWordCommitted;
            provider.ContextLost -= OnContextLost;
        }
    }
}
