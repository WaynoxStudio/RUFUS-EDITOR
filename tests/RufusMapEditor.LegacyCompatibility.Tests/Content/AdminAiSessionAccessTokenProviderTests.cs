using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Contracts.Admin;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class AdminAiSessionAccessTokenProviderTests
{
    [Fact]
    public async Task EnsureReadyAsync_caches_token_without_sync_block()
    {
        var calls = 0;
        var provider = new AdminAiSessionAccessTokenProvider(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new AdminAiSessionResponse
            {
                AccessToken = "rai1.test.token.value",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            });
        });

        Assert.Null(provider.TryGetAccessToken());
        await provider.EnsureReadyAsync();
        Assert.Equal("rai1.test.token.value", provider.TryGetAccessToken());
        await provider.EnsureReadyAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RefreshAfterUnauthorizedAsync_issues_new_token()
    {
        var n = 0;
        var provider = new AdminAiSessionAccessTokenProvider(_ =>
        {
            var i = Interlocked.Increment(ref n);
            return Task.FromResult(new AdminAiSessionResponse
            {
                AccessToken = $"rai1.token.{i}",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            });
        });

        await provider.EnsureReadyAsync();
        Assert.Equal("rai1.token.1", provider.TryGetAccessToken());
        Assert.True(await provider.RefreshAfterUnauthorizedAsync());
        Assert.Equal("rai1.token.2", provider.TryGetAccessToken());
    }
}
