using System.Reflection;
using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.1 — creative request model: no API, no technical IDs.</summary>
public sealed class AiCreativeRequestTests
{
    private static readonly string[] ForbiddenPropertyNames =
    [
        "NpcId", "Id", "PreguntaId", "Pregunta", "RespuestaId", "QuestId", "MisionId",
        "ItemId", "MapId", "CellId", "Accion", "Args", "Condicion", "Condition",
        "ApiKey", "OpenAi", "Token", "Secret"
    ];

    [Fact]
    public void Build_generar_nombre_resolves_presets_and_defaults_corta()
    {
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarNombre,
            rolePreset: "Minero",
            customRole: "ignorado",
            attitudePreset: "Sarcástico",
            customAttitude: "ignorado",
            narrativeContext: "Cueva oscura",
            additionalInstruction: "Está cansado de los aventureros.",
            length: AiTextLength.Corta,
            currentNpcName: "Bob");

        Assert.Equal(AiCreativeAction.GenerarNombre, req.Action);
        Assert.Equal("Minero", req.Role);
        Assert.Equal("Sarcástico", req.Attitude);
        Assert.Equal("Cueva oscura", req.NarrativeContext);
        Assert.Equal("Está cansado de los aventureros.", req.AdditionalInstruction);
        Assert.Equal(AiTextLength.Corta, req.Length);
        Assert.Equal(AiCreativeStyle.RufusDofusRetro, req.Style);
        Assert.Equal("Bob", req.CurrentNpcName);
    }

    [Fact]
    public void Build_uses_custom_role_and_attitude_when_personalizado()
    {
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            rolePreset: AiCreativePresets.RoleCustomLabel,
            customRole: "Alquimista errante",
            attitudePreset: AiCreativePresets.AttitudeCustomLabel,
            customAttitude: "Susurrante",
            narrativeContext: "Torre abandonada",
            additionalInstruction: null,
            length: AiTextLength.Media,
            currentNpcName: "Nia");

        Assert.Equal("Alquimista errante", req.Role);
        Assert.Equal("Susurrante", req.Attitude);
        Assert.Equal(AiTextLength.Media, req.Length);
        Assert.Equal("Nia", req.CurrentNpcName);
    }

    [Fact]
    public void Stub_prepare_does_not_invent_text_and_exposes_preview()
    {
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarConversacion,
            "Pescador",
            null,
            "Amable",
            null,
            "Bahía de Cania",
            "Habla del clima",
            AiTextLength.Corta,
            "Marin");

        var result = AiCreativeServiceStub.Prepare(req);

        Assert.False(result.Success);
        Assert.Equal(AiCreativeServiceStub.NotConnectedMessage, result.Message);
        Assert.Equal(AiCreativeServiceStub.NotConnectedShort, result.ShortMessage);
        Assert.Contains("Generar conversación", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Pescador", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Amable", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Bahía de Cania", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Corta", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Habla del clima", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Marin", result.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", result.Preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", result.Preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Request_type_has_no_technical_id_or_secret_properties()
    {
        var names = typeof(AiCreativeRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var forbidden in ForbiddenPropertyNames)
            Assert.DoesNotContain(forbidden, names);

        Assert.Contains("Role", names);
        Assert.Contains("Attitude", names);
        Assert.Contains("NarrativeContext", names);
        Assert.Contains("AdditionalInstruction", names);
        Assert.Contains("Length", names);
        Assert.Contains("Style", names);
        Assert.Contains("CurrentNpcName", names);
        Assert.Contains("Action", names);
    }
}
