using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// How words are written — "I", "don't", "London" — as opposed to which words they are. The rule under test
/// throughout is the refusal: every ambiguous spelling ("ill", "were", "well", "its") is left exactly as typed.
/// </summary>
public class EnglishFormsTests
{
    [Theory]
    [InlineData("i", "I")]
    [InlineData("im", "I'm")]
    [InlineData("ive", "I've")]
    [InlineData("i'm", "I'm")]
    [InlineData("dont", "don't")]
    [InlineData("didnt", "didn't")]
    [InlineData("youre", "you're")]
    [InlineData("thats", "that's")]
    [InlineData("Dont", "Don't")]
    [InlineData("london", "London")]
    [InlineData("iran", "Iran")]
    [InlineData("monday", "Monday")]
    [InlineData("iphone", "iPhone")]
    [InlineData("usa", "USA")]
    public void Words_only_ever_written_one_way_are_corrected_to_it(string typed, string expected)
    {
        Assert.Equal(expected, EnglishForms.CorrectionFor(typed));
    }

    [Theory]
    [InlineData("ill")]     // I'll
    [InlineData("id")]      // I'd
    [InlineData("were")]    // we're
    [InlineData("well")]    // we'll
    [InlineData("its")]     // it's
    [InlineData("lets")]    // let's
    [InlineData("shell")]   // she'll
    [InlineData("may")]     // May
    [InlineData("march")]   // March
    [InlineData("turkey")]  // Turkey
    [InlineData("windows")] // Windows
    [InlineData("apple")]   // Apple
    public void Ambiguous_spellings_are_never_corrected(string typed)
    {
        Assert.Null(EnglishForms.CorrectionFor(typed));
    }

    [Theory]
    [InlineData("don't")]
    [InlineData("London")]
    [InlineData("I'm")]
    public void Words_already_in_their_written_form_are_left_alone(string typed)
    {
        Assert.Null(EnglishForms.CorrectionFor(typed));
    }

    [Fact]
    public void A_capital_the_user_typed_is_never_taken_away()
    {
        Assert.Equal("LONDON", EnglishForms.ToWritten("LONDON"));
        Assert.Null(EnglishForms.CorrectionFor("LONDON"));
    }

    [Fact]
    public void Phrases_are_written_word_by_word()
    {
        Assert.Equal("I am going to London", EnglishForms.ToWrittenPhrase("i am going to london"));
    }

    [Theory]
    [InlineData("don")]
    [InlineData("dont")]
    [InlineData("don'")]
    public void A_contraction_is_offered_from_any_start_of_it(string typed)
    {
        Assert.Contains(EnglishForms.ContractionCandidates(typed), s => s.Word == "don't");
    }

    [Fact]
    public void An_apostrophe_typed_in_a_different_place_rules_a_contraction_out()
    {
        Assert.DoesNotContain(EnglishForms.ContractionCandidates("do'"), s => s.Word == "don't");
    }

    [Fact]
    public void A_contraction_whose_letters_were_typed_in_full_counts_as_the_exact_word()
    {
        var cant = Assert.Single(EnglishForms.ContractionCandidates("cant"), s => s.Word == "can't");
        Assert.Equal(SuggestionSource.ExactWord, cant.Source);
    }

    // --- Through the engine -----------------------------------------------------------------------------

    [Fact]
    public void The_strip_shows_I_not_i()
    {
        var engine = InteractionTestEngine.Build();

        var suggestions = engine.GetLiveSuggestions("i", 4, PredictionContext.Empty, includeEmoji: false);

        Assert.Contains(suggestions, s => s.Word == "I");
        Assert.DoesNotContain(suggestions, s => s.Word == "i");
    }

    [Fact]
    public void The_strip_shows_proper_nouns_capitalised()
    {
        var engine = InteractionTestEngine.Build();

        var suggestions = engine.GetLiveSuggestions("lond", 4, PredictionContext.Empty, includeEmoji: false);

        Assert.Equal("London", suggestions[0].Word);
    }

    [Fact]
    public void The_strip_offers_contractions_the_dictionary_does_not_have()
    {
        var engine = InteractionTestEngine.Build();

        var suggestions = engine.GetLiveSuggestions("don", 4, PredictionContext.Empty, includeEmoji: false);

        Assert.Contains(suggestions, s => s.Word == "don't");
    }

    [Fact]
    public void Contractions_and_proper_nouns_count_as_correctly_spelled()
    {
        var engine = InteractionTestEngine.Build();

        Assert.True(engine.IsCorrectlySpelled("don't"));
        Assert.True(engine.IsCorrectlySpelled("London"));
    }
}
