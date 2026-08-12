using System.Buffers.Binary;
using WordStrip.Core.Automation;

namespace WordStrip.Core.Text.Tsf;

/// <summary>
/// The wire format between the text service and WordStrip, and the only thing the two sides share.
///
/// <para><b>Why there is a wire at all.</b> A TSF text service is a DLL loaded into Chrome, Word and every
/// other application that accepts text. WordStrip's prediction engine is half a gigabyte of managed code
/// behind a WPF window; it cannot live inside those processes, and nothing would persuade a browser vendor
/// it should. So the service gathers context where it is and sends it to the tray application, which is the
/// one place that knows how to predict.</para>
///
/// <para><b>Raw text crosses, not parsed words.</b> Tempting to tokenise in the service and send a tidy word
/// list, but the tokenizer is shared between the offline model builder and the running app precisely so the
/// two can never disagree about what a token is — a rule recorded in section 13 of the context document, and
/// one whose failure mode is a bar that silently stops predicting. Adding a third tokenizer, in another
/// language, would be the exact mistake that rule exists to prevent. The service sends characters; the
/// managed side tokenises them with the same code it always has.</para>
///
/// <para><b>The text is bounded hard, and that is a privacy requirement rather than an optimisation.</b> The
/// phase brief says not to store documents and to pass the minimum context needed. A trigram model needs the
/// word in progress and two words behind it, so <see cref="MaxTextChars"/> characters is already generous.
/// A service with an entire document available must still send only this much, and the format gives it
/// nowhere to put more.</para>
/// </summary>
public readonly record struct TsfContextMessage(
    bool IsEditable,
    bool IsPasswordField,
    bool HasSelection,
    CaretRect? Caret,
    string TextBeforeCaret)
{
    /// <summary>Bumped only on a breaking change. A service and a tray app from different builds must refuse each other rather than misread.</summary>
    public const uint Version = 1;

    /// <summary>
    /// Hard cap on characters sent. Enough for a 50-character personal entry plus two preceding words, with
    /// room to spare, and nowhere near enough to be a document.
    /// </summary>
    public const int MaxTextChars = 128;

    private const uint FlagEditable = 1 << 0;
    private const uint FlagPassword = 1 << 1;
    private const uint FlagSelection = 1 << 2;
    private const uint FlagHasCaret = 1 << 3;

    /// <summary>Fixed part: version, flags, four caret ints, text length.</summary>
    public const int HeaderBytes = 4 + 4 + (4 * 4) + 4;

    public const int MaxBytes = HeaderBytes + (MaxTextChars * 2);

    /// <summary>
    /// Reads one message. Returns null for anything malformed, truncated, or from a different protocol
    /// version — never throws, because the sender is a DLL running inside somebody else's application and a
    /// parser that throws on the typing path is a worse failure than a dropped update.
    /// </summary>
    public static TsfContextMessage? TryParse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes) return null;

        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (version != Version) return null;

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var left = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var top = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        var right = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        var bottom = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]);
        var textChars = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]);

        // A length claiming more than the cap is either a bug or something hostile; either way the answer is
        // to drop the message rather than to allocate what it asks for.
        if (textChars > MaxTextChars) return null;

        var textBytes = checked((int)textChars * 2);
        if (bytes.Length < HeaderBytes + textBytes) return null;

        var text = textChars == 0
            ? string.Empty
            : System.Text.Encoding.Unicode.GetString(bytes.Slice(HeaderBytes, textBytes));

        return new TsfContextMessage(
            IsEditable: (flags & FlagEditable) != 0,
            IsPasswordField: (flags & FlagPassword) != 0,
            HasSelection: (flags & FlagSelection) != 0,
            Caret: (flags & FlagHasCaret) != 0 ? new CaretRect(left, top, right, bottom) : null,
            TextBeforeCaret: text);
    }

    /// <summary>Writes this message. Exists for the tests — the real sender is the C++ service.</summary>
    public byte[] ToBytes()
    {
        var text = TextBeforeCaret ?? string.Empty;
        if (text.Length > MaxTextChars) text = text[^MaxTextChars..];

        var buffer = new byte[HeaderBytes + (text.Length * 2)];
        var span = buffer.AsSpan();

        uint flags = 0;
        if (IsEditable) flags |= FlagEditable;
        if (IsPasswordField) flags |= FlagPassword;
        if (HasSelection) flags |= FlagSelection;
        if (Caret is not null) flags |= FlagHasCaret;

        BinaryPrimitives.WriteUInt32LittleEndian(span, Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], flags);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], Caret?.Left ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], Caret?.Top ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], Caret?.Right ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], Caret?.Bottom ?? 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)text.Length);

        System.Text.Encoding.Unicode.GetBytes(text, span[HeaderBytes..]);

        return buffer;
    }
}
