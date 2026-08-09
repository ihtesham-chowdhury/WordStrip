using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// Personal learning: what gets recorded, what it changes, and — the part that matters most for a feature
/// that watches someone type — what it refuses to keep.
/// </summary>
public sealed class PersonalLearningTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public PersonalLearningTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "wordstrip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "personal-language-model.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private PersonalLanguageModel NewModel() => new(_path);

    /// <summary>Feeds a sentence in the same shape the controller does: each word with the two before it.</summary>
    private static void Teach(PersonalLanguageModel model, string sentence, int times = 1)
    {
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var repeat = 0; repeat < times; repeat++)
        {
            for (var i = 0; i < words.Length; i++)
            {
                var preceding = words.Take(i).TakeLast(2).ToArray();
                model.Learn(words[i], preceding);
            }
        }
    }

    // --- What gets counted ----------------------------------------------------------------------------

    [Fact]
    public void Words_are_counted()
    {
        var model = NewModel();

        Teach(model, "northfield data systems");

        Assert.Equal(1, model.GetUnigramCount("british"));
        Assert.Equal(1, model.GetUnigramCount("northfield"));
    }

    [Fact]
    public void Pairs_are_counted()
    {
        var model = NewModel();

        Teach(model, "northfield data systems", times: 3);

        Assert.Equal(3, model.GetBigramCount("british", "council"));
        Assert.Equal(3, model.GetBigramCount("council", "northfield"));
    }

    [Fact]
    public void Triples_are_counted()
    {
        var model = NewModel();

        Teach(model, "northfield data systems", times: 2);

        Assert.Equal(2, model.GetTrigramCount("british", "council", "northfield"));
    }

    [Fact]
    public void Counting_ignores_capitalisation()
    {
        var model = NewModel();

        Teach(model, "British Council");
        Teach(model, "british council");

        Assert.Equal(2, model.GetBigramCount("BRITISH", "COUNCIL"));
    }

    [Fact]
    public void Single_letters_are_not_learned()
    {
        var model = NewModel();

        Teach(model, "a b c");

        // Almost every sentence contains them and none of them says anything useful about what comes next.
        Assert.Equal(0, model.WordsLearned);
    }

    [Fact]
    public void Nothing_is_learned_from_an_empty_or_punctuation_only_token()
    {
        var model = NewModel();

        model.Learn("", Array.Empty<string>());
        model.Learn("!!!", Array.Empty<string>());

        Assert.Equal(0, model.WordsLearned);
    }

    // --- What it predicts -----------------------------------------------------------------------------

    [Fact]
    public void A_repeated_phrase_becomes_a_prediction()
    {
        var model = NewModel();

        Teach(model, "thank you for your support", times: 60);

        var score = model.GetPersonalScore("support", new[] { "for", "your" });

        Assert.True(score > 0, "the model should predict a phrase it has seen sixty times");
    }

    [Fact]
    public void A_word_never_typed_scores_nothing()
    {
        var model = NewModel();
        Teach(model, "northfield data systems", times: 50);

        Assert.Equal(0, model.GetPersonalScore("aardvark", new[] { "british", "council" }));
    }

    [Fact]
    public void A_model_that_has_learned_nothing_scores_nothing()
    {
        Assert.Equal(0, NewModel().GetPersonalScore("anything", Array.Empty<string>()));
    }

    [Fact]
    public void The_longest_matching_context_is_used()
    {
        var model = NewModel();

        // "council northfield" always follows "british"; "council london" never does.
        Teach(model, "northfield data systems", times: 100);
        Teach(model, "the council london", times: 100);

        var afterBritishCouncil = model.GetPersonalScore("northfield", new[] { "british", "council" });
        var afterTheCouncil = model.GetPersonalScore("northfield", new[] { "the", "council" });

        Assert.True(afterBritishCouncil > afterTheCouncil,
            $"trigram context {afterBritishCouncil} should beat a context that never precedes it {afterTheCouncil}");
    }

    // --- Cold start -----------------------------------------------------------------------------------

    [Fact]
    public void A_brand_new_model_has_no_confidence()
    {
        Assert.Equal(0, NewModel().Confidence);
    }

    [Fact]
    public void Confidence_grows_with_evidence()
    {
        var model = NewModel();
        Teach(model, "thank you for your support", times: 10);
        var early = model.Confidence;

        Teach(model, "thank you for your support", times: 200);
        var later = model.Confidence;

        Assert.True(early > 0 && early < 1, $"early confidence {early} should be partial");
        Assert.True(later > early, $"confidence should grow: {early} then {later}");
    }

    [Fact]
    public void Confidence_never_exceeds_one()
    {
        var model = NewModel();
        Teach(model, "thank you for your support", times: 2000);

        Assert.Equal(1, model.Confidence);
    }

    [Fact]
    public void Early_evidence_is_damped_rather_than_trusted_outright()
    {
        var sparse = NewModel();
        Teach(sparse, "thank you for your support", times: 2);

        // The phrase is 100% of what this model has ever seen, but two sentences is not grounds for
        // rearranging someone's suggestions.
        Assert.True(sparse.GetPersonalScore("support", new[] { "for", "your" }) < 0.2);
    }

    // --- Bounded growth and forgetting ----------------------------------------------------------------

    [Fact]
    public void A_single_count_cannot_grow_without_limit()
    {
        var model = NewModel();

        for (var i = 0; i < PersonalLanguageModel.MaxCount + 500; i++)
            model.Learn("northfield", Array.Empty<string>());

        Assert.Equal(PersonalLanguageModel.MaxCount, model.GetUnigramCount("northfield"));
    }

    [Fact]
    public void Counts_decay_so_old_evidence_fades()
    {
        var model = NewModel();
        Teach(model, "thank you", times: 100);
        var before = model.GetUnigramCount("thank");

        // Push past the decay interval with unrelated typing.
        for (var i = 0; i < PersonalLanguageModel.DecayIntervalWords; i++)
            model.Learn("filler", Array.Empty<string>());

        Assert.True(model.GetUnigramCount("thank") < before,
            $"count should have decayed from {before}");
    }

    [Fact]
    public void Storage_stays_bounded_under_a_lot_of_typing()
    {
        var model = NewModel();

        // Far more distinct words than the cap allows.
        for (var i = 0; i < PersonalLanguageModel.MaxEntriesPerOrder * 2; i++)
            model.Learn($"word{i}", Array.Empty<string>());

        Assert.True(model.UnigramCount <= PersonalLanguageModel.MaxEntriesPerOrder,
            $"unigram table grew to {model.UnigramCount}");
    }

    [Fact]
    public void Pruning_keeps_the_words_actually_used()
    {
        var model = NewModel();

        // Something typed constantly, buried under a flood of one-offs.
        for (var i = 0; i < 200; i++) model.Learn("northfield", Array.Empty<string>());
        for (var i = 0; i < PersonalLanguageModel.MaxEntriesPerOrder + 100; i++)
            model.Learn($"noise{i}", Array.Empty<string>());

        Assert.True(model.GetUnigramCount("northfield") > 0, "a frequently used word should survive pruning");
    }

    // --- Persistence and privacy ----------------------------------------------------------------------

    [Fact]
    public void What_was_learned_survives_a_restart()
    {
        var first = NewModel();
        Teach(first, "northfield data systems", times: 5);
        first.SaveIfDirty();

        var second = NewModel();
        second.Load();

        Assert.Equal(5, second.GetBigramCount("british", "council"));
        Assert.Equal(first.WordsLearned, second.WordsLearned);
    }

    [Fact]
    public void Saving_does_nothing_when_nothing_changed()
    {
        var model = NewModel();
        Teach(model, "thank you");
        model.SaveIfDirty();

        Assert.False(model.HasUnsavedChanges);
    }

    [Fact]
    public void A_corrupt_model_file_loads_as_empty()
    {
        File.WriteAllText(_path, "not json ][");

        var model = NewModel();
        model.Load();

        Assert.Equal(0, model.WordsLearned);
    }

    [Fact]
    public void Clearing_forgets_everything_in_memory()
    {
        var model = NewModel();
        Teach(model, "northfield data systems", times: 10);

        model.Clear();

        Assert.Equal(0, model.WordsLearned);
        Assert.Equal(0, model.GetBigramCount("british", "council"));
        Assert.Equal(0, model.GetPersonalScore("northfield", new[] { "british", "council" }));
    }

    [Fact]
    public void Clearing_leaves_nothing_behind_on_disk()
    {
        var model = NewModel();
        Teach(model, "northfield data systems", times: 10);
        model.SaveIfDirty();
        Assert.True(File.Exists(_path));

        model.Clear();

        // "Delete my data" should not leave a tidy record that there used to be data.
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void The_stored_file_holds_counts_and_not_sentences()
    {
        var model = NewModel();
        Teach(model, "my password is hunter two", times: 3);
        model.SaveIfDirty();

        var contents = File.ReadAllText(_path);

        // Individual words and pairs are the whole point and will be present. What must not be is the
        // sentence itself, in order, recoverable.
        Assert.DoesNotContain("my password is hunter two", contents, StringComparison.OrdinalIgnoreCase);
    }

    // --- Ranking influence ----------------------------------------------------------------------------

    [Fact]
    public void Learning_shifts_ranking_towards_how_the_user_writes()
    {
        var model = NewModel();
        Teach(model, "the working world", times: 400);

        var dictionary = TestVocabulary.BuildDictionary();
        var ranker = new ContextualRanker(
            WordStrip.Core.Prediction.NGram.NGramLanguageModel.Empty(dictionary),
            personalVocabulary: null,
            personalLearning: model);

        var context = new PredictionContext(string.Empty, new[] { "the", "working" });

        Assert.True(ranker.LearnedBonus("world", context) > 0,
            "a phrase typed four hundred times should influence ranking");
    }

    [Fact]
    public void The_personal_boost_is_bounded()
    {
        var model = NewModel();
        Teach(model, "northfield northfield", times: 5000);

        var dictionary = TestVocabulary.BuildDictionary();
        var ranker = new ContextualRanker(
            WordStrip.Core.Prediction.NGram.NGramLanguageModel.Empty(dictionary),
            personalVocabulary: null,
            personalLearning: model);

        var bonus = ranker.LearnedBonus("northfield", PredictionContext.After("northfield"));

        Assert.InRange(bonus, 0, ContextualRanker.MaxLearnedBonus);
    }

    [Fact]
    public void A_ranker_with_no_personal_model_is_unaffected()
    {
        var dictionary = TestVocabulary.BuildDictionary();
        var ranker = new ContextualRanker(WordStrip.Core.Prediction.NGram.NGramLanguageModel.Empty(dictionary));

        Assert.Equal(0, ranker.LearnedBonus("world", PredictionContext.After("the", "working")));
    }

    [Fact]
    public void Ranking_stays_deterministic_with_learning_on()
    {
        var model = NewModel();
        Teach(model, "the working world", times: 400);

        var dictionary = TestVocabulary.BuildDictionary();
        var engine = new PredictionEngine(
            dictionary, SymSpellIndex.Build(dictionary, 2), personalLearning: model);

        var context = new PredictionContext("wor", new[] { "the", "working" });
        var first = engine.GetLiveSuggestions("wor", 5, context).Select(s => s.Word).ToList();

        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(first, engine.GetLiveSuggestions("wor", 5, context).Select(s => s.Word));
    }
}
