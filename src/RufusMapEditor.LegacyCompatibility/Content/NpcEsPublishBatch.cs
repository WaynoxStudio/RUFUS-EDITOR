using System.Globalization;
using System.Text;

namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class NpcEsPublishBinding
{
    public required int NpcId { get; init; }
    public required string Name { get; init; }
    /// <summary>new | already | update | rename</summary>
    public required string Kind { get; init; }
    public IReadOnlyList<int> ExpectedActions { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> ExistingActions { get; init; } = Array.Empty<int>();
    public string? ExistingName { get; init; }
    public NpcEsAssignment? Assignment { get; init; }
}

public sealed class NpcEsPublishBatch
{
    public required IReadOnlyList<NpcEsPublishBinding> Bindings { get; init; }
    public required int SourceVersion { get; init; }
    public required int TargetVersion { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0 && (Additions.Count > 0 || AlreadyPublished.Count > 0);

    public IReadOnlyList<NpcEsAssignment> Additions =>
        Bindings.Where(b => (b.Kind is "new" or "update" or "rename") && b.Assignment is not null)
            .Select(b => b.Assignment!)
            .ToList();

    public IReadOnlyList<NpcEsPublishBinding> AlreadyPublished =>
        Bindings.Where(b => b.Kind == "already").ToList();

    public int NewCount => Additions.Count;

    public string FormatPreview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"npc_es actual: {SourceVersion}");
        sb.AppendLine($"npc_es nuevo: {(NewCount > 0 ? TargetVersion.ToString(CultureInfo.InvariantCulture) : SourceVersion.ToString(CultureInfo.InvariantCulture))}");
        sb.AppendLine();
        sb.AppendLine($"NPC a escribir: {NewCount}");
        if (AlreadyPublished.Count > 0)
            sb.AppendLine($"Ya correctos: {AlreadyPublished.Count}");
        sb.AppendLine();

        foreach (var b in Bindings)
        {
            sb.AppendLine($"NPC ID: {b.NpcId}");
            sb.AppendLine($"Nombre: {b.Name}");
            sb.AppendLine("Acciones:");
            sb.AppendLine("  " + NpcEsClientActions.FormatList(b.ExpectedActions));
            var aLit = NpcEsClientActions.FormatArrayLiteral(b.ExpectedActions);
            if (string.IsNullOrEmpty(aLit))
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"Entrada prevista: N.d[{b.NpcId}] = {{n: \"{Truncate(b.Name, 40)}\"}};"));
            else
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"Entrada prevista: N.d[{b.NpcId}] = {{n: \"{Truncate(b.Name, 40)}\", a: {aLit}}};"));

            if (b.Kind is "update" or "rename")
            {
                sb.AppendLine("Estado actual:");
                sb.AppendLine($"  n: \"{b.ExistingName}\"");
                sb.AppendLine("  a: " + (NpcEsClientActions.FormatArrayLiteral(b.ExistingActions) is { Length: > 0 } lit ? lit : "[]"));
                sb.AppendLine("Estado nuevo:");
                sb.AppendLine($"  n: \"{b.Name}\"");
                sb.AppendLine("  a: " + (aLit.Length > 0 ? aLit : "[]"));
                if (b.Kind == "rename")
                    sb.AppendLine("⚠ Cambio de nombre (confirmación requerida).");
                if (b.Kind == "update")
                    sb.AppendLine("⚠ Reparación / actualización de acciones.");
            }
            else if (b.Kind == "already")
            {
                sb.AppendLine("(ya publicado — sin cambios)");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>CONT.7B.1 — batch with action sets, updates, Hablar[3] auto.</summary>
public static class NpcEsPublishBatchBuilder
{
    public static NpcEsPublishBatch Build(ContentDraftWorkspace workspace, NpcEsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(snapshot);

        var errors = new List<string>();
        var bindings = new List<NpcEsPublishBinding>();

        foreach (var npc in workspace.Npcs.Drafts.Where(n => n.IsPendingNpcEsFor(workspace)))
        {
            var name = npc.Nombre.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"NPC {npc.Id}: nombre vacío.");
                continue;
            }

            if (npc.Id <= 0)
            {
                errors.Add($"NPC {npc.Id}: ID inválido (se reutiliza el ID real, no se inventa).");
                continue;
            }

            try
            {
                DialogEsLatin1.Validate(name, $"NPC {npc.Id}");
            }
            catch (DialogEsEncodingException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            var expected = NpcEsActionResolver.ResolveExpected(workspace, npc);
            // Persist auto Hablar into draft selection so UI stays consistent.
            if (expected.Contains(NpcEsClientActions.Talk)
                && !npc.NpcEsActionIds.Contains(NpcEsClientActions.Talk))
            {
                npc.NpcEsActionIds.Add(NpcEsClientActions.Talk);
            }

            var assignment = new NpcEsAssignment
            {
                Id = npc.Id,
                Name = name,
                Actions = expected,
            };

            if (!snapshot.Names.TryGetValue(npc.Id, out var existingName))
            {
                bindings.Add(new NpcEsPublishBinding
                {
                    NpcId = npc.Id,
                    Name = name,
                    Kind = "new",
                    ExpectedActions = expected,
                    Assignment = assignment,
                });
                continue;
            }

            var existingActions = snapshot.ActionsOf(npc.Id);
            var nameSame = string.Equals(existingName, name, StringComparison.Ordinal);
            var actionsSame = NpcEsClientActions.SameSet(existingActions, expected);

            if (nameSame && actionsSame)
            {
                bindings.Add(new NpcEsPublishBinding
                {
                    NpcId = npc.Id,
                    Name = name,
                    Kind = "already",
                    ExpectedActions = expected,
                    ExistingActions = existingActions,
                    ExistingName = existingName,
                });
                continue;
            }

            if (!nameSame)
            {
                bindings.Add(new NpcEsPublishBinding
                {
                    NpcId = npc.Id,
                    Name = name,
                    Kind = "rename",
                    ExpectedActions = expected,
                    ExistingActions = existingActions,
                    ExistingName = existingName,
                    Assignment = assignment,
                });
                continue;
            }

            // same name, different actions → update / repair
            bindings.Add(new NpcEsPublishBinding
            {
                NpcId = npc.Id,
                Name = name,
                Kind = "update",
                ExpectedActions = expected,
                ExistingActions = existingActions,
                ExistingName = existingName,
                Assignment = assignment,
            });
        }

        if (bindings.Count == 0 && errors.Count == 0)
            errors.Add("No hay NPCs pendientes de publicar en npc_es.");

        return new NpcEsPublishBatch
        {
            Bindings = bindings,
            SourceVersion = snapshot.Version,
            TargetVersion = snapshot.Version + 1,
            Errors = errors,
        };
    }

    public static void ApplyToWorkspace(
        ContentDraftWorkspace workspace,
        NpcEsPublishBatch batch,
        int publishedVersion)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var b in batch.Bindings)
        {
            var npc = workspace.Npcs.FindById(b.NpcId);
            if (npc is null) continue;
            npc.NpcEsPublished = true;
            npc.NpcEsPublishedVersion = publishedVersion;
            npc.NpcEsPublishedName = b.Name;
            npc.NpcEsPublishedActionIds = NpcEsClientActions.Normalize(b.ExpectedActions).ToList();
            // Keep user selection aligned with what was published (minus forcing is already in expected).
            npc.NpcEsActionIds = npc.NpcEsPublishedActionIds.ToList();
        }
    }
}
