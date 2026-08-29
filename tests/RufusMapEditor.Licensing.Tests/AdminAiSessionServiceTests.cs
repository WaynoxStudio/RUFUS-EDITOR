using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Services;

namespace RufusMapEditor.Licensing.Tests;

public sealed class AdminAiSessionServiceTests
{
    [Fact]
    public void Issue_and_validate_roundtrip()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-08-26T12:00:00Z"));
        var svc = new AdminAiSessionService(new AdminAiSessionOptions
        {
            SigningSecret = "test-admin-secret-32chars!!",
            LifetimeMinutes = 60,
        }, clock);

        var issued = svc.Issue();
        Assert.True(svc.TryValidate(issued.AccessToken, out var exp));
        Assert.Equal(issued.ExpiresAt, exp);
    }

    [Fact]
    public void Expired_token_fails()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-08-26T12:00:00Z"));
        var svc = new AdminAiSessionService(new AdminAiSessionOptions
        {
            SigningSecret = "test-admin-secret-32chars!!",
            LifetimeMinutes = 60,
        }, clock);

        var issued = svc.Issue();
        clock.UtcNow = clock.UtcNow.AddHours(2);
        Assert.False(svc.TryValidate(issued.AccessToken, out _));
    }

    [Fact]
    public void Different_secret_rejects_token()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var a = new AdminAiSessionService(new AdminAiSessionOptions
        {
            SigningSecret = "test-admin-secret-32chars!!",
            LifetimeMinutes = 60,
        }, clock);
        var b = new AdminAiSessionService(new AdminAiSessionOptions
        {
            SigningSecret = "other-admin-secret-32chars!",
            LifetimeMinutes = 60,
        }, clock);

        var issued = a.Issue();
        Assert.False(b.TryValidate(issued.AccessToken, out _));
    }

    [Fact]
    public void Lifetime_clamped_and_centralized()
    {
        var opts = new AdminAiSessionOptions { SigningSecret = "x".PadRight(16, 'x'), LifetimeMinutes = 1 };
        Assert.Equal(TimeSpan.FromMinutes(AdminAiSessionOptions.MinLifetimeMinutes), opts.Lifetime);

        opts = new AdminAiSessionOptions { SigningSecret = "x".PadRight(16, 'x'), LifetimeMinutes = 99999 };
        Assert.Equal(TimeSpan.FromMinutes(AdminAiSessionOptions.MaxLifetimeMinutes), opts.Lifetime);
    }

    private sealed class FixedClock : IServerClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
    }
}
