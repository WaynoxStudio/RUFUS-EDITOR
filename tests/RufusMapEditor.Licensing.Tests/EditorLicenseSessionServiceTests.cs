using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Client;
using RufusMapEditor.Licensing.Contracts;
using RufusMapEditor.Licensing.Contracts.Admin;
using RufusMapEditor.Licensing.Options;
using RufusMapEditor.Licensing.Services;
using RufusMapEditor.Licensing.Sqlite;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.Licensing.Tests;

public sealed class EditorLicenseSessionServiceTests
{
    private static (SqliteLicenseUnitOfWork db, FakeServerClock clock, EditorLicenseSessionService editor, AdminLicenseService admin, MemorySessionStore store) Create(
        LicenseLeaseOptions? lease = null)
    {
        var db = SqliteLicenseUnitOfWork.CreateInMemory();
        var clock = new FakeServerClock(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        lease ??= new LicenseLeaseOptions { LeaseSeconds = 900, HeartbeatSeconds = 300 };
        var auth = new LicenseAuthService(db, clock, lease);
        var admin = new AdminLicenseService(db, clock);
        var store = new MemorySessionStore();
        var devices = new FakeDeviceIdProvider("dev-a");
        var editor = new EditorLicenseSessionService(
            new InProcessLicenseClient(auth),
            store,
            devices,
            lease,
            clientVersion: "lic5-test",
            utcNow: () => clock.UtcNow);
        return (db, clock, editor, admin, store);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("ON", true)]
    [InlineData("yes", true)]
    public void LicenseTestOptions_parses_truthy(string? raw, bool expected)
    {
        Assert.Equal(expected, LicenseTestOptions.IsTruthy(raw));
    }

    [Fact]
    public async Task Resume_without_store_needs_activation()
    {
        var (db, _, editor, _, _) = Create();
        await using (db)
        {
            var r = await editor.TryResumeAsync();
            Assert.Equal(LicenseGateOutcome.NeedsActivation, r.Outcome);
        }
    }

    [Fact]
    public async Task Activate_then_resume_validates_against_backend()
    {
        var (db, _, editor, admin, store) = Create();
        await using (db)
        {
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
                PermissionAi = true,
            })).LicenseCode;

            var act = await editor.ActivateAsync(code);
            Assert.Equal(LicenseGateOutcome.Authorized, act.Outcome);
            Assert.NotNull(await store.LoadAsync());

            var resume = await editor.TryResumeAsync();
            Assert.Equal(LicenseGateOutcome.Authorized, resume.Outcome);
            Assert.True(resume.Session!.PermissionEditor);
            Assert.True(resume.Session.PermissionAi);
        }
    }

    [Fact]
    public async Task Explicit_reject_clears_store()
    {
        var (db, _, editor, admin, store) = Create();
        await using (db)
        {
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
                PermissionAi = false,
            })).LicenseCode;
            Assert.Equal(LicenseGateOutcome.Authorized, (await editor.ActivateAsync(code)).Outcome);
            var id = (await db.Licenses.ListAsync()).Single().Id;
            await admin.SuspendAsync(id);

            var hb = await editor.HeartbeatAsync();
            Assert.Equal(LicenseGateOutcome.Denied, hb.Outcome);
            Assert.Equal(LicenseErrorCodes.LicenseSuspended, hb.ErrorCode);
            Assert.Null(await store.LoadAsync());
            Assert.Equal(LicenseUserMessages.Suspended, hb.UserMessage);
        }
    }

    [Fact]
    public async Task Editor_permission_false_denied()
    {
        var (db, _, editor, admin, store) = Create();
        await using (db)
        {
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = false,
                PermissionAi = true,
            })).LicenseCode;
            var act = await editor.ActivateAsync(code);
            Assert.Equal(LicenseGateOutcome.Denied, act.Outcome);
            Assert.Equal(LicenseErrorCodes.EditorNotAllowed, act.ErrorCode);
            Assert.Null(await store.LoadAsync());
        }
    }

    [Fact]
    public async Task Device_limit_message()
    {
        var (db, clock, _, admin, _) = Create();
        await using (db)
        {
            var lease = new LicenseLeaseOptions { LeaseSeconds = 900, HeartbeatSeconds = 300 };
            var auth = new LicenseAuthService(db, clock, lease);
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
                PermissionAi = false,
            })).LicenseCode;

            var a = new EditorLicenseSessionService(
                new InProcessLicenseClient(auth), new MemorySessionStore(), new FakeDeviceIdProvider("dev-a"), lease,
                utcNow: () => clock.UtcNow);
            Assert.Equal(LicenseGateOutcome.Authorized, (await a.ActivateAsync(code)).Outcome);

            var b = new EditorLicenseSessionService(
                new InProcessLicenseClient(auth), new MemorySessionStore(), new FakeDeviceIdProvider("dev-b"), lease,
                utcNow: () => clock.UtcNow);
            var denied = await b.ActivateAsync(code);
            Assert.Equal(LicenseGateOutcome.Denied, denied.Outcome);
            Assert.Equal(LicenseUserMessages.DeviceLinkedElsewhere, denied.UserMessage);
        }
    }

    [Fact]
    public async Task LogoutBestEffort_does_not_clear_local_store()
    {
        var (db, _, editor, admin, store) = Create();
        await using (db)
        {
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
                PermissionAi = true,
            })).LicenseCode;

            Assert.Equal(LicenseGateOutcome.Authorized, (await editor.ActivateAsync(code)).Outcome);
            Assert.NotNull(await store.LoadAsync());

            await editor.LogoutBestEffortAsync();
            var local = await store.LoadAsync();
            Assert.NotNull(local);
            Assert.False(string.IsNullOrWhiteSpace(local!.SessionToken));
            Assert.Equal(code, local.LicenseCode);
        }
    }

    [Fact]
    public async Task TryResume_silently_reactivates_when_lease_soft_expired()
    {
        var lease = new LicenseLeaseOptions { LeaseSeconds = 30, HeartbeatSeconds = 10 };
        var (db, clock, editor, admin, store) = Create(lease);
        await using (db)
        {
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
                PermissionAi = false,
            })).LicenseCode;

            Assert.Equal(LicenseGateOutcome.Authorized, (await editor.ActivateAsync(code)).Outcome);
            Assert.Equal(code, (await store.LoadAsync())!.LicenseCode);

            // Simulate pre-fix backend that killed the lease (or closed session via activate rotate).
            clock.UtcNow = clock.UtcNow.AddMinutes(10);
            // Force SESSION_INVALID path: close session on server while keeping local token+code.
            var auth = new LicenseAuthService(db, clock, lease);
            await auth.LogoutAsync(new LogoutRequest
            {
                SessionToken = (await store.LoadAsync())!.SessionToken,
                DeviceId = "dev-a",
            });

            var resume = await editor.TryResumeAsync();
            Assert.Equal(LicenseGateOutcome.Authorized, resume.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(resume.Session?.SessionToken));
            Assert.Equal(code, (await store.LoadAsync())!.LicenseCode);
        }
    }

    [Fact]
    public async Task ClearLocalAsync_removes_store_for_explicit_logout()
    {
        var (db, _, editor, admin, store) = Create();
        await using (db)
        {
            var code = (await admin.CreateAsync(new CreateLicenseRequest
            {
                DurationDays = 1,
                MaxDevices = 1,
                MaxConcurrentSessions = 1,
                PermissionEditor = true,
                PermissionAi = true,
            })).LicenseCode;

            await editor.ActivateAsync(code);
            await editor.LogoutBestEffortAsync();
            await editor.ClearLocalAsync();
            Assert.Null(await store.LoadAsync());

            var resume = await editor.TryResumeAsync();
            Assert.Equal(LicenseGateOutcome.NeedsActivation, resume.Outcome);
        }
    }

    [Fact]
    public async Task SessionAccessTokenProvider_reads_ai_permission_from_store()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new LicenseSessionLocalState
        {
            SessionToken = "tok-ai",
            PermissionAi = true,
            PermissionEditor = true,
            DeviceId = "d",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        });

        var provider = new SessionAccessTokenProvider(store);
        Assert.Equal("tok-ai", provider.TryGetAccessToken());

        await store.SaveAsync(new LicenseSessionLocalState
        {
            SessionToken = "tok-no",
            PermissionAi = false,
            PermissionEditor = true,
            DeviceId = "d",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        });
        Assert.Null(provider.TryGetAccessToken());
    }

    [Fact]
    public void UserMessages_map_codes()
    {
        Assert.Equal(LicenseUserMessages.Expired, LicenseUserMessages.ForErrorCode(LicenseErrorCodes.LicenseExpired));
        Assert.Equal(LicenseUserMessages.Revoked, LicenseUserMessages.ForErrorCode(LicenseErrorCodes.LicenseRevoked));
        Assert.True(LicenseUserMessages.IsExplicitRejection(LicenseErrorCodes.SessionInvalid));
        Assert.False(LicenseUserMessages.IsExplicitRejection(LicenseErrorCodes.NetworkUnavailable));
    }
}
