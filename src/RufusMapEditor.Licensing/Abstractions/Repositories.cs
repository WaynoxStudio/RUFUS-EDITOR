using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Abstractions;

public interface ILicenseRepository
{
    Task<LicenseEntity?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<LicenseEntity?> GetByCodeHashAsync(string codeHash, CancellationToken ct = default);
    Task<IReadOnlyList<LicenseEntity>> ListAsync(CancellationToken ct = default);
    Task<LicenseEntity> InsertAsync(LicenseEntity entity, CancellationToken ct = default);
    Task UpdateAsync(LicenseEntity entity, CancellationToken ct = default);
    /// <summary>Physical delete including sessions, devices, AI usage and quota rows.</summary>
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceEntity>> ListBoundByLicenseAsync(long licenseId, CancellationToken ct = default);
    Task<DeviceEntity?> GetBoundAsync(long licenseId, string deviceId, CancellationToken ct = default);
    /// <summary>Any row for (license, device), including Reset — needed to re-bind after admin reset.</summary>
    Task<DeviceEntity?> GetAnyAsync(long licenseId, string deviceId, CancellationToken ct = default);
    Task<DeviceEntity> InsertAsync(DeviceEntity entity, CancellationToken ct = default);
    Task UpdateAsync(DeviceEntity entity, CancellationToken ct = default);
    Task ResetAllBoundAsync(long licenseId, DateTimeOffset atUtc, CancellationToken ct = default);
}

public interface ISessionRepository
{
    Task<SessionEntity?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<SessionEntity>> ListActiveByLicenseAsync(long licenseId, CancellationToken ct = default);
    Task<SessionEntity> InsertAsync(SessionEntity entity, CancellationToken ct = default);
    Task UpdateAsync(SessionEntity entity, CancellationToken ct = default);
    /// <summary>Marks Active sessions whose lease already expired as Expired.</summary>
    Task ExpireLeasesAsync(long licenseId, DateTimeOffset nowUtc, CancellationToken ct = default);
}

public interface IAdminAuditRepository
{
    Task AppendAsync(AdminAuditEntity entry, CancellationToken ct = default);
    Task<IReadOnlyList<AdminAuditEntity>> ListRecentAsync(int take, CancellationToken ct = default);
}

/// <summary>Unit of work for atomic activate/bind/session/AI quota.</summary>
public interface ILicenseUnitOfWork
{
    ILicenseRepository Licenses { get; }
    IDeviceRepository Devices { get; }
    ISessionRepository Sessions { get; }
    IAdminAuditRepository Audit { get; }
    IAiUsageRepository AiUsage { get; }
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);
}
