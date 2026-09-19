namespace WordStrip.Core.Prediction;

/// <summary>
/// How English words are written, as opposed to which words they are: "I" and never "i", "don't" rather than
/// "dont", "London" rather than "london". The dictionary and the language model are lower-case and
/// apostrophe-free by construction, so without this every suggestion arrives in a form nobody would write.
///
/// <para><b>Three uses, one table.</b> The suggestion strip shows every candidate in its written form.
/// Contractions become candidates in their own right, so "don" offers "don't". And a finished word is
/// corrected to its written form — but only from the <see cref="SafeCorrections"/> subset, because that is
/// the one use that changes text the user did not choose from the strip.</para>
///
/// <para><b>Ambiguity is the whole design problem, and it is resolved by leaving words out.</b> "ill" is a
/// word and so is "I'll"; "were" and "we're", "well" and "we'll", "its" and "it's", "lets" and "let's" — each
/// pair stays a suggestion only, never a correction, because either spelling may be meant. Proper nouns that
/// are also ordinary words are not listed at all: "may", "march", "turkey", "polish", "python", "chile", "windows",
/// "apple", "bath", "nice". ("china" the porcelain is rare enough that China is kept.) A missing capital costs a keystroke; a wrong one
/// costs trust.</para>
/// </summary>
public static class EnglishForms
{
    /// <summary>Contractions and how common they are, on the dictionary's frequency scale.</summary>
    private static readonly (string Written, long Frequency)[] ContractionTable =
    {
        ("I'm", 900_000_000), ("I've", 250_000_000), ("I'll", 250_000_000), ("I'd", 150_000_000),
        ("don't", 900_000_000), ("doesn't", 300_000_000), ("didn't", 400_000_000),
        ("can't", 450_000_000), ("won't", 250_000_000), ("isn't", 250_000_000), ("aren't", 100_000_000),
        ("wasn't", 180_000_000), ("weren't", 60_000_000), ("hasn't", 50_000_000), ("haven't", 150_000_000),
        ("hadn't", 40_000_000), ("wouldn't", 150_000_000), ("couldn't", 150_000_000),
        ("shouldn't", 60_000_000), ("mustn't", 5_000_000), ("needn't", 3_000_000),
        ("it's", 1_200_000_000), ("that's", 500_000_000), ("what's", 180_000_000), ("there's", 250_000_000),
        ("here's", 60_000_000), ("where's", 25_000_000), ("who's", 40_000_000), ("how's", 15_000_000),
        ("let's", 200_000_000), ("he's", 180_000_000), ("she's", 100_000_000),
        ("you're", 300_000_000), ("we're", 180_000_000), ("they're", 180_000_000),
        ("you've", 100_000_000), ("we've", 90_000_000), ("they've", 60_000_000),
        ("you'll", 150_000_000), ("we'll", 100_000_000), ("they'll", 60_000_000), ("it'll", 30_000_000),
        ("you'd", 50_000_000), ("we'd", 25_000_000), ("they'd", 20_000_000), ("he'd", 30_000_000),
        ("she'd", 15_000_000), ("would've", 15_000_000), ("could've", 15_000_000), ("should've", 15_000_000),
        ("ain't", 30_000_000), ("y'all", 15_000_000),
    };

    /// <summary>
    /// Typed forms that are corrected to their written form when the word is finished. Only spellings that
    /// are not themselves words: "dont" and "im" are never meant as typed, "ill" and "were" often are.
    /// </summary>
    private static readonly Dictionary<string, string> SafeCorrections = new(StringComparer.Ordinal)
    {
        ["i"] = "I", ["im"] = "I'm", ["ive"] = "I've",
        ["i'm"] = "I'm", ["i've"] = "I've", ["i'll"] = "I'll", ["i'd"] = "I'd",
        ["dont"] = "don't", ["doesnt"] = "doesn't", ["didnt"] = "didn't", ["cant"] = "can't", ["wont"] = "won't",
        ["isnt"] = "isn't", ["arent"] = "aren't", ["wasnt"] = "wasn't", ["werent"] = "weren't",
        ["hasnt"] = "hasn't", ["havent"] = "haven't", ["hadnt"] = "hadn't", ["wouldnt"] = "wouldn't",
        ["couldnt"] = "couldn't", ["shouldnt"] = "shouldn't", ["mustnt"] = "mustn't",
        ["thats"] = "that's", ["whats"] = "what's", ["theres"] = "there's", ["heres"] = "here's",
        ["wheres"] = "where's", ["whos"] = "who's", ["hows"] = "how's", ["shes"] = "she's",
        ["youre"] = "you're", ["theyre"] = "they're", ["youve"] = "you've", ["weve"] = "we've",
        ["theyve"] = "they've", ["youll"] = "you'll", ["theyll"] = "they'll", ["youd"] = "you'd",
        ["theyd"] = "they'd", ["wouldve"] = "would've", ["couldve"] = "could've", ["shouldve"] = "should've",
        ["aint"] = "ain't", ["yall"] = "y'all",
    };

    /// <summary>Words that are capitalised wherever they appear, and only those that are never ordinary words.</summary>
    private static readonly string[] ProperNounList =
    {
        // Days and months. "May" and "March" are ordinary words too, and are left out.
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "January", "February", "April", "June", "July", "August", "September", "October", "November", "December",

        // Continents and regions.
        "Africa", "Asia", "Europe", "America", "Americas", "Australia", "Antarctica", "Arctic", "Atlantic",
        "Pacific", "Mediterranean", "Caribbean", "Scandinavia", "Balkans", "Himalayas", "Sahara",

        // Countries. "Turkey", "Chad" and "Guinea" are also ordinary words or names and are left out.
        "Afghanistan", "Albania", "Algeria", "Andorra", "Angola", "Argentina", "Armenia", "Austria", "Azerbaijan",
        "Bahamas", "Bahrain", "Bangladesh", "Barbados", "Belarus", "Belgium", "Belize", "Benin", "Bhutan",
        "Bolivia", "Bosnia", "Botswana", "Brazil", "Brunei", "Bulgaria", "Burundi", "Cambodia", "Cameroon",
        "Canada", "China", "Colombia", "Congo", "Croatia", "Cuba", "Cyprus", "Czechia", "Denmark",
        "Djibouti", "Ecuador", "Egypt", "Eritrea", "Estonia", "Eswatini", "Ethiopia", "Fiji", "Finland", "France",
        "Gabon", "Gambia", "Georgia", "Germany", "Ghana", "Greece", "Grenada", "Guatemala", "Guyana", "Haiti",
        "Honduras", "Hungary", "Iceland", "India", "Indonesia", "Iran", "Iraq", "Ireland", "Israel", "Italy",
        "Jamaica", "Japan", "Jordan", "Kazakhstan", "Kenya", "Kiribati", "Korea", "Kosovo", "Kuwait", "Kyrgyzstan",
        "Laos", "Latvia", "Lebanon", "Lesotho", "Liberia", "Libya", "Liechtenstein", "Lithuania", "Luxembourg",
        "Madagascar", "Malawi", "Malaysia", "Maldives", "Mali", "Malta", "Mauritania", "Mauritius", "Mexico",
        "Moldova", "Monaco", "Mongolia", "Montenegro", "Morocco", "Mozambique", "Myanmar", "Namibia", "Nauru",
        "Nepal", "Netherlands", "Nicaragua", "Niger", "Nigeria", "Norway", "Oman", "Pakistan", "Palestine",
        "Panama", "Paraguay", "Peru", "Philippines", "Poland", "Portugal", "Qatar", "Romania", "Russia", "Rwanda",
        "Samoa", "Senegal", "Serbia", "Seychelles", "Singapore", "Slovakia", "Slovenia", "Somalia", "Spain",
        "Sudan", "Suriname", "Sweden", "Switzerland", "Syria", "Taiwan", "Tajikistan", "Tanzania", "Thailand",
        "Togo", "Tonga", "Tunisia", "Turkmenistan", "Tuvalu", "Uganda", "Ukraine", "Uruguay", "Uzbekistan",
        "Vanuatu", "Vatican", "Venezuela", "Vietnam", "Yemen", "Zambia", "Zimbabwe", "England", "Scotland",
        "Wales", "Britain",

        // Major cities. Place names that are also ordinary words (Reading, Nice, Bath, Mobile) are left out.
        "London", "Paris", "Berlin", "Madrid", "Rome", "Moscow", "Tokyo", "Beijing", "Shanghai", "Delhi",
        "Mumbai", "Kolkata", "Chennai", "Bangalore", "Dhaka", "Chittagong", "Karachi", "Lahore", "Islamabad",
        "Kathmandu", "Colombo", "Bangkok", "Jakarta", "Manila", "Seoul", "Hanoi", "Dubai", "Doha", "Riyadh",
        "Jeddah", "Mecca", "Makkah", "Medina", "Tehran", "Baghdad", "Istanbul", "Ankara", "Cairo", "Nairobi",
        "Lagos", "Johannesburg", "Toronto", "Vancouver", "Montreal", "Sydney", "Melbourne", "Auckland",
        "Chicago", "Boston", "Seattle", "Houston", "Dallas", "Atlanta", "Miami", "Washington", "Manchester",
        "Birmingham", "Liverpool", "Leeds", "Glasgow", "Edinburgh", "Dublin", "Amsterdam", "Brussels", "Vienna",
        "Prague", "Warsaw", "Budapest", "Athens", "Lisbon", "Barcelona", "Milan", "Munich", "Hamburg", "Zurich",
        "Geneva", "Stockholm", "Oslo", "Copenhagen", "Helsinki", "Kabul", "Tashkent", "Singapore",

        // Languages, nationalities and peoples.
        "English", "American", "British", "French", "German", "Spanish", "Italian", "Portuguese", "Dutch",
        "Russian", "Chinese", "Japanese", "Korean", "Arabic", "Arab", "Persian", "Turkish", "Indian", "Hindi",
        "Urdu", "Bengali", "Bangla", "Bangladeshi", "Pakistani", "Nepali", "Tamil", "Punjabi", "Gujarati",
        "African", "Asian", "European", "Australian", "Canadian", "Mexican", "Brazilian", "Egyptian", "Iranian",
        "Iraqi", "Israeli", "Palestinian", "Saudi", "Emirati", "Scottish", "Welsh", "Irish", "Greek", "Swedish",
        "Norwegian", "Danish", "Finnish", "Ukrainian", "Latin", "Hebrew", "Swahili", "Malay",
        "Filipino", "Thai", "Vietnamese", "Indonesian", "Nigerian", "Kenyan",

        // Faiths, texts and festivals.
        "Muslim", "Muslims", "Islam", "Islamic", "Christian", "Christians", "Christianity", "Hindu", "Hindus",
        "Hinduism", "Jewish", "Judaism", "Buddhist", "Buddhism", "Sikh", "Quran", "Koran", "Bible", "Torah",
        "Allah", "Ramadan", "Eid", "Christmas", "Easter", "Diwali", "Hanukkah", "Thanksgiving",

        // Organisations and products people type constantly. Single-word forms only.
        "Google", "Microsoft", "Facebook", "YouTube", "Instagram", "WhatsApp", "LinkedIn", "Twitter", "TikTok",
        "Netflix", "Amazon", "GitHub", "Gmail", "PayPal", "iPhone", "iPad", "iOS", "macOS", "Android", "Linux",
        "ChatGPT", "OpenAI", "Anthropic", "Claude", "Wikipedia", "Spotify", "Skype", "Uber",
        "Samsung", "Huawei", "Xiaomi", "Toyota", "Tesla", "Photoshop", "PowerPoint", "JavaScript",
        "Bluetooth",

        // Acronyms written in capitals.
        "USA", "UK", "UAE", "EU", "NASA", "NATO", "UNICEF", "UNESCO", "FBI", "CIA", "CEO", "CFO", "CTO", "PDF",
        "GPS", "DNA", "FAQ", "HTML", "CSS", "USB", "PhD", "MBA", "TV",
    };

    private static readonly Dictionary<string, string> Written = BuildWritten();

    private static readonly Dictionary<string, (string Written, long Frequency)> ContractionsByLetters =
        ContractionTable.ToDictionary(c => LettersOnly(c.Written), c => c, StringComparer.Ordinal);

    private static Dictionary<string, string> BuildWritten()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["i"] = "I" };
        foreach (var noun in ProperNounList) map.TryAdd(noun.ToLowerInvariant(), noun);
        foreach (var (written, _) in ContractionTable) map.TryAdd(written.ToLowerInvariant(), written);
        return map;
    }

    /// <summary>
    /// The written form of a single word, or the word unchanged. Only rewrites text that is all lower-case —
    /// a capital the user or the dictionary already put there is theirs.
    /// </summary>
    public static string ToWritten(string word)
    {
        if (string.IsNullOrEmpty(word) || !IsAllLower(word)) return word;
        return Written.TryGetValue(word, out var written) ? written : word;
    }

    /// <summary>The written form of every word in a phrase ("i am" becomes "I am").</summary>
    public static string ToWrittenPhrase(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(' ') < 0) return ToWritten(text);
        return string.Join(' ', text.Split(' ').Select(ToWritten));
    }

    /// <summary>Whether the word is a recognised written form — a contraction or a listed proper noun — in any case.</summary>
    public static bool IsKnownForm(string word) =>
        !string.IsNullOrEmpty(word) && Written.ContainsKey(word.ToLowerInvariant());

    /// <summary>
    /// The correction for a finished word, or null. The safe contraction list first, then proper nouns;
    /// never anything ambiguous. Typed capitals are respected: "Dont" becomes "Don't".
    /// </summary>
    public static string? CorrectionFor(string typed)
    {
        if (string.IsNullOrEmpty(typed)) return null;

        var lower = typed.ToLowerInvariant();
        string? written = null;

        if (SafeCorrections.TryGetValue(lower, out var contraction)) written = contraction;
        else if (IsAllLower(typed) && Written.TryGetValue(lower, out var proper) && !ContractionsByLetters.ContainsKey(LettersOnly(proper)))
            written = proper;

        if (written is null) return null;

        // Keep a capital the user typed ("Dont" -> "Don't"); the written form supplies any others.
        if (char.IsUpper(typed[0]) && char.IsLower(written[0]))
            written = char.ToUpperInvariant(written[0]) + written[1..];

        return string.Equals(written, typed, StringComparison.Ordinal) ? null : written;
    }

    /// <summary>
    /// Contractions to offer while <paramref name="typed"/> is in progress: those it begins, apostrophe aside,
    /// so "dont", "don" and "don'" all reach "don't". A contraction whose letters are exactly what was typed is
    /// marked as the exact word, so it competes with — and usually beats — a rare dictionary homograph such
    /// as "cant".
    /// </summary>
    public static IEnumerable<Suggestion> ContractionCandidates(string typed)
    {
        if (string.IsNullOrEmpty(typed)) yield break;

        var lower = typed.ToLowerInvariant();
        var letters = LettersOnly(lower);
        if (letters.Length == 0) yield break;

        foreach (var (key, entry) in ContractionsByLetters)
        {
            if (!key.StartsWith(letters, StringComparison.Ordinal)) continue;

            // If the user typed the apostrophe, it has to be in the same place.
            if (lower.Contains('\'') && !entry.Written.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;

            var exact = key.Length == letters.Length;
            yield return new Suggestion(
                entry.Written, entry.Frequency, EditDistance: 0,
                exact ? SuggestionSource.ExactWord : SuggestionSource.PrefixCompletion);
        }
    }

    public static string Capitalize(string word) =>
        string.IsNullOrEmpty(word) || !char.IsLower(word[0]) ? word : char.ToUpperInvariant(word[0]) + word[1..];

    private static bool IsAllLower(string word)
    {
        foreach (var c in word)
        {
            if (char.IsUpper(c)) return false;
        }

        return true;
    }

    private static string LettersOnly(string word)
    {
        var chars = new char[word.Length];
        var n = 0;
        foreach (var c in word)
        {
            if (char.IsLetter(c)) chars[n++] = char.ToLowerInvariant(c);
        }

        return new string(chars, 0, n);
    }
}
