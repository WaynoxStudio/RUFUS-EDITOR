using System.Text;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

public sealed class MobsFixPublishResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public MobsFixRow? VerifiedRow { get; init; }

    public static MobsFixPublishResult Fail(string error) =>
        new() { Ok = false, Error = error };

    public static MobsFixPublishResult Success(MobsFixRow row) =>
        new() { Ok = true, VerifiedRow = row };
}

/// <summary>LIB.4 — validate → REPLACE → re-read → verify defaults. No GM / no mapas.mobs.</summary>
public sealed class MobsFixPublishService
{
    private readonly IMobsFixRepository _repo;

    public MobsFixPublishService(IMobsFixRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<MobsFixPublishResult> PublishAsync(
        MobsFixPublishRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await _repo.PingAsync(ct).ConfigureAwait(false);
            await _repo.ValidateSchemaAsync(ct).ConfigureAwait(false);

            foreach (var slot in request.Slots)
            {
                if (!await _repo.MobModeloExistsAsync(slot.MobId, ct).ConfigureAwait(false))
                    return MobsFixPublishResult.Fail(
                        $"Mob ID {slot.MobId} no existe en mobs_modelo.");
            }

            await _repo.ReplaceAsync(request, ct).ConfigureAwait(false);

            var row = await _repo.GetByMapaCeldaAsync(request.Mapa, request.Celda, ct)
                .ConfigureAwait(false);
            if (row is null)
                return MobsFixPublishResult.Fail("Verificación fallida: fila no encontrada tras REPLACE.");

            var err = VerifyRow(request, row);
            if (err is not null)
                return MobsFixPublishResult.Fail(err);

            return MobsFixPublishResult.Success(row);
        }
        catch (MobsFixSchemaException ex)
        {
            return MobsFixPublishResult.Fail("Esquema mobs_fix: " + ex.Message);
        }
        catch (Exception ex)
        {
            return MobsFixPublishResult.Fail(ex.Message);
        }
    }

    public static string BuildPreviewText(MobsFixPublishRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MAPA:\n{request.Mapa}");
        sb.AppendLine();
        sb.AppendLine($"CELDA:\n{request.Celda}");
        sb.AppendLine();
        sb.AppendLine($"MOBS:\n{request.Mobs}");
        sb.AppendLine();
        sb.AppendLine($"TIPO:\n{request.Tipo} ({MobsFixTipoValues.DisplayName(request.Tipo)})");
        sb.AppendLine();
        sb.AppendLine($"CONDICIÓN:\n{(string.IsNullOrEmpty(request.Condicion) ? "(vacía)" : request.Condicion)}");
        sb.AppendLine();
        sb.AppendLine($"RESPAWN:\n{request.SegundosRespawn}");
        sb.AppendLine();
        sb.AppendLine($"DESCRIPCIÓN:\n{(string.IsNullOrEmpty(request.Descripcion) ? "(vacía)" : request.Descripcion)}");
        sb.AppendLine();
        sb.AppendLine("Operación:\nREPLACE mobs_fix");
        sb.AppendLine($"PK: mapa + celda ({request.Mapa}, {request.Celda})");
        if (request.ReplacingExisting)
        {
            sb.AppendLine();
            sb.AppendLine("Ya existe un grupo fijo en esta celda.");
            if (request.ExistingRow is { } ex)
            {
                sb.AppendLine($"Actual: mobs={ex.Mobs} · tipo={ex.Tipo}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("El grupo quedará persistido en BD.");
        sb.AppendLine("No aparecerá inmediatamente en runtime hasta que el servidor");
        sb.AppendLine("recargue los mobs fijos.");
        return sb.ToString().TrimEnd();
    }

    private static string? VerifyRow(MobsFixPublishRequest request, MobsFixRow row)
    {
        if (row.Mapa != request.Mapa)
            return $"Verificación: mapa {row.Mapa} ≠ {request.Mapa}";
        if (row.Celda != request.Celda)
            return $"Verificación: celda {row.Celda} ≠ {request.Celda}";
        if (!string.Equals(row.Mobs, request.Mobs, StringComparison.Ordinal))
            return $"Verificación: mobs distintos.";
        if (row.Tipo != request.Tipo)
            return $"Verificación: tipo {row.Tipo} ≠ {request.Tipo}";
        if (!string.Equals(row.Condicion ?? "", request.Condicion ?? "", StringComparison.Ordinal))
            return "Verificación: condicion distinta.";
        if (row.SegundosRespawn != request.SegundosRespawn)
            return $"Verificación: segundosRespawn {row.SegundosRespawn} ≠ {request.SegundosRespawn}";
        if (!string.Equals(row.Descripcion ?? "", request.Descripcion ?? "", StringComparison.Ordinal))
            return "Verificación: descripcion distinta.";

        var sala = row.Sala ?? "";
        if (!string.Equals(sala, MobsFixColumns.ExpectedSalaDefault, StringComparison.Ordinal))
            return $"Verificación: Sala esperada '{MobsFixColumns.ExpectedSalaDefault}', obtuvo '{sala}'.";
        if (row.Movible != MobsFixColumns.ExpectedMovibleDefault)
            return $"Verificación: movible esperado {MobsFixColumns.ExpectedMovibleDefault}, obtuvo {row.Movible}.";
        if (row.Oleadas != MobsFixColumns.ExpectedOleadasDefault)
            return $"Verificación: oleadas esperado {MobsFixColumns.ExpectedOleadasDefault}, obtuvo {row.Oleadas}.";
        if (row.Id is not null)
            return $"Verificación: id esperado NULL, obtuvo {row.Id}.";

        return null;
    }
}
