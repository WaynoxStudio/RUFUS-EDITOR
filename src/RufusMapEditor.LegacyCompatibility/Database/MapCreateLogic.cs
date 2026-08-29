using System.Globalization;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Database;

/// <summary>
/// FASE 10B / HOTFIX 10B.2 — INSERT plan from live schema + RUFUS defaults (user overrides optional).
/// CREATE only: unset world coords become explicit 0,0. Does not apply to UPDATE / legacy documents.
/// </summary>
public static class MapCreateLogic
{
    public const string InitialRevision = "0";

    /// <summary>
    /// For NEW-map INSERT only: if the user left X/Y empty, treat (0,0) as an explicit valid position.
    /// Does not run on UPDATE; legacy maps without provenance stay undefined until sync/edit.
    /// </summary>
    public static void EnsureNewMapWorldCoordinates(MapDocument map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.WorldCoordinatesSet)
            return;
        map.WorldX = 0;
        map.WorldY = 0;
        map.WorldCoordinatesSet = true;
    }

    public static IReadOnlyList<string> ValidateDocumentForCreate(MapDocument map)
    {
        var errors = new List<string>();
        if (map.Id <= 0)
            errors.Add("MapId inválido.");
        if (map.Width <= 0 || map.Height <= 0)
            errors.Add("Ancho/Alto inválidos.");
        if (map.Outdoor is null)
            errors.Add("Outdoor (outDoor) no está definido.");
        // World coords: EnsureNewMapWorldCoordinates runs in BuildInsertPlan before this check path.
        // FromDocument (UPDATE) still rejects unset coords — legacy stays protected.

        MapCellEditor.SyncDocument(map);
        var expected = MapGeometry.ExpectedMapDataLength(map.Width, map.Height);
        var mapData = map.MapData ?? "";
        if (mapData.Length == 0)
            errors.Add("MapData vacío.");
        else if (mapData.Length != expected)
            errors.Add($"MapData length {mapData.Length} ≠ esperado {expected}.");

        return errors;
    }

    public static MapInsertPlan BuildInsertPlan(
        MapDocument map,
        MapTableSchema schema,
        NewMapDefaultsSettings? defaults)
    {
        EnsureNewMapWorldCoordinates(map);
        defaults ??= new NewMapDefaultsSettings();
        var docErrors = ValidateDocumentForCreate(map);
        if (docErrors.Count > 0)
        {
            return new MapInsertPlan
            {
                EditorValues = PlaceholderEditor(map),
                Columns = Array.Empty<InsertColumnPlan>(),
                MissingRequiredDefaults = docErrors,
            };
        }

        var editor = MapPublishLogic.FromDocument(map, InitialRevision);
        var columns = new List<InsertColumnPlan>();
        var missing = new List<string>();

        void AddEditor(string name, object value, string display) =>
            columns.Add(new InsertColumnPlan
            {
                ColumnName = name,
                Source = InsertColumnSource.Editor,
                Value = value,
                Display = display,
            });

        AddEditor(MapasColumns.Id, editor.Id, editor.Id.ToString(CultureInfo.InvariantCulture));
        AddEditor(MapasColumns.Fecha, editor.Fecha, editor.Fecha);
        AddEditor(MapasColumns.Ancho, editor.Ancho, S(editor.Ancho));
        AddEditor(MapasColumns.Alto, editor.Alto, S(editor.Alto));
        AddEditor(MapasColumns.BgId, editor.BgId, S(editor.BgId));
        AddEditor(MapasColumns.MusicId, editor.MusicId, S(editor.MusicId));
        AddEditor(MapasColumns.AmbienteId, editor.AmbienteId, S(editor.AmbienteId));
        AddEditor(MapasColumns.OutDoor, editor.OutDoor, S(editor.OutDoor));
        AddEditor(MapasColumns.Capabilities, editor.Capabilities, S(editor.Capabilities));
        AddEditor(MapasColumns.PosPelea, editor.PosPelea, Short(editor.PosPelea));
        AddEditor(MapasColumns.MapData, editor.MapData, $"{editor.MapData.Length} caracteres");
        AddEditor(MapasColumns.X, editor.X, S(editor.X));
        AddEditor(MapasColumns.Y, editor.Y, S(editor.Y));

        foreach (var colName in MapasColumns.Preserved)
        {
            var meta = schema.Find(colName);
            if (meta is null)
                continue;

            if (meta.HasDefault)
            {
                columns.Add(new InsertColumnPlan
                {
                    ColumnName = colName,
                    Source = InsertColumnSource.DatabaseDefault,
                    Value = null,
                    Display = $"DEFAULT BD ({meta.ColumnDefault})",
                });
                continue;
            }

            if (meta.IsNullable)
            {
                columns.Add(new InsertColumnPlan
                {
                    ColumnName = colName,
                    Source = InsertColumnSource.ExplicitNull,
                    Value = null,
                    Display = "NULL",
                });
                continue;
            }

            if (NewMapDefaultsSettings.TryResolve(defaults, colName, out var configured, out var fromUser))
            {
                columns.Add(new InsertColumnPlan
                {
                    ColumnName = colName,
                    Source = InsertColumnSource.ConfiguredValue,
                    Value = configured,
                    Display = fromUser
                        ? FormatValue(configured)
                        : $"RUFUS ({FormatValue(configured)})",
                });
                continue;
            }

            missing.Add(
                $"{colName} (NOT NULL, sin DEFAULT MySQL) — sin default RUFUS; configure en Configuración BD");
        }

        return new MapInsertPlan
        {
            EditorValues = editor,
            Columns = columns,
            MissingRequiredDefaults = missing,
        };
    }

    public static string FormatCreateSummary(MapInsertPlan plan, MapDocument map)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"CREAR MAPA {map.Id}");
        sb.AppendLine();
        sb.AppendLine($"Revisión:            {InitialRevision}");
        sb.AppendLine($"Tamaño:              {map.Width} × {map.Height}");
        sb.AppendLine($"Background:          {map.BackgroundId}");
        sb.AppendLine($"Música:              {map.MusicId}");
        sb.AppendLine($"Ambiente:            {map.AmbianceId}");
        sb.AppendLine($"Exterior:            {(map.Outdoor == true ? "Sí" : map.Outdoor == false ? "No" : "?")}");
        sb.AppendLine($"Capabilities:        {map.Capabilities}");
        sb.AppendLine();
        sb.AppendLine($"Coordenada X:        {map.WorldX}");
        sb.AppendLine($"Coordenada Y:        {map.WorldY}");
        sb.AppendLine();
        sb.AppendLine($"MapData:             {(map.MapData ?? "").Length} caracteres");
        sb.AppendLine($"Posiciones combate:  {Short(map.FightPlaces ?? "")}");
        sb.AppendLine();
        sb.AppendLine("VALORES PREDETERMINADOS:");
        foreach (var c in plan.Columns.Where(c =>
                     MapasColumns.Preserved.Contains(c.ColumnName, StringComparer.OrdinalIgnoreCase)))
            sb.AppendLine($"{c.ColumnName,-22} {c.Display}");
        if (plan.MissingRequiredDefaults.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BLOQUEADO — faltan defaults:");
            foreach (var m in plan.MissingRequiredDefaults)
                sb.AppendLine("  • " + m);
        }

        return sb.ToString();
    }

    private static MapPublishValues PlaceholderEditor(MapDocument map) => new()
    {
        Id = map.Id,
        Fecha = InitialRevision,
        Ancho = map.Width,
        Alto = map.Height,
        BgId = map.BackgroundId,
        MusicId = map.MusicId,
        AmbienteId = map.AmbianceId,
        OutDoor = map.Outdoor == true ? 1 : 0,
        Capabilities = map.Capabilities,
        PosPelea = map.FightPlaces ?? "",
        MapData = map.MapData ?? "",
        X = map.WorldX,
        Y = map.WorldY,
    };

    private static string S(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Short(string s) =>
        s.Length <= 48 ? s : s[..45] + "...";

    private static string FormatValue(object? v) =>
        v is null ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
}
