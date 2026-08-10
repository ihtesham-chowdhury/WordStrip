using System.Text.Json;
using System.Text.Json.Serialization;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Personal;

/// <summary>
/// The user's own words — names, products, jargon, abbreviations — that the general 60,000-word dictionary
/// has never heard of. Supplements the dictionary; never replaces it.
///
/// <para><b>Local, and visibly so.</b> One JSON file under <c>%LOCALAPPDATA%\WordStrip</c>. There is no
/// network code in this class or anywhere beneath it, nothing is uploaded, and export happens only when the
/// user explicitly asks for a path to write to. The file is plain text specifically so its contents are
/// something the user can read, edit and delete without this application's help.</para>
///
/// <para><b>Robust against a bad file.</b> A corrupt or unreadable store falls back to empty rather than
/// taking the app down at startup, matching how <see cref="Settings.AppSettingsStore"/> already behaves.
/// Saves are written to a temporary file and moved into place, so an interrupted write cannot leave a
/// half-written vocabulary behind.</para>
///
/// <para>Thread-safe: Phase 4 learns from the keyboard hook thread while the settings window reads and
/// writes on the UI thread.</para>
/// </summary>
public sealed class PersonalVocabularyStore
{
    /// <summary>
    /// Upper bound on entries. A personal vocabulary is names and jargon, not a second dictionary; past a
    /// few thousand words something has gone wrong (most likely Phase 4's learning being fed rubbish), and
    /// an unbounded file would grow forever. When full, the least-used entry makes way.
    /// </summary>
    public const int MaxEntries = 5_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, PersonalWord> _words = new(StringComparer.Ordinal);
    private readonly string _filePath;

    public PersonalVocabularyStore(string? filePath = null)
    {
        _filePath = filePath ?? Settings.UserDataLocation.File("personal-vocabulary.json");
    }

    /// <summary>Where the vocabulary lives on disk. Surfaced so the settings window can show the user exactly what file this is.</summary>
    public string FilePath => _filePath;

    public int Count
    {
        get { lock (_gate) return _words.Count; }
    }

    /// <summary>Raised after any change, so the UI can refresh without polling.</summary>
    public event EventHandler? Changed;

    // --- Queries ------------------------------------------------------------------------------------

    public bool Contains(string word)
    {
        var key = Normalize(word);
        if (key.Length == 0) return false;

        lock (_gate) return _words.ContainsKey(key);
    }

    public int GetFrequency(string word)
    {
        var key = Normalize(word);
        if (key.Length == 0) return 0;

        lock (_gate) return _words.TryGetValue(key, out var entry) ? entry.Frequency : 0;
    }

    /// <summary>The preferred casing for a word, or null if it isn't a personal word. "qnap" gives back "QNAP".</summary>
    public string? GetDisplayForm(string word)
    {
        var key = Normalize(word);
        if (key.Length == 0) return null;

        lock (_gate) return _words.TryGetValue(key, out var entry) ? entry.Display : null;
    }

    public bool TryGet(string word, out PersonalWord entry)
    {
        var key = Normalize(word);
        if (key.Length == 0) { entry = default; return false; }

        lock (_gate) return _words.TryGetValue(key, out entry);
    }

    /// <summary>Every entry, ordered by display form so the settings list and any export are stable.</summary>
    public IReadOnlyList<PersonalWord> GetAll()
    {
        lock (_gate)
        {
            return _words.Values
                .OrderBy(w => w.Display, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.Key, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// Personal words starting with the given prefix, most-used first. The prefix is matched against the
    /// normalized key but the display form is what comes back, so typing "git" can offer "GitHub".
    /// </summary>
    public IReadOnlyList<PersonalWord> FindByPrefix(string prefix, int maxResults)
    {
        var normalized = Normalize(prefix);
        if (normalized.Length == 0 || maxResults <= 0) return Array.Empty<PersonalWord>();

        lock (_gate)
        {
            // A linear scan, unlike the general dictionary's binary-searched index. At a few thousand entries
            // against 60,000 that is the right trade: the scan costs microseconds, and keeping a sorted index
            // in step with a store that Phase 4 mutates on every committed word would cost more than it saves.
            var matches = new List<PersonalWord>();
            foreach (var entry in _words.Values)
            {
                if (entry.Key.StartsWith(normalized, StringComparison.Ordinal))
                    matches.Add(entry);
            }

            matches.Sort(static (a, b) =>
            {
                var byFrequency = b.Frequency.CompareTo(a.Frequency);
                if (byFrequency != 0) return byFrequency;

                var byLength = a.Key.Length.CompareTo(b.Key.Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(a.Key, b.Key);
            });

            return matches.Count <= maxResults ? matches : matches.GetRange(0, maxResults);
        }
    }

    // --- Mutations ----------------------------------------------------------------------------------

    /// <summary>
    /// Adds a word, or records another use of one already known. Returns false for anything that isn't a
    /// usable word.
    /// </summary>
    /// <param name="incrementFrequency">
    /// False when the user is adding a word by hand — an explicit add is a statement that the word exists,
    /// not evidence about how often it gets typed. Phase 4's learning passes true.
    /// </param>
    public bool Add(string word, bool incrementFrequency = false)
    {
        var key = Normalize(word);
        if (key.Length == 0) return false;

        var display = word.Trim();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        lock (_gate)
        {
            if (_words.TryGetValue(key, out var existing))
            {
                _words[key] = existing with
                {
                    // Keep the casing already recorded. The first form the user chose deliberately beats
                    // whatever they happened to type at the start of a sentence.
                    Frequency = incrementFrequency ? existing.Frequency + 1 : existing.Frequency,
                    LastUsedUtc = today,
                };
            }
            else
            {
                if (_words.Count >= MaxEntries && !EvictLeastUsed()) return false;
                _words[key] = new PersonalWord(key, display, Frequency: 1, LastUsedUtc: today);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Replaces the casing shown for a word the user has already added.</summary>
    public bool SetDisplayForm(string word, string display)
    {
        var key = Normalize(word);
        var trimmed = (display ?? string.Empty).Trim();
        if (key.Length == 0 || trimmed.Length == 0) return false;
        if (!string.Equals(Normalize(trimmed), key, StringComparison.Ordinal)) return false;

        lock (_gate)
        {
            if (!_words.TryGetValue(key, out var existing)) return false;
            _words[key] = existing with { Display = trimmed };
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(string word)
    {
        var key = Normalize(word);
        if (key.Length == 0) return false;

        bool removed;
        lock (_gate) removed = _words.Remove(key);

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>Deletes every personal word. The user-facing "forget everything" action.</summary>
    public void Clear()
    {
        lock (_gate) _words.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the least-used, then least-recently-used entry. Returns false only if there was nothing to drop.</summary>
    private bool EvictLeastUsed()
    {
        var victim = _words.Values
            .OrderBy(w => w.Frequency)
            .ThenBy(w => w.LastUsedUtc ?? DateOnly.MinValue)
            .ThenBy(w => w.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return victim.Key is not null && _words.Remove(victim.Key);
    }

    // --- Persistence --------------------------------------------------------------------------------

    /// <summary>
    /// The lookup form: lower-cased, letters and inner apostrophes only. Shares
    /// <see cref="NGramTokenizer"/> with the prediction layer on purpose — a personal word normalized one way
    /// here and another way there would be added successfully and then never match anything the user typed.
    /// </summary>
    public static string Normalize(string word) => NGramTokenizer.Normalize(word ?? string.Empty);

    private sealed class PersistedFile
    {
        public int Version { get; set; } = 1;
        public List<PersistedWord> Words { get; set; } = new();
    }

    private sealed class PersistedWord
    {
        public string Key { get; set; } = string.Empty;
        public string Display { get; set; } = string.Empty;
        public int Frequency { get; set; } = 1;
        public string? LastUsed { get; set; }
    }

    public void Load()
    {
        var loaded = new Dictionary<string, PersonalWord>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var file = JsonSerializer.Deserialize<PersistedFile>(json, JsonOptions);

                foreach (var word in file?.Words ?? new List<PersistedWord>())
                {
                    // Re-normalize rather than trusting the key on disk: the file is meant to be
                    // hand-editable, so someone may well have typed "QNAP" straight into it.
                    var key = Normalize(string.IsNullOrEmpty(word.Key) ? word.Display : word.Key);
                    if (key.Length == 0) continue;

                    var display = string.IsNullOrWhiteSpace(word.Display) ? key : word.Display.Trim();
                    var lastUsed = DateOnly.TryParse(word.LastUsed, out var parsed) ? parsed : (DateOnly?)null;

                    loaded[key] = new PersonalWord(key, display, Math.Max(1, word.Frequency), lastUsed);
                    if (loaded.Count >= MaxEntries) break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // A damaged vocabulary is not worth failing startup over. Falling back to empty loses personal
            // words, which is bad; refusing to launch loses the whole application, which is worse. The file
            // is left alone rather than overwritten, so it can still be recovered by hand.
            loaded.Clear();
        }

        lock (_gate)
        {
            _words.Clear();
            foreach (var (key, value) in loaded) _words[key] = value;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        List<PersistedWord> snapshot;
        lock (_gate)
        {
            snapshot = _words.Values
                .OrderBy(w => w.Key, StringComparer.Ordinal)   // deterministic: the file diffs cleanly
                .Select(w => new PersistedWord
                {
                    Key = w.Key,
                    Display = w.Display,
                    Frequency = w.Frequency,
                    LastUsed = w.LastUsedUtc?.ToString("yyyy-MM-dd"),
                })
                .ToList();
        }

        WriteAtomically(_filePath, new PersistedFile { Words = snapshot });
    }

    /// <summary>
    /// Writes via a temporary file and then replaces the original, so a crash or a full disk mid-write
    /// cannot leave a truncated vocabulary. Phase 4 saves periodically, which makes an interrupted write a
    /// question of when rather than if.
    /// </summary>
    private static void WriteAtomically(string path, PersistedFile content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(content, JsonOptions));

        if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
        else File.Move(temporary, path);
    }

    // --- Import / export ----------------------------------------------------------------------------

    /// <summary>
    /// Writes the vocabulary as one word per line, most-preferred casing intact.
    ///
    /// <para>Plain text rather than the internal JSON: an export is something to share with another machine
    /// or keep in a note, and it deliberately carries no frequencies or dates. Those describe the user's
    /// typing habits, which is not something that should leave the machine as a side effect of asking for a
    /// word list.</para>
    /// </summary>
    public void ExportTo(string path)
    {
        var lines = new List<string>
        {
            "# WordStrip personal vocabulary",
            "# One word per line. Lines starting with # are ignored.",
        };

        lines.AddRange(GetAll().Select(w => w.Display));
        File.WriteAllLines(path, lines);
    }

    /// <summary>Reads a word-per-line file, adding what it finds. Returns how many were new.</summary>
    public int ImportFrom(string path)
    {
        if (!File.Exists(path)) return 0;

        var added = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            // Tolerate a pasted list with several words on a line.
            foreach (var candidate in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Contains(candidate)) continue;
                if (Add(candidate)) added++;
            }
        }

        if (added > 0) Save();
        return added;
    }
}
