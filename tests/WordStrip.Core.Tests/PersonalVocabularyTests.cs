using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// The personal vocabulary store, and what happens to a personal word once prediction gets hold of it.
///
/// <para>Each test gets a real file in a temporary directory rather than an in-memory fake. Persistence is
/// most of what this class does, and the interesting failures — a corrupt file, a half-written save, a
/// hand-edited entry — only exist on disk.</para>
/// </summary>
public sealed class PersonalVocabularyTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public PersonalVocabularyTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "wordstrip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "personal-vocabulary.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private PersonalVocabularyStore NewStore() => new(_path);

    // --- Adding and removing --------------------------------------------------------------------------

    [Fact]
    public void A_word_can_be_added_and_found()
    {
        var store = NewStore();

        Assert.True(store.Add("QNAP"));

        Assert.True(store.Contains("QNAP"));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void A_word_is_found_however_it_is_capitalised()
    {
        var store = NewStore();
        store.Add("QNAP");

        Assert.True(store.Contains("qnap"));
        Assert.True(store.Contains("QNap"));
        Assert.True(store.Contains("Qnap"));
    }

    [Fact]
    public void Adding_the_same_word_twice_does_not_create_a_second_entry()
    {
        var store = NewStore();

        store.Add("GitHub");
        store.Add("github");
        store.Add("GITHUB");

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void The_casing_first_chosen_is_the_casing_kept()
    {
        var store = NewStore();

        store.Add("GitHub");
        store.Add("github");   // e.g. the same word typed at the start of a sentence later

        // The deliberate choice wins over whatever happened to be typed afterwards.
        Assert.Equal("GitHub", store.GetDisplayForm("github"));
    }

    [Fact]
    public void Display_casing_can_be_corrected_afterwards()
    {
        var store = NewStore();
        store.Add("qnap");

        Assert.True(store.SetDisplayForm("qnap", "QNAP"));
        Assert.Equal("QNAP", store.GetDisplayForm("qnap"));
    }

    [Fact]
    public void Display_casing_cannot_be_changed_into_a_different_word()
    {
        var store = NewStore();
        store.Add("qnap");

        Assert.False(store.SetDisplayForm("qnap", "Synology"));
        Assert.Equal("qnap", store.GetDisplayForm("qnap"));
    }

    [Fact]
    public void A_word_can_be_removed()
    {
        var store = NewStore();
        store.Add("QNAP");

        Assert.True(store.Remove("qnap"));
        Assert.False(store.Contains("QNAP"));
    }

    [Fact]
    public void Removing_something_that_was_never_there_reports_it()
    {
        Assert.False(NewStore().Remove("nonexistent"));
    }

    [Fact]
    public void Clearing_removes_everything()
    {
        var store = NewStore();
        store.Add("QNAP");
        store.Add("GitHub");

        store.Clear();

        Assert.Equal(0, store.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("!!!")]
    public void Things_that_are_not_words_are_rejected(string input)
    {
        Assert.False(NewStore().Add(input));
    }

    // --- Persistence ----------------------------------------------------------------------------------

    [Fact]
    public void Words_survive_a_restart()
    {
        var first = NewStore();
        first.Add("QNAP");
        first.Add("GitHub");
        first.Save();

        var second = NewStore();
        second.Load();

        Assert.True(second.Contains("qnap"));
        Assert.Equal("GitHub", second.GetDisplayForm("github"));
    }

    [Fact]
    public void An_empty_vocabulary_round_trips()
    {
        var first = NewStore();
        first.Save();

        var second = NewStore();
        second.Load();

        Assert.Equal(0, second.Count);
    }

    [Fact]
    public void A_missing_file_loads_as_empty_rather_than_throwing()
    {
        var store = NewStore();

        store.Load();

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void A_corrupt_file_loads_as_empty_rather_than_taking_the_app_down()
    {
        File.WriteAllText(_path, "{ this is not json at all ][");

        var store = NewStore();
        store.Load();

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void A_corrupt_file_is_left_on_disk_for_recovery()
    {
        File.WriteAllText(_path, "{ broken ][");

        NewStore().Load();

        // Overwriting it with an empty vocabulary would destroy whatever could still be salvaged by hand.
        Assert.Contains("broken", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_edited_file_is_re_normalized_on_load()
    {
        // The file is plain JSON precisely so people can edit it, which means the keys in it cannot be
        // trusted to already be in lookup form.
        File.WriteAllText(_path, """
            { "Version": 1, "Words": [ { "Key": "QNAP", "Display": "QNAP", "Frequency": 3 } ] }
            """);

        var store = NewStore();
        store.Load();

        Assert.True(store.Contains("qnap"));
        Assert.Equal("QNAP", store.GetDisplayForm("qnap"));
    }

    [Fact]
    public void A_save_interrupted_partway_cannot_corrupt_the_existing_file()
    {
        var store = NewStore();
        store.Add("QNAP");
        store.Save();

        // A leftover temporary file is what an interrupted write looks like on disk. The real file must be
        // untouched by it.
        File.WriteAllText(_path + ".tmp", "half-written garbage");

        var reloaded = NewStore();
        reloaded.Load();

        Assert.True(reloaded.Contains("qnap"));
    }

    // --- Prefix lookup --------------------------------------------------------------------------------

    [Fact]
    public void Prefix_matching_returns_the_display_form()
    {
        var store = NewStore();
        store.Add("GitHub");

        var matches = store.FindByPrefix("git", 5);

        Assert.Equal("GitHub", Assert.Single(matches).Display);
    }

    [Fact]
    public void Prefix_matching_puts_the_most_used_word_first()
    {
        var store = NewStore();
        store.Add("Claude");
        store.Add("Clarity");
        for (var i = 0; i < 5; i++) store.Add("Clarity", incrementFrequency: true);

        var matches = store.FindByPrefix("cla", 5);

        Assert.Equal("Clarity", matches[0].Display);
    }

    [Fact]
    public void An_empty_prefix_matches_nothing()
    {
        var store = NewStore();
        store.Add("QNAP");

        Assert.Empty(store.FindByPrefix("", 5));
    }

    // --- Bounded growth -------------------------------------------------------------------------------

    [Fact]
    public void Adding_a_word_by_hand_does_not_inflate_its_usage_count()
    {
        var store = NewStore();

        store.Add("QNAP");
        store.Add("QNAP");
        store.Add("QNAP");

        // An explicit add says the word exists; it is not evidence about how often it gets typed.
        Assert.Equal(1, store.GetFrequency("qnap"));
    }

    [Fact]
    public void Learning_a_word_again_does_increase_its_usage_count()
    {
        var store = NewStore();

        store.Add("QNAP", incrementFrequency: true);
        store.Add("QNAP", incrementFrequency: true);

        Assert.Equal(2, store.GetFrequency("qnap"));
    }

    // --- Import and export ----------------------------------------------------------------------------

    [Fact]
    public void Exported_words_can_be_imported_again()
    {
        var source = NewStore();
        source.Add("QNAP");
        source.Add("GitHub");

        var exportPath = Path.Combine(_directory, "export.txt");
        source.ExportTo(exportPath);

        var destination = new PersonalVocabularyStore(Path.Combine(_directory, "other.json"));
        var added = destination.ImportFrom(exportPath);

        Assert.Equal(2, added);
        Assert.Equal("QNAP", destination.GetDisplayForm("qnap"));
        Assert.Equal("GitHub", destination.GetDisplayForm("github"));
    }

    [Fact]
    public void An_export_carries_no_usage_data()
    {
        var store = NewStore();
        for (var i = 0; i < 40; i++) store.Add("QNAP", incrementFrequency: true);

        var exportPath = Path.Combine(_directory, "export.txt");
        store.ExportTo(exportPath);

        // How often someone types a word describes their habits. A word list should be a word list.
        var exported = File.ReadAllText(exportPath);
        Assert.DoesNotContain("40", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("Frequency", exported, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Importing_skips_comments_and_duplicates()
    {
        var importPath = Path.Combine(_directory, "import.txt");
        File.WriteAllLines(importPath, new[] { "# a comment", "", "QNAP", "qnap", "GitHub" });

        var store = NewStore();
        var added = store.ImportFrom(importPath);

        Assert.Equal(2, added);
    }

    [Fact]
    public void Importing_a_file_that_is_not_there_does_nothing()
    {
        Assert.Equal(0, NewStore().ImportFrom(Path.Combine(_directory, "nope.txt")));
    }

    // --- Integration with prediction ------------------------------------------------------------------

    private PredictionEngine BuildEngine(PersonalVocabularyStore vocabulary)
    {
        var dictionary = TestVocabulary.BuildDictionary();
        return new PredictionEngine(
            dictionary,
            SymSpellIndex.Build(dictionary, maxEditDistance: 2),
            personalVocabulary: vocabulary);
    }

    [Fact]
    public void A_personal_word_absent_from_the_dictionary_is_still_offered()
    {
        var vocabulary = NewStore();
        vocabulary.Add("QNAP");
        var engine = BuildEngine(vocabulary);

        var words = engine.GetLiveSuggestions("qn", 5).Select(s => s.Word);

        Assert.Contains("QNAP", words);
    }

    [Fact]
    public void A_personal_word_is_offered_with_its_own_capitalisation()
    {
        var vocabulary = NewStore();
        vocabulary.Add("QNAP");
        var engine = BuildEngine(vocabulary);

        var suggestion = engine.GetLiveSuggestions("qn", 5).First(s => string.Equals(s.Word, "QNAP", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("QNAP", suggestion.Word);
    }

    [Fact]
    public void A_personal_word_is_protected_from_autocorrection()
    {
        var vocabulary = NewStore();
        vocabulary.Add("QNAP");
        var engine = BuildEngine(vocabulary);

        // Without protection this is just an unknown word a few edits from something in the dictionary,
        // and the user would watch their NAS get renamed every time they mentioned it.
        Assert.Null(engine.GetAutocorrection("QNAP"));
        Assert.True(engine.IsCorrectlySpelled("qnap"));
    }

    [Fact]
    public void An_unknown_word_that_is_not_personal_is_still_corrected()
    {
        var vocabulary = NewStore();
        var engine = BuildEngine(vocabulary);

        Assert.NotNull(engine.GetAutocorrection("recieve"));
    }

    [Fact]
    public void A_personal_word_does_not_displace_a_word_the_user_has_fully_typed()
    {
        var vocabulary = NewStore();
        vocabulary.Add("Worldwide");
        var engine = BuildEngine(vocabulary);

        // "world" is an exact dictionary match. A personal word merely sharing the prefix must not outrank
        // something the user has literally finished typing.
        Assert.Equal("world", engine.GetLiveSuggestions("world", 5)[0].Word);
    }

    [Fact]
    public void A_personal_word_is_competitive_with_common_words_sharing_its_prefix()
    {
        var vocabulary = NewStore();
        vocabulary.Add("Workflowy");
        var engine = BuildEngine(vocabulary);

        // It carries no corpus frequency at all, so without a deliberate personal signal it would sit below
        // every common "wor..." word and be invisible.
        var words = engine.GetLiveSuggestions("workf", 5).Select(s => s.Word).ToList();

        Assert.Equal("Workflowy", words[0]);
    }

    [Fact]
    public void An_engine_with_no_personal_vocabulary_behaves_exactly_as_before()
    {
        var dictionary = TestVocabulary.BuildDictionary();
        var withoutStore = new PredictionEngine(dictionary, SymSpellIndex.Build(dictionary, 2));
        var withEmptyStore = BuildEngine(NewStore());

        Assert.Equal(
            withoutStore.GetLiveSuggestions("wor", 5).Select(s => s.Word),
            withEmptyStore.GetLiveSuggestions("wor", 5).Select(s => s.Word));
    }
}
