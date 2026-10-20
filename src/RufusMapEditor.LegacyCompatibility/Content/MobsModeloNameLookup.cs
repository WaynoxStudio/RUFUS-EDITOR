using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Confirmed MySQL identifiers for estaticos.mobs_modelo (mission DefeatMobs UI / preflight).</summary>
public static class MobsModeloNameColumns
{
    public const string DefaultTable = MobsModeloColumns.DefaultTable;
    public const string Id = MobsModeloColumns.Id;
    public const string Nombre = MobsModeloColumns.Nombre;
}

public interface IMobsModeloNameReadRepository
{
    /// <summary>Returns nombre when id exists; otherwise null.</summary>
    Task<string?> TryGetNombreAsync(int mobId, CancellationToken ct = default);
}

public sealed class MysqlMobsModeloNameReadRepository : IMobsModeloNameReadRepository
{
    private readonly string _cs;
    private readonly string _schema;

    public MysqlMobsModeloNameReadRepository(
        RufusMapEditor.LegacyCompatibility.Database.DatabaseSettings settings,
        string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schema = string.IsNullOrWhiteSpace(settings.Database)
            ? MobsModeloColumns.DefaultDatabase
            : settings.Database.Trim();
    }

    public async Task<string?> TryGetNombreAsync(int mobId, CancellationToken ct = default)
    {
        if (mobId <= 0) return null;
        await using var conn = new MySqlConnector.MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT `{MobsModeloNameColumns.Nombre}` FROM `{_schema}`.`{MobsModeloNameColumns.DefaultTable}` WHERE `{MobsModeloNameColumns.Id}`=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", mobId);
        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (scalar is null or DBNull)
            return null;
        var name = Convert.ToString(scalar)?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

/// <summary>Process-wide cache for mobs_modelo.id → nombre.</summary>
public sealed class MobsModeloNameCache
{
    public const string NotFoundMessage = "Monstruo no encontrado.";

    private readonly Func<int, Task<string?>> _lookup;
    private readonly Dictionary<int, string?> _cache = new();
    private readonly object _gate = new();

    public static MobsModeloNameCache? Shared { get; set; }

    public static Func<int, bool>? SharedExistsPredicate =>
        Shared is null ? null : Shared.Exists;

    public MobsModeloNameCache(Func<int, Task<string?>> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public static MobsModeloNameCache FromRepository(IMobsModeloNameReadRepository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return new MobsModeloNameCache(id => repo.TryGetNombreAsync(id));
    }

    public bool TryGetCached(int mobId, out string? nombre)
    {
        lock (_gate)
            return _cache.TryGetValue(mobId, out nombre);
    }

    public async Task<string?> ResolveAsync(int mobId, CancellationToken ct = default)
    {
        if (mobId <= 0) return null;
        lock (_gate)
        {
            if (_cache.TryGetValue(mobId, out var hit))
                return hit;
        }

        string? name;
        try
        {
            name = await _lookup(mobId).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        lock (_gate)
            _cache[mobId] = name;
        return name;
    }

    public string? ResolveSync(int mobId)
    {
        if (mobId <= 0) return null;
        if (TryGetCached(mobId, out var cached))
            return cached;
        return Task.Run(() => ResolveAsync(mobId)).GetAwaiter().GetResult();
    }

    public bool Exists(int mobId) =>
        mobId > 0 && !string.IsNullOrWhiteSpace(ResolveSync(mobId));
}
