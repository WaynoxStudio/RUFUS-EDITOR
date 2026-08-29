using Microsoft.Data.Sqlite;
using RufusMapEditor.Licensing.Abstractions;
using RufusMapEditor.Licensing.Model;

namespace RufusMapEditor.Licensing.Sqlite;

/// <summary>
/// Private RUFUS license DB (SQLite). Table prefix rufus_* — never DOFUS estaticos/mapas/npcs.
/// </summary>
public sealed class SqliteLicenseUnitOfWork : ILicenseUnitOfWork, IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _ownsConnection;

    public SqliteLicenseUnitOfWork(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        _connection.Open();
        _ownsConnection = true;
        ApplySchema(_connection);
        Licenses = new SqliteLicenseRepository(_connection);
        Devices = new SqliteDeviceRepository(_connection);
        Sessions = new SqliteSessionRepository(_connection);
        Audit = new SqliteAdminAuditRepository(_connection);
        AiUsage = new SqliteAiUsageRepository(_connection);
    }

    /// <summary>In-memory DB for tests.</summary>
    public static SqliteLicenseUnitOfWork CreateInMemory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        ApplySchema(conn);
        return new SqliteLicenseUnitOfWork(conn, ownsConnection: true);
    }

    private SqliteLicenseUnitOfWork(SqliteConnection connection, bool ownsConnection)
    {
        _connection = connection;
        _ownsConnection = ownsConnection;
        Licenses = new SqliteLicenseRepository(_connection);
        Devices = new SqliteDeviceRepository(_connection);
        Sessions = new SqliteSessionRepository(_connection);
        Audit = new SqliteAdminAuditRepository(_connection);
        AiUsage = new SqliteAiUsageRepository(_connection);
    }

    public ILicenseRepository Licenses { get; }
    public IDeviceRepository Devices { get; }
    public ISessionRepository Sessions { get; }
    public IAdminAuditRepository Audit { get; }
    public IAiUsageRepository AiUsage { get; }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(ct);
        try
        {
            ((SqliteLicenseRepository)Licenses).CurrentTransaction = tx;
            ((SqliteDeviceRepository)Devices).CurrentTransaction = tx;
            ((SqliteSessionRepository)Sessions).CurrentTransaction = tx;
            ((SqliteAdminAuditRepository)Audit).CurrentTransaction = tx;
            ((SqliteAiUsageRepository)AiUsage).CurrentTransaction = tx;

            await work(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            ((SqliteLicenseRepository)Licenses).CurrentTransaction = null;
            ((SqliteDeviceRepository)Devices).CurrentTransaction = null;
            ((SqliteSessionRepository)Sessions).CurrentTransaction = null;
            ((SqliteAdminAuditRepository)Audit).CurrentTransaction = null;
            ((SqliteAiUsageRepository)AiUsage).CurrentTransaction = null;
        }
    }

    internal static void ApplySchema(SqliteConnection connection) => LicenseSqliteSchema.Apply(connection);

    public void Dispose()
    {
        if (_ownsConnection)
            _connection.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsConnection)
            return _connection.DisposeAsync();
        return ValueTask.CompletedTask;
    }
}
