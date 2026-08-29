using RufusMapEditor.LegacyCompatibility.Content.Ai;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>AI.2 — master prompt, personalities, and prompt composition (no API).</summary>
public sealed class AiPromptComposerTests
{
    private static readonly (string Label, string MustContain)[] PersonalityMarkers =
    [
        ("Amable", "cordial"),
        ("Gruñón", "Protestón"),
        ("Sarcástico", "ironía"),
        ("Desconfiado", "Sospecha"),
        ("Excéntrico", "peculiares"),
        ("Cobarde", "Temeroso"),
        ("Arrogante", "condescendencia"),
        ("Misterioso", "incógnitas"),
        ("Nervioso", "Apresurado"),
        ("Entusiasta", "Energético"),
        ("Hostil", "incómodo"),
        ("Melancólico", "Nostálgico")
    ];

    [Fact]
    public void Master_prompt_includes_rufus_style_and_blocks_technical_ids()
    {
        var master = AiMasterPrompt.FullMasterInstructions;
        Assert.Contains("RUFUS", master, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DOFUS", master, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("juegos de palabras", master, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NPC ID", master, StringComparison.Ordinal);
        Assert.Contains("Map ID", master, StringComparison.Ordinal);
        Assert.Contains("GFX ID", master, StringComparison.Ordinal);
        Assert.Contains("contenido creativo", master, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllPersonalityCases))]
    public void Each_preset_injects_its_own_internal_guidance(string label, string marker)
    {
        var req = BaseRequest(AiCreativeAction.GenerarDialogo, attitude: label);
        var package = AiPromptComposer.Compose(req);

        Assert.Equal(label, package.PersonalityLabel);
        Assert.Contains(marker, package.PersonalityGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(marker, package.DynamicContext, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(marker, package.FullPrompt, StringComparison.OrdinalIgnoreCase);

        var other = PersonalityMarkers.First(p => p.Label != label);
        Assert.DoesNotContain(other.MustContain, package.PersonalityGuidance, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> AllPersonalityCases() =>
        PersonalityMarkers.Select(p => new object[] { p.Label, p.MustContain });

    [Fact]
    public void Catalog_exposes_all_twelve_presets()
    {
        Assert.Equal(12, AiPersonalityCatalog.Presets.Count);
        foreach (var (label, _) in PersonalityMarkers)
            Assert.True(AiPersonalityCatalog.TryGetPreset(label, out _), label);
    }

    [Fact]
    public void Custom_personality_keeps_user_text_verbatim()
    {
        const string custom =
            "Está convencido de que los tofus están conspirando contra él, pero intenta que nadie lo note.";

        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            "Minero",
            null,
            AiCreativePresets.AttitudeCustomLabel,
            custom,
            "Mina abandonada",
            null,
            AiTextLength.Corta,
            "Pico");

        var package = AiPromptComposer.Compose(req);
        Assert.Equal(AiCreativePresets.AttitudeCustomLabel, package.PersonalityLabel);
        Assert.Equal(custom, package.PersonalityGuidance);
        Assert.Contains(custom, package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains("no alterar su significado", package.DynamicContext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generar_nombre_asks_for_three_options()
    {
        var package = AiPromptComposer.Compose(BaseRequest(AiCreativeAction.GenerarNombre));
        Assert.Contains("3", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("nombres", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GENERAR NOMBRE", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("no fuerza nombres más largos", package.LengthSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generar_dialogo_asks_spoken_text_without_technical_ids()
    {
        var req = BaseRequest(AiCreativeAction.GenerarDialogo, name: "Roco");
        var package = AiPromptComposer.Compose(req);
        Assert.Contains("texto hablado", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Roco", package.TaskInstructions, StringComparison.Ordinal);
        Assert.Contains("D.q", package.TaskInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", package.FullPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generar_conversacion_asks_opening_and_three_player_replies()
    {
        var package = AiPromptComposer.Compose(BaseRequest(AiCreativeAction.GenerarConversacion, name: "Nia"));
        Assert.Contains("texto inicial", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 respuestas", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("neutra", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("amable", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("humorística", package.TaskInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO asignar acciones técnicas", package.TaskInstructions, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AiTextLength.Corta, "concisas")]
    [InlineData(AiTextLength.Media, "desarrollo")]
    [InlineData(AiTextLength.Larga, "narrativo")]
    public void Length_guidance_applied_for_dialog(AiTextLength length, string marker)
    {
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            "Guardia",
            null,
            "Amable",
            null,
            "Puerta de la ciudad",
            null,
            length,
            null);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains(marker, package.LengthSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(marker, package.DynamicContext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Additional_instruction_is_included_verbatim()
    {
        const string extra = "No menciones directamente a RUFUS.";
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            "Ermitaño",
            null,
            "Misterioso",
            null,
            "Cueva",
            extra,
            AiTextLength.Corta,
            null);

        var package = AiPromptComposer.Compose(req);
        Assert.Equal(extra, package.AdditionalInstructionSummary);
        Assert.Contains(extra, package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains(extra, package.FullPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Narrative_context_has_priority_wording_in_context_block()
    {
        const string ctx = "Está atrapado en una mina porque unos aventureros bloquearon la salida.";
        var req = AiCreativeRequestBuilder.Build(
            AiCreativeAction.GenerarDialogo,
            "Minero",
            null,
            "Gruñón",
            null,
            ctx,
            null,
            AiTextLength.Corta,
            null);

        var package = AiPromptComposer.Compose(req);
        Assert.Contains(ctx, package.DynamicContext, StringComparison.Ordinal);
        Assert.Contains("prioridad", package.DynamicContext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_includes_prompt_compuesto_section()
    {
        var result = AiCreativeServiceStub.Prepare(BaseRequest(AiCreativeAction.GenerarNombre));
        Assert.Contains("PROMPT COMPUESTO", result.Preview, StringComparison.Ordinal);
        Assert.Contains("Reglas maestras aplicadas:", result.Preview, StringComparison.Ordinal);
        Assert.Contains("3", result.Preview, StringComparison.Ordinal);
        Assert.NotNull(result.Package);
        Assert.False(result.Success);
        Assert.DoesNotContain("sk-", result.Preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", result.Preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Full_prompt_separates_master_context_and_task()
    {
        var package = AiPromptComposer.Compose(BaseRequest(AiCreativeAction.GenerarDialogo));
        Assert.False(string.IsNullOrWhiteSpace(package.MasterInstructions));
        Assert.False(string.IsNullOrWhiteSpace(package.DynamicContext));
        Assert.False(string.IsNullOrWhiteSpace(package.TaskInstructions));
        Assert.Contains(package.MasterInstructions.Trim(), package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains(package.DynamicContext.Trim(), package.FullPrompt, StringComparison.Ordinal);
        Assert.Contains(package.TaskInstructions.Trim(), package.FullPrompt, StringComparison.Ordinal);
    }

    private static AiCreativeRequest BaseRequest(
        AiCreativeAction action,
        string attitude = "Amable",
        string? name = null) =>
        AiCreativeRequestBuilder.Build(
            action,
            "Minero",
            null,
            attitude,
            null,
            "Cueva oscura",
            null,
            AiTextLength.Corta,
            name);
}
