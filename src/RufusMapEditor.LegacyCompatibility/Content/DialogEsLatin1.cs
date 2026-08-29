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

/// <summary>CONT.6B — dialog_es texts are Latin1. Never silently replace characters.</summary>
public static class DialogEsLatin1
{
    public static Encoding Strict { get; } = Encoding.GetEncoding(
        "iso-8859-1",
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

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

    public static byte[] GetBytes(string text)
    {
        Validate(text);
        return Encoding.Latin1.GetBytes(text);
    }
}
