using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// Exercises the four edit operations bounded Damerau-Levenshtein is supposed to handle, plus the boundary
/// where a word is simply too far away to be a candidate.
/// </summary>
public sealed class FuzzyMatchingTests : IClassFixture<EngineFixture>
{
    private readonly SymSpellIndex _index;

    public FuzzyMatchingTests(EngineFixture fixture) =>
        _index = SymSpellIndex.Build(fixture.Dictionary, maxEditDistance: 2);

    [Fact]
    public void Insertion_IsFound()
    {
        // "worrld" -> "world" (one extra letter)
        var results = _index.Lookup("worrld", 5);
        Assert.Contains(results, s => s.Word == "world" && s.EditDistance == 1);
    }

    [Fact]
    public void Deletion_IsFound()
    {
        // "wrld" -> "world" (one missing letter)
        var results = _index.Lookup("wrld", 5);
        Assert.Contains(results, s => s.Word == "world" && s.EditDistance == 1);
    }

    [Fact]
    public void Substitution_IsFound()
    {
        // "wurld" -> "world" (one wrong letter)
        var results = _index.Lookup("wurld", 5);
        Assert.Contains(results, s => s.Word == "world" && s.EditDistance == 1);
    }

    [Fact]
    public void AdjacentTransposition_CountsAsOneEdit()
    {
        // This is the "Damerau" part: under plain Levenshtein a swap costs two edits, not one.
        var results = _index.Lookup("wrold", 5);
        Assert.Contains(results, s => s.Word == "world" && s.EditDistance == 1);
    }

    [Fact]
    public void BeyondMaxDistance_IsNotReturned()
    {
        // "zzzzz" is far past two edits from anything in the vocabulary.
        Assert.Empty(_index.Lookup("zzzzz", 5));
    }

    [Fact]
    public void ExactWord_HasDistanceZero()
    {
        var results = _index.Lookup("world", 5);
        Assert.Contains(results, s => s.Word == "world" && s.EditDistance == 0);
    }

    [Fact]
    public void ResultsAreOrderedByDistanceThenFrequency()
    {
        var results = _index.Lookup("wor", 8);

        for (var i = 1; i < results.Count; i++)
        {
            var previous = results[i - 1];
            var current = results[i];

            Assert.True(
                previous.EditDistance < current.EditDistance ||
                (previous.EditDistance == current.EditDistance && previous.Frequency >= current.Frequency),
                $"'{previous.Word}' (d={previous.EditDistance}, f={previous.Frequency}) should not precede " +
                $"'{current.Word}' (d={current.EditDistance}, f={current.Frequency})");
        }
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty() => Assert.Empty(_index.Lookup("", 5));

    [Fact]
    public void MaxDistanceOne_ExcludesTwoEditCandidates()
    {
        var strict = SymSpellIndex.Build(TestVocabulary.BuildDictionary(), maxEditDistance: 1);

        // "wrrd" is two edits from "world": delete the 'o', then substitute 'l' for 'r'.
        // (An earlier version of this test used "wrrld", which is only *one* edit away — a single
        // substitution of 'o' for the first 'r' — so it proved nothing.)
        Assert.DoesNotContain(strict.Lookup("wrrd", 5), s => s.Word == "world");
        Assert.Contains(SymSpellIndex.Build(TestVocabulary.BuildDictionary(), maxEditDistance: 2).Lookup("wrrd", 5),
            s => s.Word == "world");
    }
}
