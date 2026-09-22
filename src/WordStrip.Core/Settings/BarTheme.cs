namespace WordStrip.Core.Settings;

/// <summary>
/// The bar's visual personality: six of them, each a different material philosophy over the same component,
/// geometry system and interaction model. The strip behaves identically in all of them.
///
/// <para><b>The numbers are a storage format and must never be reused.</b> The theme is persisted to
/// settings.json as an integer, so a value that once meant one thing cannot later mean another — a user who
/// chose Mica would silently get whatever took its place. The three merged themes therefore keep their old
/// numbers as <c>Legacy…</c> members, and <see cref="AppSettingsStore"/> maps them to their successors on
/// load. New themes take new numbers.</para>
/// </summary>
public enum BarTheme
{
    /// <summary>Windows 11, refined: restrained translucency, tonal selection, quiet elevation. The default.</summary>
    FluentSurface = 0,

    /// <summary>Was "Mica-inspired". Merged into <see cref="FluentSurface"/>, whose restraint it contributed.</summary>
    LegacyMica = 1,

    /// <summary>Dark, matte and compact: a fast instrument for people who write for hours.</summary>
    Command = 2,

    /// <summary>Light, luminous and spacious, floating slightly above the page.</summary>
    SpatialGlass = 3,

    /// <summary>Was "Raycast Floating". Merged into <see cref="Command"/>, whose density it contributed.</summary>
    LegacyRaycast = 4,

    /// <summary>Was "visionOS-inspired". Merged into <see cref="SpatialGlass"/>, whose luminosity it contributed.</summary>
    LegacyVision = 5,

    /// <summary>Tonal and colour-aware, with a filled selection container. The most expressive theme.</summary>
    MaterialYou = 6,

    /// <summary>Warm, opaque and editorial. No glass at all — typography carries it.</summary>
    PaperInk = 7,

    /// <summary>OLED-black, monospaced and instant, with a terminal's block cursor.</summary>
    TerminalMono = 8,

    /// <summary>No surface at all: candidates on the page, with a lit rail beneath them tracking the selection.</summary>
    PrismRail = 9,

    /// <summary>Ivory, ruled and square, with a heavy ink underline. Flat by conviction.</summary>
    Editorial = 10,
}
