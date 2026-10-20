using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.1 — typed objective fields → server args via existing codec (no new formats).</summary>
public sealed class MissionObjectiveUiFields
{
    public int Tipo { get; set; }
    public string ManualText { get; set; } = "";
    public string NpcId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Qty { get; set; } = "1";
    public string MobId { get; set; } = "";
    /// <summary>Optional multi-pair DefeatMobs; when set, overrides MobId/Qty.</summary>
    public IReadOnlyList<(int MobId, int Qty)>? MobPairs { get; set; }
    public string MapId { get; set; } = "";
    public string AreaId { get; set; } = "";
    public string Level { get; set; } = "1";
    public string SpellCount { get; set; } = "1";
    public string JobCount { get; set; } = "1";
    public string JobLevel { get; set; } = "1";
    public bool RestrictCoords { get; set; }
    public string X { get; set; } = "";
    public string Y { get; set; } = "";
}

public static class MissionObjectiveUiSync
{
    public const string UnsupportedManualMessage =
        "El objetivo Manual (tipo 0) no está soportado. Elimínalo o sustitúyelo.";

    public const string UnsupportedManualUiLabel = "Objetivo Manual no soportado";

    /// <summary>Visible UI label only — does not change tipo / args / persistence.</summary>
    public static string UiTypeLabel(int tipo) => tipo switch
    {
        MissionObjectiveTypes.Manual => UnsupportedManualUiLabel,
        MissionObjectiveTypes.TalkToNpc => "Habla con un NPC",
        MissionObjectiveTypes.ShowItemToNpc => "Enseña un objeto",
        MissionObjectiveTypes.DeliverItemsToNpc => "Entrega un objeto",
        MissionObjectiveTypes.DiscoverMap => "Ve a un mapa",
        MissionObjectiveTypes.DiscoverArea => "Ve a una zona",
        MissionObjectiveTypes.DefeatMobs => "Vence a monstruos",
        MissionObjectiveTypes.UseItem => "Usa un objeto",
        MissionObjectiveTypes.ReturnToNpc => "Vuelve con un NPC",
        MissionObjectiveTypes.ReachLevel => "Alcanza un nivel",
        MissionObjectiveTypes.HaveSpells => "Sube un hechizo",
        MissionObjectiveTypes.JobLevel => "Sube un oficio",
        MissionObjectiveTypes.DeliverSouls => "Entregar almas",
        MissionObjectiveTypes.DefeatMobAlt => "Monstruo (alt.)",
        MissionObjectiveTypes.Free10 => "Tipo 10 (avanzado)",
        MissionObjectiveTypes.Free11 => "Tipo 11 (avanzado)",
        MissionObjectiveTypes.Free13 => "Tipo 13 (avanzado)",
        _ => MissionObjectiveTypes.DisplayName(tipo),
    };

    /// <summary>Returns human error if fields cannot build args; otherwise null and updates draft.Args/Detalle.</summary>
    public static string? TryApply(MissionObjectiveDraft draft, MissionObjectiveUiFields fields, bool overwriteDetalle = true)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Tipo == MissionObjectiveTypes.Manual)
            return UnsupportedManualMessage;

        if (fields.Tipo == MissionObjectiveTypes.DeliverSouls)
            return "Entregar almas aún no está disponible (dato pendiente).";

        int? x = null, y = null;
        if (MissionObjectiveTypes.SupportsCoordinates(fields.Tipo) && fields.RestrictCoords)
        {
            if (!TryParseInt(fields.X, out var xv) || !TryParseInt(fields.Y, out var yv))
                return "Indica coordenadas X e Y, o desactiva la restricción.";
            x = xv;
            y = yv;
        }

        try
        {
            draft.Tipo = fields.Tipo;
            draft.Args = BuildArgs(fields, x, y);
            draft.EsAlHablar = "0";
            if (overwriteDetalle
                || string.IsNullOrWhiteSpace(draft.Detalle)
                || LooksGenerated(draft.Detalle))
            {
                draft.Detalle = MissionObjectiveArgsCodec.SuggestDetalle(
                    fields.Tipo, draft.Args, fields.ManualText);
            }
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public static string BuildArgs(MissionObjectiveUiFields fields, int? x, int? y) => fields.Tipo switch
    {
        MissionObjectiveTypes.Manual =>
            throw new InvalidOperationException(UnsupportedManualMessage),
        MissionObjectiveTypes.TalkToNpc =>
            MissionObjectiveArgsCodec.BuildTalk(RequireInt(fields.NpcId, "Selecciona el NPC de destino."), x, y),
        MissionObjectiveTypes.ReturnToNpc =>
            MissionObjectiveArgsCodec.BuildTalk(RequireInt(fields.NpcId, "Selecciona el NPC."), x, y),
        MissionObjectiveTypes.ShowItemToNpc => MissionObjectiveArgsCodec.BuildShowOrDeliver(
            RequireInt(fields.NpcId, MissionShowDeliverNpcBinding.MissingStartNpcMessage),
            RequirePositive(fields.ItemId, "Indica un Item ID."),
            RequirePositive(fields.Qty, "La cantidad debe ser mayor que 0."),
            x, y),
        MissionObjectiveTypes.DeliverItemsToNpc => MissionObjectiveArgsCodec.BuildShowOrDeliver(
            RequireInt(fields.NpcId, MissionShowDeliverNpcBinding.MissingStartNpcMessage),
            RequirePositive(fields.ItemId, "Indica un Item ID."),
            RequirePositive(fields.Qty, "La cantidad debe ser mayor que 0."),
            x, y),
        MissionObjectiveTypes.DiscoverMap =>
            MissionObjectiveArgsCodec.BuildDiscoverMap(RequirePositive(fields.MapId, "Selecciona un mapa.")),
        MissionObjectiveTypes.DiscoverArea =>
            MissionObjectiveArgsCodec.BuildDiscoverArea(RequirePositive(fields.AreaId, "Selecciona una zona.")),
        MissionObjectiveTypes.DefeatMobs => MissionObjectiveArgsCodec.BuildDefeatMobs(
            fields.MobPairs is { Count: > 0 } pairs
                ? pairs
                : [(RequirePositive(fields.MobId, "Indica un Monster ID."),
                    RequirePositive(fields.Qty, "La cantidad debe ser mayor que 0."))],
            x, y),
        MissionObjectiveTypes.UseItem =>
            MissionObjectiveArgsCodec.BuildUseItem(RequirePositive(fields.ItemId, "Selecciona un objeto.")),
        MissionObjectiveTypes.ReachLevel =>
            MissionObjectiveArgsCodec.BuildReachLevel(RequirePositive(fields.Level, "Indica el nivel mínimo.")),
        MissionObjectiveTypes.HaveSpells =>
            MissionObjectiveArgsCodec.BuildHaveSpells(RequirePositive(fields.SpellCount, "Indica la cantidad de hechizos.")),
        MissionObjectiveTypes.JobLevel => MissionObjectiveArgsCodec.BuildJobLevel(
            RequirePositive(fields.JobCount, "Indica el número de oficios."),
            RequirePositive(fields.JobLevel, "Indica el nivel de oficio.")),
        MissionObjectiveTypes.DeliverSouls =>
            throw new InvalidOperationException("Entregar almas aún no está disponible (dato pendiente)."),
        _ => "",
    };

    public static string ValidateStageName(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? "La etapa necesita un nombre." : "";

    public static bool IsType12Selectable => false;

    private static bool TryParseInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static int RequireInt(string text, string humanError)
    {
        if (!TryParseInt(text, out var n))
            throw new InvalidOperationException(humanError);
        return n;
    }

    private static int RequirePositive(string text, string humanError)
    {
        var n = RequireInt(text, humanError);
        if (n <= 0)
            throw new InvalidOperationException(humanError);
        return n;
    }

    private static bool LooksGenerated(string detalle)
    {
        var t = detalle.Trim();
        return t.StartsWith('[')
               || t.StartsWith("Habla con", StringComparison.Ordinal)
               || t.StartsWith("Enseña", StringComparison.Ordinal)
               || t.StartsWith("Entrega", StringComparison.Ordinal)
               || t.StartsWith("Descubre", StringComparison.Ordinal)
               || t.StartsWith("Vence", StringComparison.Ordinal)
               || t.StartsWith("Utiliza", StringComparison.Ordinal)
               || t.StartsWith("Vuelve", StringComparison.Ordinal)
               || t.StartsWith("Alcanza", StringComparison.Ordinal)
               || t.StartsWith("Ten ", StringComparison.Ordinal);
    }
}
