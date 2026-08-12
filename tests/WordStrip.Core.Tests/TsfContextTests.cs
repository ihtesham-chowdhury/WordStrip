using System.IO.Pipes;
using WordStrip.Core.Automation;
using WordStrip.Core.Text;
using WordStrip.Core.Text.Tsf;

namespace WordStrip.Core.Tests;

/// <summary>
/// Covers Phase 7 Stage 2: the wire format between the text service and WordStrip, the parsing of document
/// text into the context the prediction engine consumes, and the named-pipe channel that carries it.
///
/// <para>The parsing tests are the ones that matter most. Everything downstream of this point has years of
/// coverage, but it has only ever been fed a shadow buffer built one keystroke at a time. Being handed a
/// slab of real document text is a new shape of input, and it arrives from an application nobody controls.
/// </para>
/// </summary>
public class TsfContextTests
{
    // --- Wire format ----------------------------------------------------------------------------------

    [Fact]
    public void A_message_survives_a_round_trip()
    {
        var original = new TsfContextMessage(
            IsEditable: true, IsPasswordField: false, HasSelection: true,
            Caret: new CaretRect(10, 20, 12, 40), TextBeforeCaret: "hello wor");

        var parsed = TsfContextMessage.TryParse(original.ToBytes());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Every_flag_survives_independently()
    {
        foreach (var (editable, password, selection) in new[]
                 {
                     (true, false, false), (false, true, false), (false, false, true),
                     (true, true, true), (false, false, false),
                 })
        {
            var message = new TsfContextMessage(editable, password, selection, null, "x");
            var parsed = TsfContextMessage.TryParse(message.ToBytes());

            Assert.NotNull(parsed);
            Assert.Equal(editable, parsed!.Value.IsEditable);
            Assert.Equal(password, parsed.Value.IsPasswordField);
            Assert.Equal(selection, parsed.Value.HasSelection);
        }
    }

    [Fact]
    public void An_absent_caret_stays_absent()
    {
        // Distinct from a caret at 0,0,0,0 - one means "off screen or unknown", the other is a position.
        var parsed = TsfContextMessage.TryParse(
            new TsfContextMessage(true, false, false, null, "hi").ToBytes());

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Value.Caret);
    }

    [Fact]
    public void Text_longer_than_the_cap_is_truncated_to_the_end_nearest_the_caret()
    {
        var long_ = new string('a', 500) + "tail";

        var parsed = TsfContextMessage.TryParse(
            new TsfContextMessage(true, false, false, null, long_).ToBytes());

        Assert.NotNull(parsed);
        Assert.Equal(TsfContextMessage.MaxTextChars, parsed!.Value.TextBeforeCaret.Length);
        Assert.EndsWith("tail", parsed.Value.TextBeforeCaret);
    }

    [Fact]
    public void A_truncated_message_is_rejected_rather_than_guessed_at()
    {
        var bytes = new TsfContextMessage(true, false, false, null, "hello").ToBytes();

        Assert.Null(TsfContextMessage.TryParse(bytes.AsSpan(0, bytes.Length - 4)));
        Assert.Null(TsfContextMessage.TryParse(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void A_message_from_a_different_protocol_version_is_rejected()
    {
        var bytes = new TsfContextMessage(true, false, false, null, "hello").ToBytes();
        bytes[0] = 99;

        // A service and a tray application from different builds must refuse each other rather than misread.
        Assert.Null(TsfContextMessage.TryParse(bytes));
    }

    [Fact]
    public void A_message_claiming_more_text_than_the_cap_is_rejected()
    {
        var bytes = new TsfContextMessage(true, false, false, null, "hi").ToBytes();
        BitConverter.GetBytes((uint)100_000).CopyTo(bytes, 24);

        // Never allocate what an untrusted sender asks for.
        Assert.Null(TsfContextMessage.TryParse(bytes));
    }

    // --- Turning document text into context -------------------------------------------------------------

    private static (IReadOnlyList<string> Preceding, string Word, bool SentenceStart) Parse(string text) =>
        TsfTextContextProvider.Parse(text);

    [Fact]
    public void A_word_in_progress_is_separated_from_the_words_behind_it()
    {
        var (preceding, word, _) = Parse("how are yo");

        Assert.Equal("yo", word);
        Assert.Equal(new[] { "how", "are" }, preceding);
    }

    [Fact]
    public void Text_ending_at_a_space_has_no_word_in_progress()
    {
        var (preceding, word, _) = Parse("how are ");

        Assert.Equal(string.Empty, word);
        Assert.Equal(new[] { "how", "are" }, preceding);
    }

    [Fact]
    public void Only_the_two_nearest_words_are_kept()
    {
        // More than a trigram consumes would be carrying the user's text around for no purpose.
        var (preceding, _, _) = Parse("one two three four five ");

        Assert.Equal(new[] { "four", "five" }, preceding);
    }

    [Fact]
    public void The_word_in_progress_keeps_its_capitals()
    {
        // It is matched as a prefix and the ranker reads the user's casing; normalising it here would lose
        // both. The preceding words are normalised, because the model is keyed on normalised tokens.
        var (preceding, word, _) = Parse("I met Ihtesh");

        Assert.Equal("Ihtesh", word);
        Assert.Equal(new[] { "i", "met" }, preceding);
    }

    [Fact]
    public void An_empty_document_reads_as_the_start_of_a_sentence()
    {
        var (preceding, word, sentenceStart) = Parse("");

        Assert.Empty(preceding);
        Assert.Equal(string.Empty, word);
        Assert.True(sentenceStart);
    }

    [Fact]
    public void A_full_stop_puts_the_caret_at_a_sentence_start()
    {
        Assert.True(Parse("That is done. ").SentenceStart);
        Assert.True(Parse("Really? ").SentenceStart);
        Assert.True(Parse("Stop! ").SentenceStart);
    }

    [Fact]
    public void Mid_sentence_is_not_a_sentence_start()
    {
        Assert.False(Parse("how are ").SentenceStart);
        Assert.False(Parse("how are yo").SentenceStart);
    }

    [Fact]
    public void A_hyphenated_word_is_treated_as_one_word_in_progress()
    {
        // KeyTranslator's rule, so the TSF path and the keyboard hook agree on where a word begins.
        Assert.Equal("well-know", Parse("a well-know").Word);
    }

    [Fact]
    public void Punctuation_before_the_caret_does_not_become_a_word()
    {
        var (preceding, word, _) = Parse("wait, then ");

        Assert.Equal(string.Empty, word);
        Assert.Equal(new[] { "wait", "then" }, preceding);
    }

    // --- Provider behaviour -----------------------------------------------------------------------------

    private static TsfContextMessage Editable(string text, bool password = false) =>
        new(IsEditable: true, IsPasswordField: password, HasSelection: false, Caret: null, TextBeforeCaret: text);

    [Fact]
    public void A_provider_with_nothing_connected_is_unavailable()
    {
        using var provider = new TsfTextContextProvider();

        Assert.False(provider.IsAvailable);
        Assert.False(provider.GetContext().IsSuggestible);
    }

    [Fact]
    public void A_connected_service_reporting_an_editable_surface_makes_the_provider_available()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);
        provider.Apply(Editable("hello wor"));

        Assert.True(provider.IsAvailable);
        Assert.Equal("wor", provider.GetContext().CurrentWord);
    }

    [Fact]
    public void A_connected_service_on_a_non_editable_surface_stands_aside()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);
        provider.Apply(new TsfContextMessage(false, false, false, null, ""));

        // Availability has to go with it, or focusing a classic dialog would win the composite's selection
        // and the keyboard hook that could have served it would never get a turn.
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void A_password_field_is_reported_but_never_suggestible()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);
        provider.Apply(Editable("hunter", password: true));

        Assert.True(provider.IsAvailable);
        Assert.False(provider.GetContext().IsSuggestible);
    }

    [Fact]
    public void Each_new_word_is_announced_once()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);

        var announced = new List<string>();
        provider.CurrentWordChanged += (_, w) => announced.Add(w);

        provider.Apply(Editable("w"));
        provider.Apply(Editable("wo"));
        provider.Apply(Editable("wo"));   // repeat - the document did not change
        provider.Apply(Editable("wor"));

        Assert.Equal(new[] { "w", "wo", "wor" }, announced);
    }

    [Fact]
    public void Focus_leaving_a_text_surface_reports_the_context_as_lost()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);
        provider.Apply(Editable("hello"));

        var lost = 0;
        provider.ContextLost += (_, _) => lost++;
        provider.Apply(new TsfContextMessage(false, false, false, null, ""));

        Assert.Equal(1, lost);
    }

    [Fact]
    public void A_service_disconnecting_reports_the_context_as_lost()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);
        provider.Apply(Editable("hello"));

        var lost = 0;
        provider.ContextLost += (_, _) => lost++;
        provider.SetConnected(false);

        Assert.Equal(1, lost);
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void A_word_committed_is_never_raised_in_this_stage()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);

        var commits = 0;
        provider.WordCommitted += (_, _) => commits++;

        provider.Apply(Editable("hello"));
        provider.Apply(Editable("hello "));
        provider.Apply(Editable("hello world"));
        provider.Apply(Editable("hello world "));

        // Deliberate. That event drives autocorrect and learning, both of which end in rewriting text in the
        // focused application, and committing through TSF is Stage 3. Firing it now would ask the injector
        // to rewrite text in Chrome on the strength of a mechanism nobody has verified.
        Assert.Equal(0, commits);
    }

    [Fact]
    public void Being_told_about_an_insertion_changes_nothing()
    {
        using var provider = new TsfTextContextProvider();
        provider.SetConnected(true);
        provider.Apply(Editable("hello "));

        provider.NoteTextInserted("world");

        // The document is the source of truth and the service will report it again. Unlike the hook, this
        // provider has no guess that needs correcting.
        Assert.Equal(new[] { "hello" }, provider.GetContext().PrecedingWords);
    }

    [Fact]
    public void Events_go_through_the_supplied_dispatcher()
    {
        var queued = new List<Action>();
        using var provider = new TsfTextContextProvider(post: queued.Add);
        provider.SetConnected(true);

        var announced = 0;
        provider.CurrentWordChanged += (_, _) => announced++;
        provider.Apply(Editable("wo"));

        // Messages arrive on a pipe thread; everything downstream has always run on the UI thread.
        Assert.Equal(0, announced);
        Assert.Single(queued);

        queued[0]();
        Assert.Equal(1, announced);
    }

    // --- The channel, over a real pipe --------------------------------------------------------------------

    [Fact]
    public async Task Context_sent_down_a_real_pipe_arrives_as_a_usable_context()
    {
        var pipeName = "WordStrip.Test." + Guid.NewGuid().ToString("N");

        using var provider = new TsfTextContextProvider();
        using var channel = new TsfContextChannel(provider, pipeName);
        channel.Start();

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000);

        var payload = new TsfContextMessage(true, false, false, new CaretRect(1, 2, 3, 4), "how are yo").ToBytes();
        await client.WriteAsync(payload);
        await client.FlushAsync();

        await WaitUntil(() => provider.GetContext().CurrentWord == "yo");

        var context = provider.GetContext();
        Assert.Equal("yo", context.CurrentWord);
        Assert.Equal(new[] { "how", "are" }, context.PrecedingWords);
        Assert.Equal(new CaretRect(1, 2, 3, 4), context.Caret);
        Assert.Equal(TextContextSource.TextServices, context.Source);
        Assert.True(provider.IsAvailable);
    }

    [Fact]
    public async Task A_host_disconnecting_takes_availability_with_it()
    {
        var pipeName = "WordStrip.Test." + Guid.NewGuid().ToString("N");

        using var provider = new TsfTextContextProvider();
        using var channel = new TsfContextChannel(provider, pipeName);
        channel.Start();

        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000);
        await client.WriteAsync(new TsfContextMessage(true, false, false, null, "hi").ToBytes());
        await WaitUntil(() => provider.IsAvailable);

        await client.DisposeAsync();

        // Applications close. When the last one goes, the TSF path must stand aside for the keyboard hook.
        await WaitUntil(() => !provider.IsAvailable);
        Assert.Equal(0, channel.ConnectedClients);
    }

    [Fact]
    public async Task Several_hosts_can_be_connected_at_once()
    {
        var pipeName = "WordStrip.Test." + Guid.NewGuid().ToString("N");

        using var provider = new TsfTextContextProvider();
        using var channel = new TsfContextChannel(provider, pipeName);
        channel.Start();

        // Every application with the service loaded holds a connection, even though only the focused one is
        // sending. A channel that accepted one would work until the user opened a second window.
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var c = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await c.ConnectAsync(10_000);
                clients.Add(c);
            }

            await WaitUntil(() => channel.ConnectedClients == 3);
            Assert.Equal(3, channel.ConnectedClients);
        }
        finally
        {
            foreach (var c in clients) await c.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_malformed_message_is_dropped_without_disturbing_what_came_before()
    {
        var pipeName = "WordStrip.Test." + Guid.NewGuid().ToString("N");

        using var provider = new TsfTextContextProvider();
        using var channel = new TsfContextChannel(provider, pipeName);
        channel.Start();

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000);

        await client.WriteAsync(new TsfContextMessage(true, false, false, null, "how are yo").ToBytes());
        await WaitUntil(() => provider.GetContext().CurrentWord == "yo");

        await client.WriteAsync(new byte[] { 1, 2, 3 });
        await Task.Delay(150);

        // Half a context is worse than none, and the sender will send another on the next keystroke.
        Assert.Equal("yo", provider.GetContext().CurrentWord);
        Assert.True(provider.IsAvailable);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail($"Condition not met within {timeoutMs} ms");
    }
}
