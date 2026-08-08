using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// The prefix index replaced a full-vocabulary scan, so these tests exist mainly to prove the binary-search
/// range finds exactly the same set the scan would have.
/// </summary>
public sealed class PrefixIndexTests : IClassFixture<EngineFixture>
{
    private readonly FrequencyDictionary _dictionary;
    private readonly PrefixIndex _index;

    public PrefixIndexTests(EngineFixture fixture)
    {
        _dictionary = fixture.Dictionary;
        _index = PrefixIndex.Build(_dictionary);
    }

    [Fact]
    public void IndexContainsWholeVocabulary() =>
        Assert.Equal(_dictionary.WordFrequency.Count, _index.Count);

    [Theory]
    [InlineData("w")]
    [InlineData("wo")]
    [InlineData("wor")]
    [InlineData("work")]
    [InlineData("t")]
    [InlineData("th")]
    [InlineData("is")]
    [InlineData("a")]
    [InlineData("z")]
    public void FindByPrefix_MatchesABruteForceScan(string prefix)
    {
        var expected = _dictionary.WordFrequency.Keys
            .Where(w => w.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToArray();

        var actual = _index.FindByPrefix(prefix, 1000)
            .Select(s => s.Word)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnknownPrefix_ReturnsEmpty() =>
        Assert.Empty(_index.FindByPrefix("qqq", 10));

    [Fact]
    public void EmptyPrefix_ReturnsEmpty() =>
        Assert.Empty(_index.FindByPrefix("", 10));

    [Fact]
    public void RespectsCandidateCap() =>
        Assert.Equal(2, _index.FindByPrefix("w", 2).Count);

    [Fact]
    public void ExactWordIsTaggedAsSuch()
    {
        var results = _index.FindByPrefix("work", 10);

        var exact = Assert.Single(results, s => s.Word == "work");
        Assert.Equal(SuggestionSource.ExactWord, exact.Source);
        Assert.All(results.Where(s => s.Word != "work"),
            s => Assert.Equal(SuggestionSource.PrefixCompletion, s.Source));
    }

    [Fact]
    public void CarriesFrequenciesFromTheDictionary()
    {
        var world = Assert.Single(_index.FindByPrefix("world", 5), s => s.Word == "world");
        Assert.Equal(_dictionary.GetFrequency("world"), world.Frequency);
    }

    [Fact]
    public void MostFrequent_ReturnsCommonestWordsInOrder()
    {
        var top = _index.MostFrequent(3).Select(s => s.Word).ToArray();

        Assert.Equal("the", top[0]);
        Assert.All(_index.MostFrequent(3), s => Assert.Equal(SuggestionSource.FrequentWord, s.Source));
    }

    [Fact]
    public void MostFrequent_IsDeterministic()
    {
        var first = _index.MostFrequent(5).Select(s => s.Word).ToArray();
        var second = _index.MostFrequent(5).Select(s => s.Word).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void MostFrequent_WithNonPositiveCount_ReturnsEmpty() =>
        Assert.Empty(_index.MostFrequent(0));
}
