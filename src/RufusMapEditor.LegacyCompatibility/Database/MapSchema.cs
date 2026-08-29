namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>One column from INFORMATION_SCHEMA.COLUMNS (read-only audit).</summary>
public sealed class MapColumnSchema
{
    public required string ColumnName { get; init; }
    public required string DataType { get; init; }
    public required string ColumnType { get; init; }
    public required bool IsNullable { get; init; }
    /// <summary>Raw COLUMN_DEFAULT; null means no DEFAULT clause.</summary>
    public string? ColumnDefault { get; init; }
    public long? CharacterMaximumLength { get; init; }
    public int? NumericPrecision { get; init; }
    public int? NumericScale { get; init; }
    public string ColumnKey { get; init; } = "";
    public string Extra { get; init; } = "";
    public int OrdinalPosition { get; init; }

    public bool HasDefault => ColumnDefault is not null;
    public bool IsPrimaryKey => ColumnKey.Contains("PRI", StringComparison.OrdinalIgnoreCase);
    public bool IsAutoIncrement => Extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase);
}

public sealed class MapTableSchema
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required IReadOnlyList<MapColumnSchema> Columns { get; init; }

    public MapColumnSchema? Find(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.ColumnName, name, StringComparison.OrdinalIgnoreCase));

    public bool IdIsPrimaryKey => Find(MapasColumns.Id)?.IsPrimaryKey == true;
    public bool IdIsAutoIncrement => Find(MapasColumns.Id)?.IsAutoIncrement == true;
}

public enum InsertColumnSource
{
    Editor,
    DatabaseDefault,
    ExplicitNull,
    ConfiguredValue,
}

public sealed class InsertColumnPlan
{
    public required string ColumnName { get; init; }
    public required InsertColumnSource Source { get; init; }
    public object? Value { get; init; }
    public string Display { get; init; } = "";
}

public sealed class MapInsertPlan
{
    public required MapPublishValues EditorValues { get; init; }
    public required IReadOnlyList<InsertColumnPlan> Columns { get; init; }
    public IReadOnlyList<string> MissingRequiredDefaults { get; init; } = Array.Empty<string>();
    public bool CanInsert => MissingRequiredDefaults.Count == 0;

    public IEnumerable<InsertColumnPlan> Included =>
        Columns.Where(c => c.Source is InsertColumnSource.Editor
            or InsertColumnSource.ConfiguredValue
            or InsertColumnSource.ExplicitNull);
}

/// <summary>
/// Optional user overrides for secondary columns on INSERT (FASE 10B / HOTFIX 10B.2).
/// Null property = use <see cref="RufusBuiltIn"/> when MySQL has no DEFAULT and the column is NOT NULL.
/// Configuración BD is advanced override only — not required for normal create.
/// </summary>
public sealed class NewMapDefaultsSettings
{
    /// <summary>Confirmed RUFUS defaults for NEW map INSERT when MySQL lacks DEFAULT.</summary>
    public static NewMapDefaultsSettings RufusBuiltIn { get; } = new()
    {
        Key = "",
        Mobs = "",
        SubArea = 0,
        MaxGrupoMobs = 4,
        MaxMobsPorGrupo = 8,
        MinNivelGrupoMob = 0,
        MaxNivelGrupoMob = 0,
        MaxMercantes = 5,
        MaxPeleas = 99,
        MinMobsPorGrupo = 1,
    };

    public string? Key { get; set; }
    public string? Mobs { get; set; }
    public int? SubArea { get; set; }
    public int? MaxGrupoMobs { get; set; }
    public int? MaxMobsPorGrupo { get; set; }
    public int? MinNivelGrupoMob { get; set; }
    public int? MaxNivelGrupoMob { get; set; }
    public int? MaxMercantes { get; set; }
    public int? MaxPeleas { get; set; }
    public int? MinMobsPorGrupo { get; set; }

    public bool TryGetConfigured(string columnName, out object? value)
    {
        value = null;
        switch (columnName)
        {
            case var n when n.Equals(MapasColumns.Key, StringComparison.OrdinalIgnoreCase) && Key is not null:
                value = Key;
                return true;
            case var n when n.Equals(MapasColumns.Mobs, StringComparison.OrdinalIgnoreCase) && Mobs is not null:
                value = Mobs;
                return true;
            case var n when n.Equals(MapasColumns.SubArea, StringComparison.OrdinalIgnoreCase) && SubArea is not null:
                value = SubArea.Value;
                return true;
            case var n when n.Equals(MapasColumns.MaxGrupoMobs, StringComparison.OrdinalIgnoreCase) && MaxGrupoMobs is not null:
                value = MaxGrupoMobs.Value;
                return true;
            case var n when n.Equals(MapasColumns.MaxMobsPorGrupo, StringComparison.OrdinalIgnoreCase) && MaxMobsPorGrupo is not null:
                value = MaxMobsPorGrupo.Value;
                return true;
            case var n when n.Equals(MapasColumns.MinNivelGrupoMob, StringComparison.OrdinalIgnoreCase) && MinNivelGrupoMob is not null:
                value = MinNivelGrupoMob.Value;
                return true;
            case var n when n.Equals(MapasColumns.MaxNivelGrupoMob, StringComparison.OrdinalIgnoreCase) && MaxNivelGrupoMob is not null:
                value = MaxNivelGrupoMob.Value;
                return true;
            case var n when n.Equals(MapasColumns.MaxMercantes, StringComparison.OrdinalIgnoreCase) && MaxMercantes is not null:
                value = MaxMercantes.Value;
                return true;
            case var n when n.Equals(MapasColumns.MaxPeleas, StringComparison.OrdinalIgnoreCase) && MaxPeleas is not null:
                value = MaxPeleas.Value;
                return true;
            case var n when n.Equals(MapasColumns.MinMobsPorGrupo, StringComparison.OrdinalIgnoreCase) && MinMobsPorGrupo is not null:
                value = MinMobsPorGrupo.Value;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// User override if set; otherwise RUFUS built-in for known preserved columns.
    /// </summary>
    public static bool TryResolve(NewMapDefaultsSettings? user, string columnName, out object? value, out bool fromUser)
    {
        user ??= new NewMapDefaultsSettings();
        if (user.TryGetConfigured(columnName, out value))
        {
            fromUser = true;
            return true;
        }

        fromUser = false;
        return RufusBuiltIn.TryGetConfigured(columnName, out value);
    }
}
