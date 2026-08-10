using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// Emoji suggestions: the bar offering 🍕 when you type "pizza", the way a phone keyboard does.
///
/// <para>Most of what matters here is restraint. The bar has three to seven slots and they belong to words;
/// a wrong emoji is more jarring than a wrong word because it is the only thing on the strip the eye goes
/// to. So the rules are deliberately narrow — at most one, always last, never on an ambiguous match.</para>
/// </summary>
public class EmojiSuggestionTests
{
    private readonly EmojiSuggester _emoji = EmojiSuggester.Default;

    // --- Matching ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("pizza", "🍕")]
    [InlineData("coffee", "☕")]
    [InlineData("thanks", "🙏")]
    [InlineData("done", "✅")]
    [InlineData("meeting", "📅")]
    public void An_exact_keyword_gives_its_emoji(string word, string expected)
    {
        Assert.Equal(expected, _emoji.Match(word));
    }

    [Fact]
    public void Matching_ignores_capitalisation()
    {
        Assert.Equal("🍕", _emoji.Match("PIZZA"));
        Assert.Equal("🍕", _emoji.Match("Pizza"));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.Equal("🍕", _emoji.Match("  pizza  "));
    }

    [Fact]
    public void An_unambiguous_prefix_matches()
    {
        // Nothing else in the table begins "piz".
        Assert.Equal("🍕", _emoji.Match("piz"));
    }

    [Fact]
    public void An_ambiguous_prefix_matches_nothing()
    {
        // "cal" opens both "calendar" and "call", which mean different things. Offering either would be a
        // guess, and a wrong emoji is worse than none.
        Assert.Null(_emoji.Match("cal"));
    }

    [Fact]
    public void A_prefix_whose_matches_all_mean_the_same_thing_still_matches()
    {
        // "congrats" and "congratulations" are both 🎉, so there is nothing to be ambiguous about.
        Assert.Equal("🎉", _emoji.Match("congrat"));
    }

    [Fact]
    public void Prefixes_shorter_than_the_minimum_never_match()
    {
        Assert.Null(_emoji.Match("pi"));
        Assert.Null(_emoji.Match("p"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("qwertyuiop")]
    [InlineData("zzzz")]
    public void Nonsense_matches_nothing(string word)
    {
        Assert.Null(_emoji.Match(word));
    }

    [Fact]
    public void The_bundled_table_is_a_curated_size()
    {
        // Small enough that ordinary words are not constantly displaced by pictograms. If this ever grows
        // into the thousands, the matching rules need revisiting before the table does.
        Assert.InRange(_emoji.Count, 100, 600);
    }

    [Fact]
    public void Matching_is_deterministic()
    {
        for (var attempt = 0; attempt < 50; attempt++)
            Assert.Equal("🎉", _emoji.Match("congrat"));
    }

    [Fact]
    public void A_duplicate_keyword_resolves_to_the_first_definition()
    {
        var suggester = new EmojiSuggester(new[] { ("word", "1️⃣"), ("word", "2️⃣") });

        Assert.Equal("1️⃣", suggester.Match("word"));
    }

    // --- Placement on the bar ---------------------------------------------------------------------------

    private static PredictionEngine NewEngine(EmojiSuggester? emoji) =>
        new(TestVocabulary.BuildDictionary(),
            SymSpellIndex.Build(TestVocabulary.BuildDictionary(), 2),
            emoji: emoji);

    [Fact]
    public void An_emoji_joins_the_bar_when_the_typed_word_matches()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        var suggestions = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty);

        Assert.Contains(suggestions, s => s.IsEmoji);
    }

    [Fact]
    public void At_most_one_emoji_appears()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        var suggestions = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty);

        Assert.Equal(1, suggestions.Count(s => s.IsEmoji));
    }

    [Fact]
    public void The_emoji_takes_the_last_slot_and_no_other()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        var suggestions = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty);

        Assert.True(suggestions[^1].IsEmoji, "the emoji belongs at the end, behind every word");
    }

    [Fact]
    public void The_bar_never_grows_beyond_the_width_the_user_chose()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        foreach (var count in new[] { 3, 4, 5, 6, 7 })
            Assert.True(engine.GetLiveSuggestions("hello", count, PredictionContext.Empty).Count <= count);
    }

    [Fact]
    public void An_emoji_displaces_at_most_one_word()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        var withEmoji = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty, includeEmoji: true);
        var withoutEmoji = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty, includeEmoji: false);

        var wordsLost = withoutEmoji.Count(s => !s.IsEmoji) - withEmoji.Count(s => !s.IsEmoji);
        Assert.InRange(wordsLost, 0, 1);
    }

    [Fact]
    public void Turning_emoji_off_removes_them_entirely()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        var suggestions = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty, includeEmoji: false);

        Assert.DoesNotContain(suggestions, s => s.IsEmoji);
    }

    [Fact]
    public void An_engine_with_no_emoji_table_behaves_exactly_as_before()
    {
        var withoutEmoji = NewEngine(null);
        var withEmojiDisabled = NewEngine(EmojiSuggester.Default);

        Assert.Equal(
            withoutEmoji.GetLiveSuggestions("hello", 5, PredictionContext.Empty).Select(s => s.Word),
            withEmojiDisabled.GetLiveSuggestions("hello", 5, PredictionContext.Empty, includeEmoji: false).Select(s => s.Word));
    }

    [Fact]
    public void A_word_with_no_emoji_leaves_the_bar_untouched()
    {
        var engine = NewEngine(EmojiSuggester.Default);

        var suggestions = engine.GetLiveSuggestions("issu", 5, PredictionContext.Empty);

        Assert.DoesNotContain(suggestions, s => s.IsEmoji);
    }

    [Fact]
    public void A_one_slot_bar_keeps_its_word()
    {
        // With a single slot the word is the more useful answer; an emoji taking it would be a poor trade.
        var engine = NewEngine(EmojiSuggester.Default);

        var suggestions = engine.GetLiveSuggestions("hello", 1, PredictionContext.Empty);

        Assert.DoesNotContain(suggestions, s => s.IsEmoji);
    }

    [Fact]
    public void An_emoji_is_inserted_as_itself()
    {
        // It travels through exactly the same path as a word: Suggestion.Word is what gets typed, so the
        // injector and the UI need to know nothing about emoji at all.
        var engine = NewEngine(EmojiSuggester.Default);

        var emoji = engine.GetLiveSuggestions("hello", 5, PredictionContext.Empty).Single(s => s.IsEmoji);

        Assert.Equal("👋", emoji.Word);
    }
}
