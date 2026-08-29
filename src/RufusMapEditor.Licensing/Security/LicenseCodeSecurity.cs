using System.Security.Cryptography;
using System.Text;

namespace RufusMapEditor.Licensing.Security;

/// <summary>
/// Cryptographically random license codes. Format: RUF-XXXX-XXXX-XXXX-XXXX (Crockford Base32).
/// No sequential IDs, dates, or user data inside the code.
/// </summary>
public static class LicenseCodeGenerator
{
    // Crockford Base32 without I,L,O,U
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate()
    {
        Span<char> chars = stackalloc char[4 * 4];
        var bytes = new byte[chars.Length];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];

        return string.Concat(
            "RUF-",
            new string(chars[..4]), "-",
            new string(chars.Slice(4, 4)), "-",
            new string(chars.Slice(8, 4)), "-",
            new string(chars.Slice(12, 4)));
    }

    public static string Normalize(string code) =>
        (code ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
}

/// <summary>
/// Store only SHA-256 hex of normalized code. Full plaintext returned once at Create; not persisted.
/// Validation = hash(input) lookup. No reversible encryption of the code.
/// </summary>
public static class LicenseCodeHasher
{
    public static string Hash(string normalizedCode)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string DisplayHint(string normalizedCode)
    {
        if (string.IsNullOrEmpty(normalizedCode) || normalizedCode.Length < 4)
            return "****";
        return normalizedCode[^4..];
    }
}

public static class SessionTokenGenerator
{
    /// <summary>URL-safe opaque session token (not the license code).</summary>
    public static string Generate()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
