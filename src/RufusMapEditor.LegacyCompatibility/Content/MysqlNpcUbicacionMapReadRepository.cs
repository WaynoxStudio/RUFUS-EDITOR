using System.Globalization;
using MySqlConnector;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>
/// COORDS_AND_PUBLISHED_REPAIR.6 — read npcs_ubicacion.mapa for an NPC (Cell ID ignored).
/// </summary>
public interface INpcUbicacionMapReadRepository
{
    /// <summary>Returns mapa when &gt; 0; otherwise null. Never derives X/Y from celda.</summary>
    Task<int?> TryGetMapIdAsync(int npcId, CancellationToken ct = default);
}

public sealed class MysqlNpcUbicacionMapReadRepository : INpcUbicacionMapReadRepository
{
    private readonly string _cs;
    private readonly string _schema;

    public MysqlNpcUbicacionMapReadRepository(DatabaseSettings settings, string plainPassword)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cs = settings.BuildConnectionString(plainPassword);
        _schema = string.IsNullOrWhiteSpace(settings.Database)
            ? MapasColumns.DefaultDatabase
            : settings.Database.Trim();
    }

    public async Task<int?> TryGetMapIdAsync(int npcId, CancellationToken ct = default)
    {
        if (npcId <= 0) return null;
        await using var conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT `{NpcsUbicacionColumns.Mapa}` FROM `{_schema}`.`{NpcsUbicacionColumns.DefaultTable}` WHERE `{NpcsUbicacionColumns.Npc}`=@npc LIMIT 1");
        cmd.Parameters.AddWithValue("@npc", npcId);
        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (scalar is null or DBNull)
            return null;
        var mapId = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        return mapId > 0 ? mapId : null;
    }
}
