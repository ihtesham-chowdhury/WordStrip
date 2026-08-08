using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// Autocorrection rewrites what the user typed without asking, so these tests care as much about when it
/// declines to act as when it acts.
/// </summary>
public sealed class AutocorrectionTests : IClassFixture<EngineFixture>
{
    private readonly PredictionEngine _engine;

    public AutocorrectionTests(EngineFixture fixture) => _engine = fixture.Engine;

    [Fact]
    public void CorrectlySpelledWord_IsLeftAlone() =>
        Assert.Null(_engine.GetAutocorrection("world"));

    [Fact]
    public void StrongCorrection_IsApplied()
    {
        var correction = _engine.GetAutocorrection("recieve");

        Assert.NotNull(correction);
        Assert.Equal("receive", correction!.Value.Word);
        Assert.Equal(1, correction.Value.EditDistance);
    }

    [Fact]
    public void CommonTypo_IsCorrected()
    {
        var correction = _engine.GetAutocorrection("teh");

        Assert.NotNull(correction);
        Assert.Equal("the", correction!.Value.Word);
    }

    [Fact]
    public void UnreachableWord_ReturnsNull() =>
        Assert.Null(_engine.GetAutocorrection("xqzptv"));

    [Fact]
    public void WeakCandidate_IsRejectedByConfidenceThreshold()
    {
        // "zebrb" is one edit from "zebra", but "zebra" is far too rare to justify rewriting the user's text.
        Assert.Null(_engine.GetAutocorrection("zebrb", minFrequencyForConfidence: 1_000_000));
    }

    [Fact]
    public void SameCandidate_IsAcceptedWhenThresholdAllowsIt()
    {
        // The same input with a permissive threshold proves the rejection above was the threshold's doing
        // and not a failure to find the candidate at all.
        var correction = _engine.GetAutocorrection("zebrb", minFrequencyForConfidence: 1_000);

        Assert.NotNull(correction);
        Assert.Equal("zebra", correction!.Value.Word);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("q")]
    public void VeryShortInput_IsNeverCorrected(string input) =>
        Assert.Null(_engine.GetAutocorrection(input));

    [Fact]
    public void CorrectionIsCaseInsensitiveOnInput()
    {
        var correction = _engine.GetAutocorrection("RECIEVE");

        Assert.NotNull(correction);
        Assert.Equal("receive", correction!.Value.Word);
    }

    [Fact]
    public void IsCorrectlySpelled_MatchesDictionaryMembership()
    {
        Assert.True(_engine.IsCorrectlySpelled("world"));
        Assert.True(_engine.IsCorrectlySpelled("World"));
        Assert.False(_engine.IsCorrectlySpelled("wrold"));
        Assert.False(_engine.IsCorrectlySpelled(""));
    }
}
