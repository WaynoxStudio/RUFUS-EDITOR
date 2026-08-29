using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

/// <summary>ADMIN.UI.4B.2A — Identidad presentation (sexo, tamaño, preserved hidden fields).</summary>
public sealed class NpcIdentityUiTests
{
    [Theory]
    [InlineData(NpcSexoUi.Hombre, 0)]
    [InlineData(NpcSexoUi.Mujer, 1)]
    public void SexoFromUi_maps_confirmed_values(NpcSexoUi ui, int expected)
    {
        Assert.Equal(expected, NpcIdentityUi.SexoFromUi(ui));
    }

    [Theory]
    [InlineData(0, NpcSexoUi.Hombre)]
    [InlineData(1, NpcSexoUi.Mujer)]
    public void SexoToUi_maps_confirmed_values(int sexo, NpcSexoUi expected)
    {
        Assert.Equal(expected, NpcIdentityUi.SexoToUi(sexo));
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(140, 140, 140)]
    public void ApplyUniformTamaño_sets_both_axes(int input, int expectedX, int expectedY)
    {
        var (sx, sy) = NpcIdentityUi.ApplyUniformTamaño(input);
        Assert.Equal(expectedX, sx);
        Assert.Equal(expectedY, sy);
    }

    [Fact]
    public void Equal_scales_show_tamaño_value_without_edit()
    {
        Assert.Equal("100", NpcIdentityUi.FormatTamañoDisplay(100, 100, userEditedTamaño: false));
    }

    [Fact]
    public void Unequal_scales_hide_tamaño_until_user_edits()
    {
        Assert.Equal("", NpcIdentityUi.FormatTamañoDisplay(120, 100, userEditedTamaño: false));
        Assert.True(NpcIdentityUi.HasUnequalScale(120, 100));
        Assert.Equal("Tamaño personalizado: X 120 / Y 100",
            NpcIdentityUi.FormatUnequalScaleHint(120, 100));
    }

    [Fact]
    public void Unequal_scales_show_value_after_explicit_edit()
    {
        Assert.Equal("140", NpcIdentityUi.FormatTamañoDisplay(140, 140, userEditedTamaño: true));
    }

    [Fact]
    public void Tamaño_140_updates_both_scales_on_draft()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        Assert.True(NpcIdentityUi.TryParseTamaño("140", out var v));
        var (sx, sy) = NpcIdentityUi.ApplyUniformTamaño(v);
        draft.ScaleX = sx;
        draft.ScaleY = sy;
        Assert.Equal(140, draft.ScaleX);
        Assert.Equal(140, draft.ScaleY);
    }

    [Fact]
    public void Unequal_scale_preserved_on_open_without_edit()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.ScaleX = 120;
        draft.ScaleY = 100;

        Assert.Equal("", NpcIdentityUi.FormatTamañoDisplay(draft.ScaleX, draft.ScaleY, userEditedTamaño: false));
        Assert.Equal(120, draft.ScaleX);
        Assert.Equal(100, draft.ScaleY);
    }

    [Fact]
    public void Unequal_scale_equalized_only_after_explicit_tamaño_edit()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.ScaleX = 120;
        draft.ScaleY = 100;

        Assert.True(NpcIdentityUi.TryParseTamaño("100", out var v));
        (draft.ScaleX, draft.ScaleY) = NpcIdentityUi.ApplyUniformTamaño(v);
        Assert.Equal(100, draft.ScaleX);
        Assert.Equal(100, draft.ScaleY);
    }

    [Theory]
    [InlineData(NpcSexoUi.Hombre, 0)]
    [InlineData(NpcSexoUi.Mujer, 1)]
    public void Sexo_ui_roundtrip_on_draft(NpcSexoUi ui, int expectedModel)
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Sexo = NpcIdentityUi.SexoFromUi(ui);
        Assert.Equal(expectedModel, draft.Sexo);
        Assert.Equal(ui, NpcIdentityUi.SexoToUi(draft.Sexo));
    }

    [Fact]
    public void Existing_npc_sexo_0_shows_hombre()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Sexo = 0;
        Assert.Equal(NpcSexoUi.Hombre, NpcIdentityUi.SexoToUi(draft.Sexo));
    }

    [Fact]
    public void Existing_npc_sexo_1_shows_mujer()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Sexo = 1;
        Assert.Equal(NpcSexoUi.Mujer, NpcIdentityUi.SexoToUi(draft.Sexo));
    }

    [Fact]
    public void Hidden_color_fields_preserved_without_ui_edit()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Color1 = 5;
        draft.Color2 = 6;
        draft.Color3 = 7;
        draft.Nombre = "Test";
        Assert.Equal(5, draft.Color1);
        Assert.Equal(6, draft.Color2);
        Assert.Equal(7, draft.Color3);
    }

    [Fact]
    public void Hidden_foto_preserved_without_ui_edit()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        draft.Foto = 42;
        draft.Nombre = "Test";
        Assert.Equal(42, draft.Foto);
    }

    [Fact]
    public void New_npc_uses_existing_defaults()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20062);
        Assert.Equal(NpcsModeloDraft.DefaultGfxId, draft.GfxId);
        Assert.Equal(0, draft.Sexo);
        Assert.Equal(NpcSexoUi.Hombre, NpcIdentityUi.SexoToUi(draft.Sexo));
        Assert.Equal("100", NpcIdentityUi.FormatTamañoDisplay(draft.ScaleX, draft.ScaleY, userEditedTamaño: false));
        Assert.Equal(NpcsModeloDraft.DefaultColor1, draft.Color1);
        Assert.Equal(NpcsModeloDraft.DefaultFoto, draft.Foto);
        Assert.Equal(NpcsModeloDraft.DefaultAccesorios, draft.Accesorios);
    }

    [Fact]
    public void Id_display_format()
    {
        var draft = NpcsModeloDraft.CreateWithDefaults(20066);
        draft.Nombre = "Salazar Limo";
        Assert.Equal(20066, draft.Id);
        Assert.Equal("Salazar Limo", draft.Nombre);
        draft.Nombre = "Otro";
        Assert.Equal("Otro", draft.Nombre);
    }

    [Fact]
    public void Workspace_roundtrip_preserves_hidden_identity_fields()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Color1 = 9;
        npc.Color2 = 8;
        npc.Color3 = 7;
        npc.Foto = 15;
        npc.ScaleX = 120;
        npc.ScaleY = 100;
        npc.Sexo = 1;

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        var d = Assert.Single(loaded.Npcs.Drafts);
        Assert.Equal(9, d.Color1);
        Assert.Equal(8, d.Color2);
        Assert.Equal(7, d.Color3);
        Assert.Equal(15, d.Foto);
        Assert.Equal(120, d.ScaleX);
        Assert.Equal(100, d.ScaleY);
        Assert.Equal(1, d.Sexo);
    }
}
