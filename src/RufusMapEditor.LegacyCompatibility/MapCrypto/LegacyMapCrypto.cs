using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.MapCrypto;

/// <summary>
/// Classical DOFUS Retro MapData XOR crypto (Astria <c>Decryptage</c> pattern).
/// Generic implementation for investigation — <b>not</b> confirmed RUFUS production crypto.
/// Validity against RUFUS requires RUFUS-owned keys. External emulator dumps are not a source of truth.
/// </summary>
public static class LegacyMapCrypto
{
    /// <summary>Uppercase hex for checksum digit (Encriptador.HEX_CHARS).</summary>
    private static readonly char[] HexCharsUpper =
        { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

    /// <summary>Lowercase hex for MapData storage (client SWF 10420 uses lowercase).</summary>
    private static readonly char[] HexCharsLower =
        { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

    /// <summary>
    /// Converts a hex-encoded key string into the working XOR key.
    /// </summary>
    public static string PrepareKey(string hexKey)
    {
        if (string.IsNullOrEmpty(hexKey))
            return string.Empty;

        hexKey = hexKey.Trim().Replace("\r", "").Replace("\n", "");
        if (hexKey.Length % 2 != 0)
            throw new ArgumentException("Key hex length must be even.", nameof(hexKey));

        var sb = new StringBuilder(hexKey.Length / 2);
        for (var i = 0; i < hexKey.Length; i += 2)
        {
            var b = int.Parse(hexKey.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            sb.Append((char)b);
        }

        return Uri.UnescapeDataString(sb.ToString());
    }

    /// <summary>Checksum digit of a prepared key (Astria / Encriptador).</summary>
    public static char ChecksumDigit(string preparedKey)
    {
        var sum = 0;
        foreach (var ch in preparedKey)
            sum += ch % 16;
        return HexCharsUpper[sum % 16];
    }

    /// <summary>Checksum offset used by cypher/decypher: parse(digit,16) * 2.</summary>
    public static int ChecksumOffset(string preparedKey) =>
        int.Parse(ChecksumDigit(preparedKey).ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture) * 2;

    /// <summary>
    /// Decrypts hex MapData (2 chars per byte) using a hex-encoded key.
    /// </summary>
    public static string Decrypt(string encryptedHexMapData, string hexKey)
    {
        if (string.IsNullOrEmpty(encryptedHexMapData))
            return string.Empty;
        if (encryptedHexMapData.Length % 2 != 0)
            throw new ArgumentException("Encrypted MapData length must be even.", nameof(encryptedHexMapData));

        var key = PrepareKey(hexKey);
        if (key.Length == 0)
            throw new ArgumentException("Prepared key is empty.", nameof(hexKey));

        var c = ChecksumOffset(key);
        var sb = new StringBuilder(encryptedHexMapData.Length / 2);
        var ki = 0;
        for (var i = 0; i < encryptedHexMapData.Length; i += 2)
        {
            var num = int.Parse(encryptedHexMapData.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var k = key[(ki + c) % key.Length];
            sb.Append((char)(num ^ k));
            ki++;
        }

        return Uri.UnescapeDataString(sb.ToString());
    }

    /// <summary>
    /// Encrypts plain MapData to hex representation for client SWF storage.
    /// </summary>
    public static string Encrypt(string plainMapData, string hexKey)
    {
        if (plainMapData is null)
            throw new ArgumentNullException(nameof(plainMapData));

        var key = PrepareKey(hexKey);
        if (key.Length == 0)
            throw new ArgumentException("Prepared key is empty.", nameof(hexKey));

        var c = ChecksumOffset(key);
        var pre = PreEscape(plainMapData);
        var sb = new StringBuilder(pre.Length * 2);
        for (var i = 0; i < pre.Length; i++)
        {
            var xored = pre[i] ^ key[(i + c) % key.Length];
            sb.Append(D2H(xored & 0xFF));
        }

        return sb.ToString();
    }

    /// <summary>True if MapData looks hex-encrypted (Astria heuristic: &gt;1000 digit chars).</summary>
    public static bool LooksEncrypted(string mapData)
    {
        if (string.IsNullOrEmpty(mapData))
            return false;
        var digits = 0;
        foreach (var ch in mapData)
        {
            if (char.IsDigit(ch))
                digits++;
            if (digits > 1000)
                return true;
        }

        return false;
    }

    private static string PreEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var code = (int)ch;
            if (code < 32 || code > 127 || ch is '%' or '+')
                sb.Append(Uri.EscapeDataString(ch.ToString()));
            else
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string D2H(int d)
    {
        if (d > 255) d = 255;
        // Client SWF MapData uses lowercase hex digits (observed on 10420_0706141524X.swf).
        return $"{HexCharsLower[d / 16]}{HexCharsLower[d % 16]}";
    }
}
