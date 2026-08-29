using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public sealed class MobsFixValidationResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public MobsFixPublishRequest? Request { get; init; }

    public static MobsFixValidationResult Fail(string error) =>
        new() { Ok = false, Error = error };

    public static MobsFixValidationResult Success(MobsFixPublishRequest request) =>
        new() { Ok = true, Request = request };
}

/// <summary>LIB.4 — pre-write validation (no SQL). Abort on any failure.</summary>
public static class MobsFixValidator
{
    public static MobsFixValidationResult Validate(
        int? mapId,
        int? cellId,
        int? cellCount,
        IReadOnlyList<MobsFixSlotDraft> slots,
        int tipo,
        string? condicion,
        string? segundosRespawnText,
        string? descripcion,
        Func<int, bool> mobIdExists,
        MobsFixRow? existingRow)
    {
        if (mapId is null or <= 0)
            return MobsFixValidationResult.Fail("Map ID inválido: abre un mapa en el editor.");

        if (cellId is null)
            return MobsFixValidationResult.Fail("Cell ID no seleccionado: selecciona explícitamente una celda del mapa.");

        if (cellId < 0)
            return MobsFixValidationResult.Fail($"Cell ID inválido: {cellId.Value}.");

        if (cellCount is int n && cellId >= n)
            return MobsFixValidationResult.Fail(
                $"Cell ID {cellId.Value} fuera de rango del mapa (0..{n - 1}).");

        if (slots is null || slots.Count is < 1 or > MapMonsterGroupLimits.MaxSlots)
            return MobsFixValidationResult.Fail(
                $"El grupo debe tener entre 1 y {MapMonsterGroupLimits.MaxSlots} monstruos.");

        if (!MobsFixTipoValues.IsAllowed(tipo))
            return MobsFixValidationResult.Fail($"Tipo inválido: {tipo}. Permitidos: -1, 0, 1, 2.");

        if (!int.TryParse(
                (segundosRespawnText ?? "").Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var segundos)
            || segundos < 0)
        {
            return MobsFixValidationResult.Fail(
                "Segundos Respawn debe ser un entero ≥ 0.");
        }

        var built = new List<MobsFixSlot>(slots.Count);
        foreach (var draft in slots)
        {
            if (draft.MobId <= 0)
                return MobsFixValidationResult.Fail("Mob ID inválido (debe ser mobs_modelo.id > 0).");

            if (!mobIdExists(draft.MobId))
                return MobsFixValidationResult.Fail(
                    $"Mob ID {draft.MobId} no existe en mobs_modelo (no usar gfxID como ID).");

            if (!int.TryParse(draft.MinLvlText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
                || !int.TryParse(draft.MaxLvlText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
            {
                return MobsFixValidationResult.Fail(
                    $"Min/Max Lvl inválidos para mob {draft.MobId}.");
            }

            if (min < 0 || max < 0 || min > max)
                return MobsFixValidationResult.Fail(
                    $"Rango de nivel inválido para mob {draft.MobId}: {min}..{max}.");

            built.Add(new MobsFixSlot(draft.MobId, min, max));
        }

        string mobs;
        try
        {
            mobs = MobsFixGroupString.Build(built);
        }
        catch (Exception ex)
        {
            return MobsFixValidationResult.Fail("Formato mobs: " + ex.Message);
        }

        return MobsFixValidationResult.Success(new MobsFixPublishRequest
        {
            Mapa = mapId.Value,
            Celda = cellId.Value,
            Mobs = mobs,
            Tipo = tipo,
            Condicion = condicion ?? "",
            SegundosRespawn = segundos,
            Descripcion = descripcion ?? "",
            Slots = built,
            ReplacingExisting = existingRow is not null,
            ExistingRow = existingRow,
        });
    }
}

/// <summary>UI draft slot (string levels) for validation.</summary>
public sealed record MobsFixSlotDraft(int MobId, string MinLvlText, string MaxLvlText);
