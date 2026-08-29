using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.4C / AI.6B.2 — BackendUrl resolution (temporary VPS HTTPS; no OpenAI keys).</summary>
public sealed class AiBackendLocalDevUrlTests
{
    [Fact]
    public void Normalize_appends_generate_path_to_base()
    {
        Assert.True(AiBackendLocalDevUrl.TryNormalizeEndpoint("http://127.0.0.1:5088", out var url));
        Assert.Equal("http://127.0.0.1:5088/v1/ai/generate", url);
    }

    [Fact]
    public void Normalize_keeps_existing_generate_path()
    {
        Assert.True(AiBackendLocalDevUrl.TryNormalizeEndpoint(
            "http://127.0.0.1:5088/v1/ai/generate", out var url));
        Assert.Equal("http://127.0.0.1:5088/v1/ai/generate", url);
    }

    [Fact]
    public void Normalize_prefers_http_from_launchSettings_list()
    {
        Assert.True(AiBackendLocalDevUrl.TryNormalizeEndpoint(
            "http://127.0.0.1:5088;https://127.0.0.1:7088", out var url));
        Assert.StartsWith("http://127.0.0.1:5088", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_keeps_temporary_vps_generate_path()
    {
        Assert.True(AiBackendLocalDevUrl.TryNormalizeEndpoint(
            AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint, out var url));
        Assert.Equal(AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint, url);
    }

    [Fact]
    public void Resolve_uses_temporary_vps_when_env_unset()
    {
        var previous = Environment.GetEnvironmentVariable(
            AiBackendLocalDevUrl.BackendUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                AiBackendLocalDevUrl.BackendUrlEnvironmentVariable, null);

            Assert.True(AiBackendLocalDevUrl.TryResolveGenerateEndpoint(out var endpoint, out var source));
            Assert.Equal(AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint, endpoint);
            Assert.Equal(AiBackendLocalDevUrl.TemporaryVpsSource, source);
            Assert.StartsWith("https://", endpoint, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://vmi3502135", endpoint, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", endpoint, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("5088", endpoint, StringComparison.Ordinal);
            Assert.DoesNotContain("api.openai.com", endpoint, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", endpoint, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AiBackendLocalDevUrl.BackendUrlEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void LaunchSettings_helpers_still_read_local_dev_url()
    {
        Assert.True(AiBackendLocalDevUrl.TryFindLaunchSettingsPath(out var path),
            "launchSettings.json del AiBackend debe existir en el repo");
        Assert.True(AiBackendLocalDevUrl.TryReadApplicationUrl(path, out var appUrl));
        Assert.Contains("127.0.0.1", appUrl, StringComparison.Ordinal);
        Assert.Contains("5088", appUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_configures_service_as_available_with_vps_url()
    {
        var previous = Environment.GetEnvironmentVariable(
            AiBackendLocalDevUrl.BackendUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                AiBackendLocalDevUrl.BackendUrlEnvironmentVariable, null);

            var svc = AiBackendGenerationServiceFactory.CreateForEditor();
            Assert.True(svc.IsConfigured);
            Assert.Equal(AiGenerationServiceStatus.Available, svc.Status);
            Assert.Equal(AiBackendLocalDevUrl.TemporaryVpsGenerateEndpoint, svc.Settings.BackendUrl);
            Assert.DoesNotContain(
                typeof(AiBackendSettings).GetProperties().Select(p => p.Name),
                n => n.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("OpenAi", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                AiBackendLocalDevUrl.BackendUrlEnvironmentVariable, previous);
        }
    }
}
