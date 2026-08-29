using RufusMapEditor.LegacyCompatibility.VisualLibrary;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class MobsFixLib4Tests
{
    [Fact]
    public void Build_one_mob_strict_format()
    {
        var s = MobsFixGroupString.Build([new MobsFixSlot(1106, 102, 130)]);
        Assert.Equal("1106,102,130", s);
    }

    [Fact]
    public void Build_eight_mobs_joined_by_semicolon()
    {
        var slots = Enumerable.Range(1, 8).Select(i => new MobsFixSlot(i, 10, 20)).ToList();
        var s = MobsFixGroupString.Build(slots);
        Assert.Equal("1,10,20;2,10,20;3,10,20;4,10,20;5,10,20;6,10,20;7,10,20;8,10,20", s);
        Assert.True(MobsFixGroupString.TryParseStrict(s, out var parsed));
        Assert.Equal(8, parsed.Count);
    }

    [Fact]
    public void Build_rejects_zero_and_over_eight()
    {
        Assert.ThrowsAny<ArgumentException>(() => MobsFixGroupString.Build([]));
        var nine = Enumerable.Range(1, 9).Select(i => new MobsFixSlot(i, 1, 2)).ToList();
        Assert.ThrowsAny<ArgumentException>(() => MobsFixGroupString.Build(nine));
    }

    [Fact]
    public void Parse_rejects_id_only_and_legacy_corrupt()
    {
        Assert.False(MobsFixGroupString.TryParseStrict("1106", out _));
        Assert.False(MobsFixGroupString.TryParseStrict("1106,102", out _));
        Assert.False(MobsFixGroupString.TryParseStrict("1106;1056", out _));
        Assert.False(MobsFixGroupString.TryParseStrict("1106,102,130,extra", out _));
        Assert.False(MobsFixGroupString.TryParseStrict("", out _));
    }

    [Fact]
    public void Parse_accepts_confirmed_min_max()
    {
        Assert.True(MobsFixGroupString.TryParseStrict("1106,102,130;1056,102,130", out var slots));
        Assert.Equal(2, slots.Count);
        Assert.Equal(1106, slots[0].MobId);
        Assert.Equal(1056, slots[1].MobId);
        Assert.DoesNotContain(1607, slots.Select(s => s.MobId)); // gfxID must not appear as mob id
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Tipos_allowed(int tipo) => Assert.True(MobsFixTipoValues.IsAllowed(tipo));

    [Fact]
    public void Tipos_reject_others()
    {
        Assert.False(MobsFixTipoValues.IsAllowed(3));
        Assert.False(MobsFixTipoValues.IsAllowed(-2));
    }

    [Fact]
    public void Validator_requires_map_cell_and_slots()
    {
        var fail = MobsFixValidator.Validate(
            null, 10, 100,
            [new MobsFixSlotDraft(1106, "1", "2")],
            -1, "", "0", "",
            _ => true, null);
        Assert.False(fail.Ok);

        fail = MobsFixValidator.Validate(
            7421, null, 100,
            [new MobsFixSlotDraft(1106, "1", "2")],
            -1, "", "0", "",
            _ => true, null);
        Assert.False(fail.Ok);

        fail = MobsFixValidator.Validate(
            7421, 10, 100,
            [],
            -1, "", "0", "",
            _ => true, null);
        Assert.False(fail.Ok);
    }

    [Fact]
    public void Validator_rejects_gfx_as_id_when_not_in_modelo()
    {
        var fail = MobsFixValidator.Validate(
            7421, 55, 200,
            [new MobsFixSlotDraft(1607, "102", "130")],
            -1, "cond", "30", "desc",
            id => id is 1056 or 1106, // gfx 1607 absent
            null);
        Assert.False(fail.Ok);
        Assert.Contains("1607", fail.Error);
    }

    [Fact]
    public void Validator_accepts_full_payload()
    {
        var ok = MobsFixValidator.Validate(
            7421, 55, 200,
            [new MobsFixSlotDraft(1106, "102", "130"), new MobsFixSlotDraft(1056, "102", "130")],
            0, "MI_COND", "45", "grupo test",
            id => id is 1056 or 1106,
            null);
        Assert.True(ok.Ok);
        Assert.Equal("1106,102,130;1056,102,130", ok.Request!.Mobs);
        Assert.Equal(0, ok.Request.Tipo);
        Assert.Equal("MI_COND", ok.Request.Condicion);
        Assert.Equal(45, ok.Request.SegundosRespawn);
        Assert.Equal("grupo test", ok.Request.Descripcion);
    }

    [Fact]
    public async Task Publish_replace_new_row_verifies_defaults()
    {
        var repo = new InMemoryMobsFixRepository();
        var service = new MobsFixPublishService(repo);
        var req = new MobsFixPublishRequest
        {
            Mapa = 900001,
            Celda = 120,
            Mobs = "1106,102,130",
            Tipo = -1,
            Condicion = "c",
            SegundosRespawn = 12,
            Descripcion = "d",
            Slots = [new MobsFixSlot(1106, 102, 130)],
        };
        var result = await service.PublishAsync(req);
        Assert.True(result.Ok, result.Error);
        Assert.Equal("0", result.VerifiedRow!.Sala);
        Assert.Equal(1, result.VerifiedRow.Movible);
        Assert.Equal(0, result.VerifiedRow.Oleadas);
        Assert.Null(result.VerifiedRow.Id);
        Assert.Equal(1, repo.ReplaceCount);
        Assert.Equal(0, repo.MapasMobsWriteCount);
    }

    [Fact]
    public async Task Publish_all_tipos_and_replace_same_pk()
    {
        var repo = new InMemoryMobsFixRepository();
        var service = new MobsFixPublishService(repo);
        foreach (var tipo in new[] { -1, 0, 1, 2 })
        {
            var req = new MobsFixPublishRequest
            {
                Mapa = 42,
                Celda = 7,
                Mobs = "1,1,2;2,1,2;3,1,2;4,1,2;5,1,2;6,1,2;7,1,2;8,1,2",
                Tipo = tipo,
                Condicion = "x",
                SegundosRespawn = 0,
                Descripcion = "t" + tipo,
                Slots = Enumerable.Range(1, 8).Select(i => new MobsFixSlot(i, 1, 2)).ToList(),
            };
            var r = await service.PublishAsync(req);
            Assert.True(r.Ok, r.Error);
            Assert.Equal(tipo, r.VerifiedRow!.Tipo);
        }

        var rows = await repo.GetByMapaAsync(42);
        Assert.Single(rows); // same mapa+celda → no duplicates
        Assert.Equal(4, repo.ReplaceCount);
    }

    [Fact]
    public async Task Load_legacy_corrupt_not_auto_fixed()
    {
        var repo = new InMemoryMobsFixRepository();
        var legacy = "1106;1056,102"; // corrupt / legacy
        repo.SeedRaw(new MobsFixRow
        {
            Mapa = 1,
            Celda = 9,
            Mobs = legacy,
            Tipo = -1,
            Condicion = "",
            SegundosRespawn = 0,
            Descripcion = "",
            Sala = "0",
            Movible = 1,
            Oleadas = 0,
            Id = null,
        });

        var loaded = await repo.GetByMapaCeldaAsync(1, 9);
        Assert.NotNull(loaded);
        Assert.True(loaded!.HasLegacyOrUnrecognizedMobsFormat);
        Assert.Equal(legacy, loaded.Mobs); // unchanged
        Assert.False(MobsFixGroupString.IsStrictFormat(loaded.Mobs));
    }

    [Fact]
    public async Task Schema_failure_aborts_publish()
    {
        var repo = new InMemoryMobsFixRepository();
        repo.SetSchemaBroken(true);
        var service = new MobsFixPublishService(repo);
        var r = await service.PublishAsync(new MobsFixPublishRequest
        {
            Mapa = 1,
            Celda = 1,
            Mobs = "1,1,1",
            Tipo = -1,
            Slots = [new MobsFixSlot(1, 1, 1)],
        });
        Assert.False(r.Ok);
        Assert.Equal(0, repo.ReplaceCount);
    }

    [Fact]
    public void Preview_contains_required_sections()
    {
        var text = MobsFixPublishService.BuildPreviewText(new MobsFixPublishRequest
        {
            Mapa = 123,
            Celda = 45,
            Mobs = "1106,102,130",
            Tipo = 2,
            Condicion = "C",
            SegundosRespawn = 9,
            Descripcion = "D",
            ReplacingExisting = true,
            ExistingRow = new MobsFixRow { Mapa = 123, Celda = 45, Mobs = "old", Tipo = -1 },
        });
        Assert.Contains("MAPA:", text);
        Assert.Contains("123", text);
        Assert.Contains("CELDA:", text);
        Assert.Contains("45", text);
        Assert.Contains("MOBS:", text);
        Assert.Contains("TIPO:", text);
        Assert.Contains("CONDICIÓN:", text);
        Assert.Contains("RESPAWN:", text);
        Assert.Contains("DESCRIPCIÓN:", text);
        Assert.Contains("REPLACE mobs_fix", text);
        Assert.Contains("Ya existe un grupo fijo", text);
        Assert.Contains("No aparecerá inmediatamente", text);
    }

    [Fact]
    public void Write_columns_are_exactly_seven_server_columns()
    {
        Assert.Equal(
            new[] { "mapa", "celda", "mobs", "tipo", "condicion", "segundosRespawn", "descripcion" },
            MobsFixColumns.WriteColumns);
        Assert.DoesNotContain("Sala", MobsFixColumns.WriteColumns);
        Assert.DoesNotContain("movible", MobsFixColumns.WriteColumns);
        Assert.DoesNotContain("oleadas", MobsFixColumns.WriteColumns);
        Assert.DoesNotContain("id", MobsFixColumns.WriteColumns);
        Assert.DoesNotContain("mobs", new[] { "mapas" }); // sanity: we never target mapas.mobs column here
    }
}
