using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// The ranker is the seam Phase 2 plugs contextual probability into, so its current contract — banded,
/// deterministic, frequency-ordered within a band — is worth pinning down precisely.
/// </summary>
public sealed class RankingTests
{
    private readonly FrequencyRanker _ranker = new();

    private static Suggestion Candidate(string word, long frequency, int distance, SuggestionSource source) =>
        new(word, frequency, distance, source);

    [Fact]
    public void ExactWord_OutranksAMuchCommonerPrefixCompletion()
    {
        var candidates = new[]
        {
            Candidate("working", 147_000_000, 0, SuggestionSource.PrefixCompletion),
            Candidate("work", 419_000, 0, SuggestionSource.ExactWord),
        };

        var ranked = _ranker.Rank(new RankingContext("work"), candidates, 2);

        Assert.Equal("work", ranked[0].Word);
    }

    [Fact]
    public void PrefixCompletion_OutranksFuzzyMatchEvenWhenRarer()
    {
        var candidates = new[]
        {
            Candidate("the", 23_000_000_000, 1, SuggestionSource.FuzzyMatch),
            Candidate("zebra", 90_000, 0, SuggestionSource.PrefixCompletion),
        };

        var ranked = _ranker.Rank(new RankingContext("ze"), candidates, 2);

        // Bands exist precisely so an enormously common but distant word cannot displace what the user is
        // actually typing.
        Assert.Equal("zebra", ranked[0].Word);
    }

    [Fact]
    public void WithinABand_FrequencyDecidesOrder()
    {
        var candidates = new[]
        {
            Candidate("word", 98_000_000, 0, SuggestionSource.PrefixCompletion),
            Candidate("world", 431_000_000, 0, SuggestionSource.PrefixCompletion),
            Candidate("worth", 40_000_000, 0, SuggestionSource.PrefixCompletion),
        };

        var ranked = _ranker.Rank(new RankingContext("wor"), candidates, 3);

        Assert.Equal(new[] { "world", "word", "worth" }, ranked.Select(r => r.Word).ToArray());
    }

    [Fact]
    public void CloserEditDistance_Wins()
    {
        var candidates = new[]
        {
            Candidate("relieve", 4_000_000, 2, SuggestionSource.FuzzyMatch),
            Candidate("receive", 88_000, 1, SuggestionSource.FuzzyMatch),
        };

        var ranked = _ranker.Rank(new RankingContext("recieve"), candidates, 2);

        Assert.Equal("receive", ranked[0].Word);
    }

    [Fact]
    public void OrderingIsDeterministic()
    {
        var candidates = new[]
        {
            Candidate("work", 419_000_000, 0, SuggestionSource.PrefixCompletion),
            Candidate("world", 431_000_000, 0, SuggestionSource.PrefixCompletion),
            Candidate("words", 62_000_000, 0, SuggestionSource.PrefixCompletion),
        };

        var first = _ranker.Rank(new RankingContext("wor"), candidates, 3).Select(r => r.Word).ToArray();

        for (var i = 0; i < 5; i++)
        {
            var again = _ranker.Rank(new RankingContext("wor"), candidates, 3).Select(r => r.Word).ToArray();
            Assert.Equal(first, again);
        }
    }

    [Fact]
    public void IdenticalScores_BreakTiesStablyAndPredictably()
    {
        // Same source, same frequency, same length: only the ordinal comparison can separate them, and it
        // must do so the same way every time.
        var candidates = new[]
        {
            Candidate("beta", 1000, 0, SuggestionSource.PrefixCompletion),
            Candidate("alfa", 1000, 0, SuggestionSource.PrefixCompletion),
        };

        var ranked = _ranker.Rank(new RankingContext("x"), candidates, 2);

        Assert.Equal(new[] { "alfa", "beta" }, ranked.Select(r => r.Word).ToArray());
    }

    [Fact]
    public void ShorterCompletionWins_WhenFrequencyIsEqual()
    {
        var candidates = new[]
        {
            Candidate("workmanship", 50_000_000, 0, SuggestionSource.PrefixCompletion),
            Candidate("work", 50_000_000, 0, SuggestionSource.PrefixCompletion),
        };

        var ranked = _ranker.Rank(new RankingContext("wor"), candidates, 2);

        Assert.Equal("work", ranked[0].Word);
    }

    [Fact]
    public void RankRespectsMaxResults()
    {
        var candidates = Enumerable.Range(0, 20)
            .Select(i => Candidate($"word{i}", 1000 + i, 0, SuggestionSource.PrefixCompletion))
            .ToArray();

        Assert.Equal(3, _ranker.Rank(new RankingContext("word"), candidates, 3).Count);
    }

    [Fact]
    public void EmptyCandidateList_ReturnsEmpty() =>
        Assert.Empty(_ranker.Rank(new RankingContext("wor"), Array.Empty<Suggestion>(), 4));

    [Fact]
    public void ScoresAreAssignedToReturnedCandidates()
    {
        var candidates = new[] { Candidate("world", 431_000_000, 0, SuggestionSource.PrefixCompletion) };

        var ranked = _ranker.Rank(new RankingContext("wor"), candidates, 1);

        Assert.NotEqual(0, ranked[0].Score);
    }
}
