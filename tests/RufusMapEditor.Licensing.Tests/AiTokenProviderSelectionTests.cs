using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Options;

namespace RufusMapEditor.Licensing.Tests;

public sealed class AiTokenProviderSelectionTests
{
    [Fact]
    public void Licensing_enforced_uses_session_provider_without_env_fallback()
    {
        var store = new MemorySessionStore();
        Environment.SetEnvironmentVariable(AiBackendAccessTokenEnv.VariableName, "should-not-use");
        try
        {
            var provider = AiBackendGenerationServiceFactory.ResolveTokenProvider(
                licensingEnforced: true, store);
            Assert.IsType<SessionAccessTokenProvider>(provider);
            Assert.Null(provider.TryGetAccessToken());
        }
        finally
        {
            Environment.SetEnvironmentVariable(AiBackendAccessTokenEnv.VariableName, null);
        }
    }

    [Fact]
    public void Licensing_not_enforced_uses_environment_provider()
    {
        var provider = AiBackendGenerationServiceFactory.ResolveTokenProvider(
            licensingEnforced: false, new MemorySessionStore());
        Assert.IsType<EnvironmentAiBackendAccessTokenProvider>(provider);
    }

    [Fact]
    public async Task Session_provider_returns_token_when_permission_ai()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new LicenseSessionLocalState
        {
            SessionToken = "sess-tok",
            PermissionAi = true,
            PermissionEditor = true,
            DeviceId = "d",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        });
        var provider = new SessionAccessTokenProvider(store);
        Assert.Equal("sess-tok", provider.TryGetAccessToken());
    }
}

public sealed class LicenseEnforcementOptionsTests
{
    [Fact]
    public void Development_build_not_user_by_default()
    {
#if RUFUS_USER
        Assert.True(LicenseEnforcementOptions.IsUserBuild);
        Assert.True(LicenseEnforcementOptions.IsEnforced);
        Assert.True(LicenseEnforcementOptions.UsesSessionTokenForAi);
#else
        Assert.False(LicenseEnforcementOptions.IsUserBuild);
        Assert.True(LicenseEnforcementOptions.IsDevelopmentBuild);
#endif
    }

    [Fact]
    public void Development_enforcement_follows_env_flag()
    {
#if RUFUS_USER
        return;
#else
        Environment.SetEnvironmentVariable(LicenseTestOptions.EnvironmentVariable, null);
        try
        {
            Assert.False(LicenseEnforcementOptions.IsEnforced);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseTestOptions.EnvironmentVariable, null);
        }

        Environment.SetEnvironmentVariable(LicenseTestOptions.EnvironmentVariable, "1");
        try
        {
            Assert.True(LicenseEnforcementOptions.IsEnforced);
            Assert.True(LicenseEnforcementOptions.UsesSessionTokenForAi);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseTestOptions.EnvironmentVariable, null);
        }
#endif
    }
}
