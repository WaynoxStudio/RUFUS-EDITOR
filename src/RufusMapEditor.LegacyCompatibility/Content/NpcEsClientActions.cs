using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.7B.1 — client-side NPC menu actions from npc_es N.a[1..8] (not npc_respuestas.accion).</summary>
public static class NpcEsClientActions
{
    public const int BuySell = 1;
    public const int Exchange = 2;
    public const int Talk = 3;
    public const int PetDropPick = 4;
    public const int Sell = 5;
    public const int Buy = 6;
    public const int PetRevive = 7;
    public const int MountExchange = 8;

    public static IReadOnlyList<(int Id, string Label)> All { get; } = new (int, string)[]
    {
        (BuySell, "Comprar/Vender"),
        (Exchange, "Intercambiar"),
        (Talk, "Hablar"),
        (PetDropPick, "Dejar/Recoger a una mascota"),
        (Sell, "Vender"),
        (Buy, "Comprar"),
        (PetRevive, "Resucitar a una mascota"),
        (MountExchange, "Intercambiar una montura"),
    };

    public static bool IsValid(int id) => id is >= BuySell and <= MountExchange;

    public static string LabelOf(int id)
    {
        foreach (var (i, label) in All)
            if (i == id) return label;
        return string.Create(CultureInfo.InvariantCulture, $"#{id}");
    }

    public static bool SameSet(IEnumerable<int>? a, IEnumerable<int>? b)
    {
        var sa = Normalize(a);
        var sb = Normalize(b);
        if (sa.Count != sb.Count) return false;
        for (var i = 0; i < sa.Count; i++)
            if (sa[i] != sb[i]) return false;
        return true;
    }

    public static IReadOnlyList<int> Normalize(IEnumerable<int>? ids)
    {
        if (ids is null) return Array.Empty<int>();
        return ids.Where(IsValid).Distinct().OrderBy(x => x).ToList();
    }

    public static string FormatList(IEnumerable<int>? ids)
    {
        var n = Normalize(ids);
        if (n.Count == 0) return "(ninguna)";
        return string.Join(", ", n.Select(i =>
            string.Create(CultureInfo.InvariantCulture, $"[{i}] {LabelOf(i)}")));
    }

    public static string FormatArrayLiteral(IEnumerable<int>? ids)
    {
        var n = Normalize(ids);
        if (n.Count == 0) return "";
        return "[" + string.Join(",", n.Select(i => i.ToString(CultureInfo.InvariantCulture))) + "]";
    }
}

/// <summary>CONT.7B.1 — expected N.d[id].a from draft + dialog rules.</summary>
public static class NpcEsActionResolver
{
    public static bool HasClientDialog(ContentDraftWorkspace workspace, NpcsModeloDraft npc)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        if (npc.DialogMode == NpcDialogMode.Simple)
        {
            return npc.Pregunta > 0
                   || npc.IsPendingDialogEs
                   || npc.DialogEsPublished
                   || !string.IsNullOrWhiteSpace(npc.SimpleDialogTextLocal);
        }

        if (npc.Pregunta > 0)
            return true;

        return workspace.Dialogs.Questions.Any(q =>
            q.OwnerNpcId == npc.Id && !string.IsNullOrWhiteSpace(q.TextLocal));
    }

    /// <summary>User selections + Hablar[3] when dialog exists. Sorted ascending, unique, valid only.</summary>
    public static IReadOnlyList<int> ResolveExpected(
        ContentDraftWorkspace workspace,
        NpcsModeloDraft npc)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(npc);

        var set = new SortedSet<int>();
        foreach (var id in npc.NpcEsActionIds)
        {
            if (NpcEsClientActions.IsValid(id))
                set.Add(id);
        }

        if (HasClientDialog(workspace, npc))
            set.Add(NpcEsClientActions.Talk);

        return set.ToList();
    }
}
