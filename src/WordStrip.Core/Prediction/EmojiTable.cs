namespace WordStrip.Core.Prediction;

/// <summary>
/// The bundled keyword-to-emoji table.
///
/// <para>Hand-picked rather than generated from Unicode CLDR. CLDR carries tens of thousands of keywords
/// including things nobody types at a keyboard, and many emoji share them — the result would be a bar that
/// constantly offers a pictogram instead of a word. These are chosen for words that come up in ordinary
/// writing and map to one obvious symbol.</para>
///
/// <para>Where a keyword could plausibly mean two things it is simply left out. <see cref="EmojiSuggester"/>
/// also refuses ambiguous prefixes, so a near-miss produces nothing rather than a guess.</para>
///
/// <para>British and American spellings are both listed where they differ, because the app has no idea which
/// the user writes.</para>
/// </summary>
internal static class EmojiTable
{
    public static readonly (string Keyword, string Emoji)[] Entries =
    {
        // Reactions and sentiment — the most-used group by a wide margin.
        ("smile", "🙂"), ("smiling", "🙂"), ("happy", "😊"), ("laugh", "😂"), ("laughing", "😂"),
        ("lol", "😂"), ("grin", "😁"), ("wink", "😉"), ("sad", "😢"), ("crying", "😢"),
        ("angry", "😠"), ("love", "❤️"), ("heart", "❤️"), ("hearts", "💕"), ("kiss", "😘"),
        ("cool", "😎"), ("thinking", "🤔"), ("confused", "😕"), ("shocked", "😱"), ("wow", "😮"),
        ("sleepy", "😴"), ("tired", "😩"), ("sick", "🤒"), ("worried", "😟"), ("proud", "🥲"),
        ("excited", "🤩"), ("nervous", "😬"), ("relieved", "😌"), ("bored", "🥱"),

        // Gestures and approval.
        ("thanks", "🙏"), ("thankyou", "🙏"), ("please", "🙏"), ("pray", "🙏"), ("sorry", "🙏"),
        ("clap", "👏"), ("congrats", "🎉"), ("congratulations", "🎉"), ("celebrate", "🎉"),
        ("wave", "👋"), ("hello", "👋"), ("goodbye", "👋"), ("welcome", "🤝"), ("agree", "👍"),
        ("perfect", "👌"), ("okay", "👌"), ("strong", "💪"), ("muscle", "💪"), ("point", "👉"),

        // Status and work.
        ("done", "✅"), ("complete", "✅"), ("completed", "✅"), ("finished", "✅"), ("approved", "✅"),
        ("check", "✔️"), ("tick", "✔️"), ("correct", "✔️"), ("wrong", "❌"), ("cancelled", "❌"),
        ("canceled", "❌"), ("rejected", "❌"), ("warning", "⚠️"), ("caution", "⚠️"), ("important", "❗"),
        ("urgent", "🚨"), ("question", "❓"), ("idea", "💡"), ("note", "📝"), ("notes", "📝"),
        ("meeting", "📅"), ("calendar", "📅"), ("deadline", "⏰"), ("schedule", "🗓️"),
        ("email", "📧"), ("inbox", "📥"), ("attached", "📎"), ("attachment", "📎"), ("document", "📄"),
        ("report", "📊"), ("chart", "📈"), ("budget", "💰"), ("invoice", "🧾"), ("payment", "💳"),
        ("phone", "📞"), ("call", "📞"), ("laptop", "💻"), ("computer", "💻"), ("printer", "🖨️"),
        ("folder", "📁"), ("search", "🔍"), ("settings", "⚙️"), ("lock", "🔒"), ("key", "🔑"),
        ("link", "🔗"), ("pin", "📌"), ("bug", "🐛"), ("rocket", "🚀"), ("launch", "🚀"),
        ("target", "🎯"), ("trophy", "🏆"), ("award", "🏆"), ("star", "⭐"), ("fire", "🔥"),

        // Food and drink.
        ("pizza", "🍕"), ("burger", "🍔"), ("coffee", "☕"), ("tea", "🍵"), ("cake", "🍰"),
        ("birthday", "🎂"), ("beer", "🍺"), ("wine", "🍷"), ("water", "💧"), ("apple", "🍎"),
        ("banana", "🍌"), ("bread", "🍞"), ("rice", "🍚"), ("chicken", "🍗"), ("fish", "🐟"),
        ("egg", "🥚"), ("cheese", "🧀"), ("chocolate", "🍫"), ("cookie", "🍪"), ("icecream", "🍦"),
        ("lunch", "🍽️"), ("dinner", "🍽️"), ("breakfast", "🍳"), ("restaurant", "🍽️"),

        // Travel and places.
        ("plane", "✈️"), ("flight", "✈️"), ("travel", "✈️"), ("train", "🚆"), ("bus", "🚌"),
        ("taxi", "🚕"), ("bicycle", "🚲"), ("boat", "⛵"), ("hotel", "🏨"), ("home", "🏠"),
        ("house", "🏠"), ("office", "🏢"), ("school", "🏫"), ("hospital", "🏥"), ("bank", "🏦"),
        ("shop", "🏪"), ("map", "🗺️"), ("location", "📍"), ("world", "🌍"), ("beach", "🏖️"),
        ("mountain", "⛰️"), ("park", "🌳"),

        // Weather and nature.
        ("sun", "☀️"), ("sunny", "☀️"), ("rain", "🌧️"), ("raining", "🌧️"), ("snow", "❄️"),
        ("storm", "⛈️"), ("cloudy", "☁️"), ("wind", "💨"), ("rainbow", "🌈"), ("moon", "🌙"),
        ("flower", "🌸"), ("tree", "🌳"), ("plant", "🌱"), ("leaf", "🍃"),

        // People and animals.
        ("baby", "👶"), ("family", "👨‍👩‍👧"), ("friend", "🧑‍🤝‍🧑"), ("team", "👥"), ("doctor", "🧑‍⚕️"),
        ("teacher", "🧑‍🏫"), ("dog", "🐶"), ("cat", "🐱"), ("bird", "🐦"), ("horse", "🐴"),
        ("tiger", "🐯"), ("elephant", "🐘"), ("monkey", "🐵"), ("butterfly", "🦋"),

        // Time, events and misc.
        ("time", "🕐"), ("clock", "🕐"), ("today", "📆"), ("tomorrow", "📆"), ("holiday", "🏝️"),
        ("party", "🎉"), ("gift", "🎁"), ("present", "🎁"), ("music", "🎵"), ("song", "🎵"),
        ("game", "🎮"), ("football", "⚽"), ("cricket", "🏏"), ("camera", "📷"), ("photo", "📷"),
        ("video", "🎬"), ("book", "📚"), ("reading", "📖"), ("pen", "🖊️"), ("money", "💰"),
        ("shopping", "🛒"), ("medicine", "💊"), ("car", "🚗"), ("keys", "🔑"), ("light", "💡"),
        ("battery", "🔋"), ("wifi", "📶"), ("signal", "📶"), ("cloud", "☁️"), ("mail", "📬"),
    };
}
