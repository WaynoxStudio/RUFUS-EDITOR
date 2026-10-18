namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Confirmed MySQL identifiers for estaticos.objetos_modelo.</summary>
public static class ObjetosModeloColumns
{
    public const string DefaultTable = "objetos_modelo";
    public const string Id = "id";
    public const string Nombre = "nombre";
}

public interface IObjetosModeloNameReadRepository
{
    /// <summary>Returns nombre when id exists; otherwise null.</summary>
    Task<string?> TryGetNombreAsync(int itemId, CancellationToken ct = default);
}

public sealed class MysqlObjetosModeloNameReadRepository : IObjetosModeloNameReadRepository
{
    private readonly string _cs;
    private readonly string _schema;

    public MysqlObjetosModeloNameReadRepository(
        RufusMapEditor.LegacyCompatibility.Database.DatabaseSettings settings,
        string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schema = string.IsNullOrWhiteSpace(settings.Database)
            ? "estaticos"
            : settings.Database.Trim();
    }

    public async Task<string?> TryGetNombreAsync(int itemId, CancellationToken ct = default)
    {
        if (itemId <= 0) return null;
        await using var conn = new MySqlConnector.MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT `{ObjetosModeloColumns.Nombre}` FROM `{_schema}`.`{ObjetosModeloColumns.DefaultTable}` WHERE `{ObjetosModeloColumns.Id}`=@id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", itemId);
        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (scalar is null or DBNull)
            return null;
        var name = Convert.ToString(scalar)?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

/// <summary>Process-wide cache for objetos_modelo.id → nombre (mission objective UI / preflight).</summary>
public sealed class ObjetosModeloNameCache
{
    public const string NotFoundMessage = "Objeto no encontrado.";

    private readonly Func<int, Task<string?>> _lookup;
    private readonly Dictionary<int, string?> _cache = new();
    private readonly object _gate = new();

    /// <summary>Optional process-wide instance used by publish preflight when configured by the App.</summary>
    public static ObjetosModeloNameCache? Shared { get; set; }

    public static Func<int, bool>? SharedExistsPredicate =>
        Shared is null ? null : Shared.Exists;

    public ObjetosModeloNameCache(Func<int, Task<string?>> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public static ObjetosModeloNameCache FromRepository(IObjetosModeloNameReadRepository repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return new ObjetosModeloNameCache(id => repo.TryGetNombreAsync(id));
    }

    public bool TryGetCached(int itemId, out string? nombre)
    {
        lock (_gate)
            return _cache.TryGetValue(itemId, out nombre);
    }

    public async Task<string?> ResolveAsync(int itemId, CancellationToken ct = default)
    {
        if (itemId <= 0) return null;
        lock (_gate)
        {
            if (_cache.TryGetValue(itemId, out var hit))
                return hit;
        }

        string? name;
        try
        {
            name = await _lookup(itemId).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        lock (_gate)
            _cache[itemId] = name;
        return name;
    }

    public string? ResolveSync(int itemId)
    {
        if (itemId <= 0) return null;
        if (TryGetCached(itemId, out var cached))
            return cached;
        return Task.Run(() => ResolveAsync(itemId)).GetAwaiter().GetResult();
    }

    public bool Exists(int itemId) =>
        itemId > 0 && !string.IsNullOrWhiteSpace(ResolveSync(itemId));
}
