using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

public sealed class PrefixCompletionTests : IClassFixture<EngineFixture>
{
    private readonly PredictionEngine _engine;

    public PrefixCompletionTests(EngineFixture fixture) => _engine = fixture.Engine;

    [Fact]
    public void SingleLetterPrefix_ReturnsMatchesOrderedByFrequency()
    {
        var results = _engine.GetLiveSuggestions("a", 3);

        Assert.NotEmpty(results);
        Assert.All(results, s => Assert.StartsWith("a", s.Word, StringComparison.Ordinal));

        // "a" itself is both an exact word and the most frequent, so it must lead.
        Assert.Equal("a", results[0].Word);
    }

    [Fact]
    public void TwoLetterPrefix_ReturnsOnlyWordsWithThatPrefix()
    {
        var results = _engine.GetLiveSuggestions("th", 5);

        Assert.NotEmpty(results);
        Assert.All(results, s => Assert.StartsWith("th", s.Word, StringComparison.Ordinal));
        Assert.Equal("the", results[0].Word); // by far the most frequent
    }

    [Fact]
    public void ThreeLetterPrefix_PrefersCommonerCompletions()
    {
        var results = _engine.GetLiveSuggestions("wor", 4);

        Assert.All(results, s => Assert.StartsWith("wor", s.Word, StringComparison.Ordinal));
        Assert.Equal("world", results[0].Word);
        Assert.Equal("work", results[1].Word);
    }

    [Fact]
    public void ExactDictionaryWord_RanksItselfFirst()
    {
        // "work" is both an exact word and a prefix of commoner-in-aggregate words; exactness must win.
        var results = _engine.GetLiveSuggestions("work", 4);

        Assert.Equal("work", results[0].Word);
        Assert.Equal(SuggestionSource.ExactWord, results[0].Source);
    }

    [Fact]
    public void NoMatches_ReturnsEmpty()
    {
        // Long enough that fuzzy top-up cannot reach anything either.
        Assert.Empty(_engine.GetLiveSuggestions("qqqqqqqqqq", 4));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceInput_ReturnsEmpty(string input) =>
        Assert.Empty(_engine.GetLiveSuggestions(input, 4));

    [Fact]
    public void ZeroOrNegativeMaxResults_ReturnsEmpty()
    {
        Assert.Empty(_engine.GetLiveSuggestions("wor", 0));
        Assert.Empty(_engine.GetLiveSuggestions("wor", -1));
    }

    [Fact]
    public void NeverReturnsMoreThanRequested()
    {
        for (var max = 1; max <= 6; max++)
            Assert.True(_engine.GetLiveSuggestions("wor", max).Count <= max);
    }

    [Fact]
    public void UppercaseInput_MatchesLowercaseVocabulary()
    {
        // The engine lower-cases input; the UI re-applies the user's capitalisation on insert.
        var results = _engine.GetLiveSuggestions("WOR", 3);

        Assert.NotEmpty(results);
        Assert.Equal("world", results[0].Word);
    }

    [Fact]
    public void ShortPrefixWithFewMatches_DoesNotPullInFuzzyNoise()
    {
        // At one or two letters nearly any word is within edit distance 2, so fuzzy top-up is suppressed;
        // everything returned must genuinely start with the prefix.
        var results = _engine.GetLiveSuggestions("ze", 5);

        Assert.All(results, s => Assert.StartsWith("ze", s.Word, StringComparison.Ordinal));
    }

    [Fact]
    public void LongerPrefixWithTooFewMatches_TopsUpWithFuzzyCandidates()
    {
        // "hellp" has no prefix completions, so the list can only be filled fuzzily.
        var results = _engine.GetLiveSuggestions("hellp", 4);

        Assert.NotEmpty(results);
        Assert.Contains(results, s => s.Word is "hello" or "help");
        Assert.Contains(results, s => s.Source == SuggestionSource.FuzzyMatch);
    }
}
