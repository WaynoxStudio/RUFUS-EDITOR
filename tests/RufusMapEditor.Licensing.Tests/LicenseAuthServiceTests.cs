using System.Text.Json;
using RufusMapEditor.LegacyCompatibility.Content.Ai;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Model;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Security;
using RufusMapEditor.Licensing.Services;
using RufusMapEditor.Licensing.Sqlite;

namespace RufusMapEditor.Licensing.Tests;

public sealed class LicenseAuthServiceTests
{
    private static (SqliteLicenseUnitOfWork db, FakeServerClock clock, LicenseAuthService auth, AdminLicenseService admin) Create(
        LicenseLeaseOptions? lease = null)
    {
        var db = SqliteLicenseUnitOfWork.CreateInMemory();
        var clock = new FakeServerClock(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var auth = new LicenseAuthService(db, clock, lease ?? new LicenseLeaseOptions { LeaseSeconds = 600, HeartbeatSeconds = 120 });
        var admin = new AdminLicenseService(db, clock);
        return (db, clock, auth, admin);
    }

    private static async Task<string> CreateCodeAsync(AdminLicenseService admin, int days = 30, int maxDevices = 1, int maxSessions = 1, bool ai = true)
    {
        var created = await admin.CreateAsync(new CreateLicenseRequest
        {
            DurationDays = days,
            MaxDevices = maxDevices,
            MaxConcurrentSessions = maxSessions,
            PermissionEditor = true,
            PermissionAi = ai,
        });
        return created.LicenseCode;
    }

    [Fact]
    public async Task First_activation_sets_expiry_from_server_clock()
    {
        var (db, clock, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, days: 30);
            var r = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a", ClientVersion = "1.0" });
            Assert.True(r.Success);
            Assert.NotNull(r.Session);
            Assert.Equal(clock.UtcNow.AddDays(30), r.Session!.LicenseExpiresAt);
            Assert.Equal(LicenseStatus.Active, (await db.Licenses.ListAsync()).Single().Status);
        }
    }

    [Fact]
    public async Task Same_device_can_reopen_after_logout()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin);
            var a1 = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.True(a1.Success);
            await auth.LogoutAsync(new LogoutRequest { SessionToken = a1.Session!.SessionToken, DeviceId = "dev-a" });
            var a2 = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.True(a2.Success);
            Assert.NotEqual(a1.Session.SessionToken, a2.Session!.SessionToken);
        }
    }

    [Fact]
    public async Task Second_device_blocked_when_max_devices_is_1()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, maxDevices: 1);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            var b = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-b" });
            Assert.False(b.Success);
            Assert.Equal(LicenseErrorCodes.DeviceLimitReached, b.ErrorCode);
        }
    }

    [Fact]
    public async Task Two_devices_allowed_when_configured()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, maxDevices: 2, maxSessions: 2);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-b" })).Success);
        }
    }

    [Fact]
    public async Task Concurrent_session_limit_one()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, maxDevices: 2, maxSessions: 1);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            // Bind B by raising devices after A — create with 2 devices from start
        }

        var (db2, _, auth2, admin2) = Create();
        await using (db2)
        {
            var code = await CreateCodeAsync(admin2, maxDevices: 2, maxSessions: 1);
            Assert.True((await auth2.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            // Force second device bind via admin reset? Prefer: activate A, reset not needed —
            // With maxDevices=2 we need B bound first without session, then A session + B session.
            // Activate A (session), activate B should hit session limit if A lease still active.
            var b = await auth2.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-b" });
            Assert.False(b.Success);
            Assert.Equal(LicenseErrorCodes.SessionLimitReached, b.ErrorCode);
        }
    }

    [Fact]
    public async Task Expired_lease_frees_session_slot()
    {
        var lease = new LicenseLeaseOptions { LeaseSeconds = 60, HeartbeatSeconds = 30 };
        var (db, clock, auth, admin) = Create(lease);
        await using (db)
        {
            var code = await CreateCodeAsync(admin, maxDevices: 2, maxSessions: 1);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            clock.UtcNow = clock.UtcNow.AddMinutes(2);
            var b = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-b" });
            Assert.True(b.Success);
        }
    }

    [Fact]
    public async Task Suspended_blocks_activate_and_heartbeat()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin);
            var a = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.True(a.Success);
            var id = (await db.Licenses.ListAsync()).Single().Id;
            await admin.SuspendAsync(id);
            // LIC.5: sessions stay Active so heartbeat returns LICENSE_SUSPENDED (user-facing message).
            var hb = await auth.HeartbeatAsync(new HeartbeatRequest { SessionToken = a.Session!.SessionToken, DeviceId = "dev-a" });
            Assert.False(hb.Success);
            Assert.Equal(LicenseErrorCodes.LicenseSuspended, hb.ErrorCode);
            var again = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.False(again.Success);
            Assert.Equal(LicenseErrorCodes.LicenseSuspended, again.ErrorCode);
        }
    }

    [Fact]
    public async Task Revoked_blocks()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin);
            var id = (await db.Licenses.ListAsync()).Single().Id;
            await admin.RevokeAsync(id);
            var a = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.False(a.Success);
            Assert.Equal(LicenseErrorCodes.LicenseRevoked, a.ErrorCode);
        }
    }

    [Fact]
    public async Task Expired_license_blocks_even_if_status_still_Active()
    {
        var (db, clock, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, days: 10);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            clock.UtcNow = clock.UtcNow.AddDays(11);
            var again = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.False(again.Success);
            Assert.Equal(LicenseErrorCodes.LicenseExpired, again.ErrorCode);
        }
    }

    [Fact]
    public async Task Client_clock_irrelevant_server_clock_controls_expiry()
    {
        var (db, clock, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, days: 1);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            // Pretend client thinks it's still day 0 — only server clock matters
            clock.UtcNow = clock.UtcNow.AddDays(2);
            var r = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.Equal(LicenseErrorCodes.LicenseExpired, r.ErrorCode);
        }
    }

    [Fact]
    public async Task Permissions_editor_and_ai_reflected_in_session()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, ai: false);
            var r = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.True(r.Session!.Permissions.Editor);
            Assert.False(r.Session.Permissions.Ai);
        }
    }

    [Fact]
    public async Task Session_token_distinct_from_license_code_and_shared_ai_token()
    {
        var (db, _, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin);
            var r = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            var token = r.Session!.SessionToken;
            Assert.False(string.Equals(token, code, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(code.Replace("-", ""), token, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(AiBackendAccessTokenEnv.VariableName, token);
            Assert.NotEqual("RUFUS_AI_ACCESS_TOKEN", token);
            Assert.True(token.Length >= 32);
        }
    }

    [Fact]
    public async Task DeviceId_provider_fake_is_stable()
    {
        var p = new FakeDeviceIdProvider("abc123");
        Assert.Equal("abc123", p.GetDeviceId());
        Assert.Equal(p.GetDeviceId(), p.GetDeviceId());
    }

    [Fact]
    public async Task SessionStore_roundtrip_memory()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new LicenseSessionLocalState
        {
            SessionToken = "tok",
            DeviceId = "d1",
            PermissionAi = true,
            PermissionEditor = true,
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        });
        var loaded = await store.LoadAsync();
        Assert.Equal("tok", loaded!.SessionToken);
        await store.ClearAsync();
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public void Json_contracts_roundtrip()
    {
        var req = new ActivateLicenseRequest { LicenseCode = "RUF-AAAA-BBBB-CCCC-DDDD", DeviceId = "deadbeef", ClientVersion = "1.0" };
        var json = JsonSerializer.Serialize(req);
        var back = JsonSerializer.Deserialize<ActivateLicenseRequest>(json);
        Assert.Equal(req.LicenseCode, back!.LicenseCode);
        Assert.Equal(req.DeviceId, back.DeviceId);

        var session = new SessionSuccessResponse
        {
            SessionToken = "s",
            ExpiresAt = DateTimeOffset.Parse("2026-09-05T12:15:00Z"),
            Permissions = new LicensePermissionsDto { Editor = true, Ai = true },
            HeartbeatSeconds = 300,
        };
        var sjson = JsonSerializer.Serialize(session);
        Assert.Contains("sessionToken", sjson, StringComparison.Ordinal);
        Assert.Contains("permissions", sjson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sqlite_schema_isolated_from_dofus_names()
    {
        var path = Path.Combine(Path.GetTempPath(), "rufus-lic-test-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var db = new SqliteLicenseUnitOfWork(path);
            await db.Licenses.InsertAsync(new LicenseEntity
            {
                CodeHash = "abc",
                CodeDisplayHint = "hint",
                Status = LicenseStatus.Created,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
            });
            Assert.True(File.Exists(path));
            Assert.Single(await db.Licenses.ListAsync());
            await db.DisposeAsync();
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // best-effort cleanup on Windows file locks
            }
        }
    }

    [Fact]
    public void License_code_hash_not_plaintext_in_db_entity()
    {
        var code = LicenseCodeGenerator.Generate();
        var hash = LicenseCodeHasher.Hash(LicenseCodeGenerator.Normalize(code));
        Assert.NotEqual(code, hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Sqlite_path_uses_env_and_relative_default()
    {
        var prev = Environment.GetEnvironmentVariable(LicenseSqlitePath.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(LicenseSqlitePath.EnvironmentVariable, null);
            var def = LicenseSqlitePath.Resolve(baseDirectory: @"C:\app\backend");
            Assert.EndsWith(Path.Combine("data", "rufus-licenses.db"), def, StringComparison.OrdinalIgnoreCase);

            Environment.SetEnvironmentVariable(LicenseSqlitePath.EnvironmentVariable, @"D:\private\lic.db");
            Assert.Equal(Path.GetFullPath(@"D:\private\lic.db"), LicenseSqlitePath.Resolve());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseSqlitePath.EnvironmentVariable, prev);
        }
    }

    [Fact]
    public async Task SessionAccessTokenProvider_reads_store_when_ai_allowed()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new LicenseSessionLocalState
        {
            SessionToken = "session-xyz",
            PermissionAi = true,
            PermissionEditor = true,
            DeviceId = "d",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        });
        var provider = new SessionAccessTokenProvider(store);
        Assert.Equal("session-xyz", provider.TryGetAccessToken());

        await store.SaveAsync(new LicenseSessionLocalState
        {
            SessionToken = "session-xyz",
            PermissionAi = false,
            PermissionEditor = true,
            DeviceId = "d",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        });
        Assert.Null(provider.TryGetAccessToken());
    }

    [Fact]
    public void Environment_ai_token_provider_still_exists()
    {
        IAiBackendAccessTokenProvider env = new EnvironmentAiBackendAccessTokenProvider();
        _ = env.TryGetAccessToken(); // must not throw; still the development path
    }

    [Fact]
    public async Task Admin_extend_and_reset_device()
    {
        var (db, clock, auth, admin) = Create();
        await using (db)
        {
            var code = await CreateCodeAsync(admin, days: 30);
            Assert.True((await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" })).Success);
            var id = (await db.Licenses.ListAsync()).Single().Id;
            var before = (await db.Licenses.GetByIdAsync(id))!.ExpiresAtUtc;
            await admin.ExtendAsync(id, 30);
            var after = (await db.Licenses.GetByIdAsync(id))!.ExpiresAtUtc;
            Assert.Equal(before!.Value.AddDays(30), after);

            await admin.ResetDevicesAsync(id);
            var b = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-b" });
            Assert.True(b.Success);

            await admin.ResetDevicesAsync(id);
            // Same device after reset must re-bind the Reset row (no UNIQUE crash).
            var aAgain = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.True(aAgain.Success);
        }
    }

    [Fact]
    public async Task Heartbeat_renews_lease()
    {
        var lease = new LicenseLeaseOptions { LeaseSeconds = 100, HeartbeatSeconds = 40 };
        var (db, clock, auth, admin) = Create(lease);
        await using (db)
        {
            var code = await CreateCodeAsync(admin);
            var a = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            clock.UtcNow = clock.UtcNow.AddSeconds(50);
            var hb = await auth.HeartbeatAsync(new HeartbeatRequest { SessionToken = a.Session!.SessionToken, DeviceId = "dev-a" });
            Assert.True(hb.Success);
            Assert.Equal(clock.UtcNow.AddSeconds(100), hb.Session!.ExpiresAt);
        }
    }

    [Fact]
    public async Task Heartbeat_renews_after_soft_lease_expiry()
    {
        var lease = new LicenseLeaseOptions { LeaseSeconds = 60, HeartbeatSeconds = 30 };
        var (db, clock, auth, admin) = Create(lease);
        await using (db)
        {
            var code = await CreateCodeAsync(admin);
            var a = await auth.ActivateAsync(new ActivateLicenseRequest { LicenseCode = code, DeviceId = "dev-a" });
            Assert.True(a.Success);

            // Past lease without heartbeat — previously returned SESSION_INVALID every relaunch.
            clock.UtcNow = clock.UtcNow.AddSeconds(120);
            var hb = await auth.HeartbeatAsync(new HeartbeatRequest
            {
                SessionToken = a.Session!.SessionToken,
                DeviceId = "dev-a",
            });
            Assert.True(hb.Success);
            Assert.Equal(a.Session.SessionToken, hb.Session!.SessionToken);
            Assert.Equal(clock.UtcNow.AddSeconds(60), hb.Session.ExpiresAt);
        }
    }
}
