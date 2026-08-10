using WordStrip.Core.Input;

namespace WordStrip.Core.Tests;

/// <summary>
/// The shape of the input batch the injector hands to Windows.
///
/// <para>These exist because of a bug that shipped. Deletions and replacement text were sent as two separate
/// <c>SendInput</c> calls, and Windows only promises that events <em>within a single call</em> reach the
/// target without other input interleaving. In practice the still-draining backspaces ate the front of the
/// text: a personal-vocabulary entry of "Alexandra Fairbourne Reed", accepted after typing "iht", arrived as
/// "exandra Fairbourne Reed".</para>
///
/// <para>It hid for a long time because the common case has nothing to delete — completing "wor" to "world"
/// shares a prefix, so there were no backspaces and only ever one call. It took a personal word whose
/// capitalisation differs from what was typed, which shares no prefix at all, to expose it.</para>
///
/// <para>Nothing about that failure throws, and it happens inside another process, so the only place it can
/// be caught is here: by asserting on the batch before it is sent.</para>
/// </summary>
public class TextInjectionTests
{
    private const int VkBack = 0x08;
    private const int KeyEventUnicode = 0x0004;
    private const int KeyEventKeyUp = 0x0002;

    private static bool IsBackspace(NativeMethods.INPUT input) =>
        input.U.ki.wVk == VkBack;

    private static bool IsKeyUp(NativeMethods.INPUT input) =>
        (input.U.ki.dwFlags & KeyEventKeyUp) != 0;

    private static string TextOf(IEnumerable<NativeMethods.INPUT> inputs) =>
        new(inputs.Where(i => (i.U.ki.dwFlags & KeyEventUnicode) != 0 && !IsKeyUp(i))
                  .Select(i => (char)i.U.ki.wScan)
                  .ToArray());

    [Fact]
    public void Deletions_and_text_are_one_batch()
    {
        // The fix, stated directly: everything the replacement needs is in a single array, so it becomes a
        // single SendInput call and Windows keeps it in order.
        var batch = Win32TextInjector.BuildReplacement(backspaces: 3, text: "Alexandra Fairbourne Reed ");

        Assert.Equal((3 + "Alexandra Fairbourne Reed ".Length) * 2, batch.Length);
    }

    [Fact]
    public void Every_deletion_comes_before_any_text()
    {
        var batch = Win32TextInjector.BuildReplacement(backspaces: 3, text: "Alexandra");

        var lastBackspace = Array.FindLastIndex(batch, IsBackspace);
        var firstCharacter = Array.FindIndex(batch, i => !IsBackspace(i));

        Assert.True(lastBackspace < firstCharacter,
            "a deletion after the text has started would delete the text");
    }

    [Fact]
    public void The_text_survives_intact()
    {
        const string phrase = "Alexandra Fairbourne Reed ";

        var batch = Win32TextInjector.BuildReplacement(backspaces: 3, text: phrase);

        Assert.Equal(phrase, TextOf(batch));
    }

    [Fact]
    public void Spaces_inside_a_phrase_are_carried_through()
    {
        var batch = Win32TextInjector.BuildReplacement(backspaces: 0, text: "Flat 12, 46 Elmwood");

        Assert.Equal("Flat 12, 46 Elmwood", TextOf(batch));
    }

    [Fact]
    public void Punctuation_and_symbols_survive()
    {
        // Email addresses are exactly what a personal vocabulary gets used for.
        const string address = "alexandra.fairbourne-reed@example.org";

        var batch = Win32TextInjector.BuildReplacement(backspaces: 5, text: address);

        Assert.Equal(address, TextOf(batch));
    }

    [Fact]
    public void Each_character_is_sent_as_a_down_and_up_pair()
    {
        var batch = Win32TextInjector.BuildReplacement(backspaces: 1, text: "ab");

        Assert.Equal(6, batch.Length);
        Assert.False(IsKeyUp(batch[0]));
        Assert.True(IsKeyUp(batch[1]));
        Assert.False(IsKeyUp(batch[2]));
        Assert.True(IsKeyUp(batch[3]));
    }

    [Fact]
    public void Nothing_to_delete_produces_text_only()
    {
        var batch = Win32TextInjector.BuildReplacement(backspaces: 0, text: "world ");

        Assert.DoesNotContain(batch, IsBackspace);
        Assert.Equal("world ", TextOf(batch));
    }

    [Fact]
    public void A_negative_deletion_count_is_treated_as_none()
    {
        var batch = Win32TextInjector.BuildReplacement(backspaces: -2, text: "x");

        Assert.Equal(2, batch.Length);
        Assert.DoesNotContain(batch, IsBackspace);
    }

    [Fact]
    public void An_empty_replacement_produces_deletions_only()
    {
        var batch = Win32TextInjector.BuildReplacement(backspaces: 2, text: string.Empty);

        Assert.Equal(4, batch.Length);
        Assert.All(batch, i => Assert.True(IsBackspace(i)));
    }

    [Fact]
    public void Every_event_carries_the_marker_that_identifies_it_as_ours()
    {
        // Without this the app reads its own injection back as the user typing, and the word buffer drifts.
        var batch = Win32TextInjector.BuildReplacement(backspaces: 2, text: "hi");

        Assert.All(batch, i => Assert.Equal(NativeMethods.OwnInjectionMarker, i.U.ki.dwExtraInfo));
    }
}
