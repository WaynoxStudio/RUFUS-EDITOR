namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>ADMIN.UI.4B.2A.2 — compact npc_es client action presentation (no publisher changes).</summary>
public static class NpcClientActionsUi
{
    /// <summary>Human-readable summary for compact multiselect (no technical IDs as protagonists).</summary>
    public static string FormatCompactSummary(IEnumerable<int>? ids)
    {
        var n = NpcEsClientActions.Normalize(ids);
        return n.Count switch
        {
            0 => "(ninguna)",
            1 => NpcEsClientActions.LabelOf(n[0]),
            2 => $"{NpcEsClientActions.LabelOf(n[0])} + {NpcEsClientActions.LabelOf(n[1])}",
            _ => $"{n.Count} acciones seleccionadas"
        };
    }

    /// <summary>True when the only selected action is Hablar [3]. Conservative commerce-field rule.</summary>
    public static bool IsTalkOnlySelection(IEnumerable<int>? ids)
    {
        var n = NpcEsClientActions.Normalize(ids);
        return n.Count == 1 && n[0] == NpcEsClientActions.Talk;
    }

    /// <summary>
    /// Ventas / ObjetoCompra: ocultos si no hay acciones o solo Hablar.
    /// No hay mapeo por acción a Ventas vs ObjetoCompra en el editor; ambos van juntos.
    /// </summary>
    public static bool ShowCommerceFields(IEnumerable<int>? ids)
    {
        var n = NpcEsClientActions.Normalize(ids);
        if (n.Count == 0)
            return false;
        return !IsTalkOnlySelection(n);
    }
}
