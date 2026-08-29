using RufusMapEditor.LegacyCompatibility.MapCrypto;
using RufusMapEditor.LegacyCompatibility.Sql;

namespace RufusMapEditor.LegacyCompatibility.Tests.MapCrypto;

/// <summary>
/// Self-contained LegacyMapCrypto math tests (Astria fixture plaintext).
/// Does not use external emulator dumps or client SWF artifacts.
/// </summary>
public sealed class LegacyMapCryptoMathTests
{
    private static string Fixture10420 =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps", "10420.sql"));

    private static string SyntheticHexKey(int charPairs = 207) =>
        string.Concat(Enumerable.Repeat("41", charPairs));

    [Fact]
    public void Encrypt_decrypt_roundtrip_with_astria_fixture_plaintext()
    {
        var plain = AstriaSqlMapParser.ParseFile(Fixture10420).MapData;
        Assert.False(LegacyMapCrypto.LooksEncrypted(plain));
        var hexKey = SyntheticHexKey();
        var encrypted = LegacyMapCrypto.Encrypt(plain, hexKey);
        Assert.True(LegacyMapCrypto.LooksEncrypted(encrypted));
        Assert.Equal(plain.Length * 2, encrypted.Length);
        var decrypted = LegacyMapCrypto.Decrypt(encrypted, hexKey);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void LooksEncrypted_heuristic_distinguishes_plain_and_hex()
    {
        var shortPlain = "Hhhjeaaaaa";
        var shortEncrypted = LegacyMapCrypto.Encrypt(shortPlain, SyntheticHexKey(5));
        Assert.False(LegacyMapCrypto.LooksEncrypted(shortPlain));
        // Astria heuristic requires >1000 digit chars; short ciphertext does not qualify.
        Assert.False(LegacyMapCrypto.LooksEncrypted(shortEncrypted));

        var longPlain = new string('a', 900);
        var longEncrypted = LegacyMapCrypto.Encrypt(longPlain, SyntheticHexKey());
        Assert.False(LegacyMapCrypto.LooksEncrypted(longPlain));
        Assert.True(LegacyMapCrypto.LooksEncrypted(longEncrypted));
    }
}
