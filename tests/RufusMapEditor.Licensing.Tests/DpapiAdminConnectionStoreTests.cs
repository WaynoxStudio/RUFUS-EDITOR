using RufusMapEditor.Licensing.Client;

namespace RufusMapEditor.Licensing.Tests;

public sealed class DpapiAdminConnectionStoreTests
{
    [Fact]
    public async Task Roundtrip_saves_and_loads_baseurl_and_secret()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "rufus-admin1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new DpapiAdminConnectionStore(dir);
            await store.SaveAsync(new AdminConnectionState
            {
                BaseUrl = "https://vmi3502135.contaboserver.net",
                AdminSecret = "unit-test-admin-secret-32chars!!",
            });

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal("https://vmi3502135.contaboserver.net", loaded!.BaseUrl);
            Assert.Equal("unit-test-admin-secret-32chars!!", loaded.AdminSecret);
            Assert.True(File.Exists(Path.Combine(dir, DpapiAdminConnectionStore.FileName)));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Blob_on_disk_is_not_plaintext_secret()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "rufus-admin1-pt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string secret = "plaintext-must-not-appear-on-disk!!";
            var store = new DpapiAdminConnectionStore(dir);
            await store.SaveAsync(new AdminConnectionState
            {
                BaseUrl = "https://example.test",
                AdminSecret = secret,
            });

            var bytes = await File.ReadAllBytesAsync(store.StorePath);
            var asText = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain(secret, asText, StringComparison.Ordinal);
            Assert.DoesNotContain("https://example.test", asText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Clear_removes_store_file()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "rufus-admin1-clr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new DpapiAdminConnectionStore(dir);
            await store.SaveAsync(new AdminConnectionState
            {
                BaseUrl = "https://example.test",
                AdminSecret = "secret-for-clear-test-xxxxxxxx",
            });
            Assert.True(File.Exists(store.StorePath));
            await store.ClearAsync();
            Assert.False(File.Exists(store.StorePath));
            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Rejects_empty_fields_on_save()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "rufus-admin1-rej-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new DpapiAdminConnectionStore(dir);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(new AdminConnectionState { BaseUrl = "", AdminSecret = "x" }));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(new AdminConnectionState { BaseUrl = "https://x", AdminSecret = "" }));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
