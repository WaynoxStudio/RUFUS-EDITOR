using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class DialogEsEncodingException : InvalidOperationException
{
    public DialogEsEncodingException(string text, int index, char ch)
        : base(BuildMessage(text, index, ch))
    {
        Text = text;
        Index = index;
        Character = ch;
    }

    public string Text { get; }
    public int Index { get; }
    public char Character { get; }

    private static string BuildMessage(string text, int index, char ch)
    {
        var snippet = text.Length <= 80 ? text : text[..80] + "…";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Texto no representable en Latin1 (dialog_es). Índice {index}, U+{(int)ch:X4} '{ch}'. Texto: {snippet}");
    }
}

/// <summary>
/// CONT.6B / PUBLISH.PREFLIGHT.LATIN1 — dialog text repertoire is Western/ISO-8859-1 code points.
/// SWF6 AVM1 ActionPush transport is UTF-8 (see <see cref="LangMaps.Avm1Bytecode"/>).
/// Known typographic Unicode is mapped to safe equivalents; unknown chars still block.
/// </summary>
public static class DialogEsLatin1
{
    public static Encoding Strict { get; } = Encoding.GetEncoding(
        "iso-8859-1",
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    /// <summary>
    /// Maps common typographic characters to Latin1-safe equivalents.
    /// Preserves Spanish Latin1 letters (áéíóúñü ¿ ¡) and does not ASCII-fold the whole string.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var repl = MapTypographic(ch);
            if (repl is null)
            {
                sb?.Append(ch);
                continue;
            }

            if (sb is null)
            {
                sb = new StringBuilder(text.Length + 8);
                sb.Append(text, 0, i);
            }

            sb.Append(repl);
        }

        return sb?.ToString() ?? text;
    }

    private static string? MapTypographic(char ch) => ch switch
    {
        '\u2014' => "-",  // —
        '\u2013' => "-",  // –
        '\u2012' => "-",  // ‒
        '\u2015' => "-",  // ―
        '\u2212' => "-",  // −
        '\u2018' => "'",  // ‘
        '\u2019' => "'",  // ’
        '\u201A' => "'",  // ‚
        '\u201B' => "'",  // ‛
        '\u2032' => "'",  // ′
        '\u201C' => "\"", // “
        '\u201D' => "\"", // ”
        '\u201E' => "\"", // „
        '\u201F' => "\"", // ‟
        '\u2033' => "\"", // ″
        '\u2026' => "...", // …
        '\u2022' => "*",  // •
        '\u00A0' => " ",  // NBSP → space (Latin1-safe, clearer in SWF)
        '\u202F' => " ",  // narrow NBSP
        '\u2007' => " ",  // figure space
        '\u2009' => " ",  // thin space
        '\u200A' => " ",  // hair space
        '\u200B' => "",   // zero-width space
        '\uFEFF' => "",   // BOM / ZWNBSP
        _ => null,
    };

    public static void Validate(string text, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\0'))
        {
            throw new DialogEsEncodingException(text, text.IndexOf('\0'), '\0');
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch > 0xFF)
                throw new DialogEsEncodingException(text, i, ch);
        }

        try
        {
            _ = Strict.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            var idx = ex.Index;
            var ch = idx >= 0 && idx < text.Length ? text[idx] : '?';
            throw new DialogEsEncodingException(label is null ? text : label + ": " + text, idx, ch);
        }
    }

    /// <summary>Normalize known typographics, then validate. Returns the Latin1-ready text.</summary>
    public static string Prepare(string text, string? label = null)
    {
        var normalized = Normalize(text);
        Validate(normalized, label);
        return normalized;
    }

    /// <summary>
    /// Wire bytes for SWF6 AVM1 string literals: UTF-8 encoding of a Latin1-repertoire string.
    /// Repertoire is validated by <see cref="Prepare"/>; transport is not ISO-8859-1 bytes.
    /// </summary>
    public static byte[] GetBytes(string text)
    {
        var prepared = Prepare(text);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(prepared);
    }
}
