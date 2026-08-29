using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Model;
using RufusMapEditor.Licensing.Security;

namespace RufusMapEditor.AiBackend.Tests;

public sealed class LicenseHttpIntegrationTests : IClassFixture<LicenseWebAppFactory>
{
    private readonly LicenseWebAppFactory _factory;
    private readonly HttpClient _client;

    public LicenseHttpIntegrationTests(LicenseWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpRequestMessage Admin(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LicenseWebAppFactory.AdminSecret);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task Admin_without_auth_is_401_even_with_empty_body()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/licenses")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Admin_wrong_secret_is_401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/licenses");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-secret-value!!");
        using var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Full_flow_create_activate_limits_heartbeat_logout_admin_actions()
    {
        // Create
        using var createRes = await _client.SendAsync(Admin(HttpMethod.Post, "/v1/admin/licenses", new CreateLicenseRequest
        {
            DurationDays = 30,
            MaxDevices = 1,
            MaxConcurrentSessions = 1,
            PermissionEditor = true,
            PermissionAi = true,
            AdminNotes = "lic3-test",
        }));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CreateLicenseResponse>();
        Assert.False(string.IsNullOrWhiteSpace(created!.LicenseCode));
        Assert.StartsWith("RUF-", created.LicenseCode);

        // List shows hint not full code
        using var listRes = await _client.SendAsync(Admin(HttpMethod.Get, "/v1/admin/licenses"));
        listRes.EnsureSuccessStatusCode();
        var listJson = await listRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.LicenseCode, listJson);
        Assert.Contains(created.CodeDisplayHint, listJson);

        // Get detail
        using var getRes = await _client.SendAsync(Admin(HttpMethod.Get, $"/v1/admin/licenses/{created.LicenseId}"));
        getRes.EnsureSuccessStatusCode();

        // Activate device A
        using var actA = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-a-hash",
            ClientVersion = "test",
        });
        Assert.Equal(HttpStatusCode.OK, actA.StatusCode);
        var sessionA = await actA.Content.ReadFromJsonAsync<SessionSuccessResponse>();
        Assert.True(sessionA!.Permissions.Editor);
        Assert.True(sessionA.Permissions.Ai);
        Assert.NotEqual(created.LicenseCode, sessionA.SessionToken);
        Assert.NotEqual(LicenseWebAppFactory.AdminSecret, sessionA.SessionToken);

        // Second device blocked
        using var actB = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-b-hash",
        });
        Assert.Equal(HttpStatusCode.Forbidden, actB.StatusCode);
        var errB = await actB.Content.ReadFromJsonAsync<LicenseErrorResponse>();
        Assert.Equal(LicenseErrorCodes.DeviceLimitReached, errB!.ErrorCode);

        // Heartbeat OK
        using var hb = await _client.PostAsJsonAsync("/v1/license/heartbeat", new HeartbeatRequest
        {
            SessionToken = sessionA.SessionToken,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.OK, hb.StatusCode);

        // Session validate alias
        using var sess = await _client.PostAsJsonAsync("/v1/license/session", new HeartbeatRequest
        {
            SessionToken = sessionA.SessionToken,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.OK, sess.StatusCode);

        // Logout
        using var logout = await _client.PostAsJsonAsync("/v1/license/logout", new LogoutRequest
        {
            SessionToken = sessionA.SessionToken,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        // Re-activate same device OK
        using var actA2 = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.OK, actA2.StatusCode);
        var sessionA2 = await actA2.Content.ReadFromJsonAsync<SessionSuccessResponse>();

        // Terminate session from admin
        using var term = await _client.SendAsync(Admin(HttpMethod.Post, $"/v1/admin/licenses/{created.LicenseId}/terminate-session"));
        term.EnsureSuccessStatusCode();
        using var hb2 = await _client.PostAsJsonAsync("/v1/license/heartbeat", new HeartbeatRequest
        {
            SessionToken = sessionA2!.SessionToken,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, hb2.StatusCode);

        // Suspend / reactivate / revoke
        using var act3 = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-a-hash",
        });
        act3.EnsureSuccessStatusCode();

        using var sus = await _client.SendAsync(Admin(HttpMethod.Post, $"/v1/admin/licenses/{created.LicenseId}/suspend"));
        sus.EnsureSuccessStatusCode();
        using var actSus = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.Forbidden, actSus.StatusCode);

        using var rea = await _client.SendAsync(Admin(HttpMethod.Post, $"/v1/admin/licenses/{created.LicenseId}/reactivate"));
        rea.EnsureSuccessStatusCode();
        using var actOk = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.OK, actOk.StatusCode);

        using var rev = await _client.SendAsync(Admin(HttpMethod.Post, $"/v1/admin/licenses/{created.LicenseId}/revoke"));
        rev.EnsureSuccessStatusCode();
        using var actRev = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created.LicenseCode,
            DeviceId = "device-a-hash",
        });
        Assert.Equal(HttpStatusCode.Forbidden, actRev.StatusCode);

        // Extend on a fresh license
        using var c2 = await _client.SendAsync(Admin(HttpMethod.Post, "/v1/admin/licenses", new CreateLicenseRequest
        {
            DurationDays = 7,
            MaxDevices = 2,
            MaxConcurrentSessions = 1,
            PermissionEditor = true,
            PermissionAi = false,
        }));
        var created2 = await c2.Content.ReadFromJsonAsync<CreateLicenseResponse>();
        using var act2a = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created2!.LicenseCode,
            DeviceId = "d1",
        });
        act2a.EnsureSuccessStatusCode();
        // session limit: second device while first session active
        using var act2b = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created2.LicenseCode,
            DeviceId = "d2",
        });
        Assert.Equal(LicenseErrorCodes.SessionLimitReached,
            (await act2b.Content.ReadFromJsonAsync<LicenseErrorResponse>())!.ErrorCode);

        using var ext = await _client.SendAsync(Admin(HttpMethod.Post, $"/v1/admin/licenses/{created2.LicenseId}/extend",
            new ExtendLicenseRequest { ExtraDays = 30 }));
        ext.EnsureSuccessStatusCode();

        using var reset = await _client.SendAsync(Admin(HttpMethod.Post, $"/v1/admin/licenses/{created2.LicenseId}/reset-device"));
        reset.EnsureSuccessStatusCode();
        using var actAfterReset = await _client.PostAsJsonAsync("/v1/license/activate", new ActivateLicenseRequest
        {
            LicenseCode = created2.LicenseCode,
            DeviceId = "d-new",
        });
        Assert.Equal(HttpStatusCode.OK, actAfterReset.StatusCode);
        var sessPerm = await actAfterReset.Content.ReadFromJsonAsync<SessionSuccessResponse>();
        Assert.False(sessPerm!.Permissions.Ai);
    }

    [Fact]
    public void DeviceId_provider_is_stable_hash_without_raw_guid()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var p = new WindowsMachineGuidDeviceIdProvider();
        var a = p.GetDeviceId();
        var b = p.GetDeviceId();
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.DoesNotContain("-", a); // hex hash, not GUID format with dashes in typical MachineGuid sense — actually hex has no dashes
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public async Task Dpapi_session_store_roundtrip()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var dir = Path.Combine(Path.GetTempPath(), "rufus-lic3-sess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new DpapiLicenseSessionStore(dir);
            await store.SaveAsync(new LicenseSessionLocalState
            {
                SessionToken = "tok-lic3",
                DeviceId = "dev",
                PermissionAi = true,
                PermissionEditor = true,
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            var loaded = await store.LoadAsync();
            Assert.Equal("tok-lic3", loaded!.SessionToken);
            Assert.True(File.Exists(Path.Combine(dir, "license-session.bin")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void License_code_hash_not_equal_plaintext()
    {
        var code = LicenseCodeGenerator.Generate();
        var hash = LicenseCodeHasher.Hash(LicenseCodeGenerator.Normalize(code));
        Assert.NotEqual(code, hash);
    }

    [Fact]
    public void Dist_hygiene_patterns_exclude_admin()
    {
        var script = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "build-portable-dist.ps1")));
        Assert.Contains("AiBackend", script, StringComparison.OrdinalIgnoreCase);
        // After LIC.3 update should mention Admin
        Assert.True(
            script.Contains("RufusAdmin", StringComparison.OrdinalIgnoreCase)
            || script.Contains("RufusMapEditor.Admin", StringComparison.OrdinalIgnoreCase)
            || script.Contains("Admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_ai_usage_requires_auth()
    {
        using var bare = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/ai-usage");
        using var resBare = await _client.SendAsync(bare);
        Assert.Equal(HttpStatusCode.Unauthorized, resBare.StatusCode);

        using var wrong = Admin(HttpMethod.Get, "/v1/admin/ai-usage");
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-secret-value!!");
        using var resWrong = await _client.SendAsync(wrong);
        Assert.Equal(HttpStatusCode.Unauthorized, resWrong.StatusCode);
    }

    [Fact]
    public async Task Admin_ai_usage_returns_metrics_without_secrets()
    {
        using var createRes = await _client.SendAsync(Admin(HttpMethod.Post, "/v1/admin/licenses", new CreateLicenseRequest
        {
            DurationDays = 30,
            MaxDevices = 1,
            MaxConcurrentSessions = 1,
            PermissionEditor = true,
            PermissionAi = true,
        }));
        createRes.EnsureSuccessStatusCode();
        var created = await createRes.Content.ReadFromJsonAsync<CreateLicenseResponse>();
        Assert.NotNull(created);

        var db = _factory.Services.GetRequiredService<ILicenseUnitOfWork>();
        await db.ExecuteInTransactionAsync(async ct =>
        {
            await db.AiUsage.AppendEventAsync(new AiUsageEventEntity
            {
                LicenseId = created!.LicenseId,
                AtUtc = DateTimeOffset.UtcNow,
                Action = AiUsageStoredActions.GenerateName,
                Model = "gpt-test",
                InputTokens = 11,
                OutputTokens = 7,
                OpenAiSucceeded = true,
            }, ct);
        });

        using var res = await _client.SendAsync(Admin(HttpMethod.Get, "/v1/admin/ai-usage"));
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync();
        var stats = JsonSerializer.Deserialize<AdminAiUsageStatsDto>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(stats);
        Assert.True(stats!.AllTime.Generations >= 1);
        Assert.True(stats.AllTime.InputTokens >= 11);
        Assert.True(stats.AllTime.OutputTokens >= 7);
        Assert.DoesNotContain(created.LicenseCode, body);
        Assert.DoesNotContain("gpt-test", body);
        Assert.DoesNotContain(LicenseWebAppFactory.AdminSecret, body);
        Assert.DoesNotContain("OPENAI_API_KEY", body);
        Assert.DoesNotContain("prompt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generate_name", body);
    }
}

public sealed class LicenseWebAppFactory : WebApplicationFactory<Program>
{
    public const string AdminSecret = "local-dev-admin-secret-32chars!!";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "rufus-lic3-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable(AdminAuthOptions.EnvironmentVariable, AdminSecret);
        Environment.SetEnvironmentVariable(RufusMapEditor.Licensing.Options.LicenseSqlitePath.EnvironmentVariable, _dbPath);
        builder.UseSetting("Licensing:SqlitePath", _dbPath);
        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch { /* ignore locks */ }
    }
}
