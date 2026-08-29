using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class NpcDraftBatchTests
{
    [Fact]
    public void Max_20061_first_id_is_20062()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        Assert.Equal(20062, batch.NextProvisionalId);

        var a = batch.CreateNew();
        Assert.Equal(20062, a.Id);
        Assert.Equal(NpcsModeloDraft.StatusBorrador, a.Status);
    }

    [Fact]
    public void Create_three_are_consecutive()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);

        var a = batch.CreateNew();
        var b = batch.CreateNew();
        var c = batch.CreateNew();

        Assert.Equal(new[] { 20062, 20063, 20064 }, batch.ProvisionalIds);
        Assert.Equal(20062, a.Id);
        Assert.Equal(20063, b.Id);
        Assert.Equal(20064, c.Id);
        Assert.False(batch.HasDuplicateIds());
    }

    [Fact]
    public void Duplicate_assigns_new_provisional_id()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var a = batch.CreateNew();
        a.Nombre = "Lío";
        a.GfxId = 71;
        a.Pregunta = 20000;

        var copy = batch.Duplicate(a);
        Assert.Equal(20063, copy.Id);
        Assert.Equal("Lío", copy.Nombre);
        Assert.Equal(71, copy.GfxId);
        Assert.Equal(20000, copy.Pregunta);
        Assert.NotEqual(a.Id, copy.Id);
        Assert.False(batch.HasDuplicateIds());
    }

    [Fact]
    public void Delete_leaves_no_duplicate_ids()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var a = batch.CreateNew();
        var b = batch.CreateNew();
        var c = batch.CreateNew();
        Assert.Equal(3, batch.Drafts.Count);

        Assert.True(batch.Remove(b));
        Assert.Equal(new[] { 20062, 20064 }, batch.ProvisionalIds);
        Assert.False(batch.HasDuplicateIds());

        // Does not refill historical gap 20063 while 20064 remains
        var d = batch.CreateNew();
        Assert.Equal(20065, d.Id);
        Assert.DoesNotContain(20063, batch.ProvisionalIds);
        Assert.False(batch.HasDuplicateIds());
        Assert.Contains(a, batch.Drafts);
        Assert.Contains(c, batch.Drafts);
    }

    [Fact]
    public void Defaults_match_confirmed_values()
    {
        var d = NpcsModeloDraft.CreateWithDefaults(20062);
        Assert.Equal(100, d.ScaleX);
        Assert.Equal(100, d.ScaleY);
        Assert.Equal(-1, d.Color1);
        Assert.Equal(-1, d.Color2);
        Assert.Equal(-1, d.Color3);
        Assert.Equal("0,0,0,0,0", d.Accesorios);
        Assert.Equal(0, d.Foto);
        Assert.Equal(0, d.Pregunta);
        Assert.Equal(0, d.ObjetoCompra);
        Assert.Equal("", d.Ventas);
        Assert.Equal("", d.Nombre);
        Assert.Equal(NpcsModeloDraft.DefaultGfxId, d.GfxId);
        Assert.Equal(71, d.GfxId);
        Assert.Equal(0, d.Sexo);
        Assert.Empty(d.Locations);
    }

    [Fact]
    public void Does_not_fill_ancient_gaps_below_max()
    {
        var batch = new NpcDraftBatch();
        // BD has gap at 20002 but MAX is 20061 — must start at 20062
        batch.SetDbMaxId(20061);
        Assert.Equal(20062, batch.CreateNew().Id);
        Assert.DoesNotContain(20002, batch.ProvisionalIds);
    }

    [Fact]
    public async Task Fixed_reader_returns_configured_max()
    {
        INpcsModeloReadRepository repo = new FixedNpcsModeloReadRepository(20061);
        Assert.Equal(20061, await repo.GetMaxIdAsync());
    }

    [Fact]
    public void Location_links_to_provisional_npc_id()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var npc = batch.CreateNew();
        Assert.Equal(20062, npc.Id);

        var loc = batch.AddLocation(npc);
        loc.MapId = 12501;
        loc.CellId = 104;
        loc.Orientation = 1;

        Assert.Equal(20062, npc.ResolveLocationNpcId(loc));
        Assert.Contains((20062, loc), batch.EnumerateLocationsForPublish());
    }

    [Fact]
    public void Multiple_locations_same_npc()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var npc = batch.CreateNew();
        var a = batch.AddLocation(npc);
        a.MapId = 12501;
        a.CellId = 104;
        var b = batch.AddLocation(npc);
        b.MapId = 12502;
        b.CellId = 200;

        Assert.Equal(2, npc.Locations.Count);
        Assert.Equal(20062, npc.ResolveLocationNpcId(a));
        Assert.Equal(20062, npc.ResolveLocationNpcId(b));
    }

    [Fact]
    public void Several_npcs_do_not_cross_locations()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var n1 = batch.CreateNew();
        var n2 = batch.CreateNew();
        var a = batch.AddLocation(n1);
        a.MapId = 1;
        a.CellId = 10;
        var b = batch.AddLocation(n1);
        b.MapId = 2;
        b.CellId = 20;
        var c = batch.AddLocation(n2);
        c.MapId = 3;
        c.CellId = 30;

        Assert.Equal(2, n1.Locations.Count);
        Assert.Single(n2.Locations);
        Assert.DoesNotContain(c, n1.Locations);
        Assert.DoesNotContain(a, n2.Locations);
        Assert.Equal(20062, n1.ResolveLocationNpcId(a));
        Assert.Equal(20063, n2.ResolveLocationNpcId(c));

        var flat = batch.EnumerateLocationsForPublish();
        Assert.Equal(3, flat.Count);
        Assert.All(flat.Where(x => x.NpcId == 20062), x => Assert.Contains(x.Location, n1.Locations));
        Assert.All(flat.Where(x => x.NpcId == 20063), x => Assert.Contains(x.Location, n2.Locations));
    }

    [Fact]
    public void Duplicate_npc_locations_link_to_new_id()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var src = batch.CreateNew();
        src.Nombre = "Original";
        var loc = batch.AddLocation(src);
        loc.MapId = 12501;
        loc.CellId = 104;

        var copy = batch.Duplicate(src);
        Assert.Equal(20063, copy.Id);
        Assert.Single(copy.Locations);
        Assert.NotSame(loc, copy.Locations[0]);
        Assert.Equal(12501, copy.Locations[0].MapId);
        Assert.Equal(104, copy.Locations[0].CellId);
        Assert.Equal(20063, copy.ResolveLocationNpcId(copy.Locations[0]));
        Assert.Equal(20062, src.ResolveLocationNpcId(loc));
        Assert.Equal(2, batch.EnumerateLocationsForPublish().Count);
    }

    [Fact]
    public void Remove_location_keeps_npc()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var npc = batch.CreateNew();
        var loc = batch.AddLocation(npc);
        Assert.True(batch.RemoveLocation(npc, loc));
        Assert.Empty(npc.Locations);
        Assert.Contains(npc, batch.Drafts);
        Assert.Equal(20062, npc.Id);
    }

    [Fact]
    public void Remove_npc_clears_its_local_locations()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var n1 = batch.CreateNew();
        var n2 = batch.CreateNew();
        batch.AddLocation(n1).MapId = 1;
        batch.AddLocation(n1).MapId = 2;
        var keep = batch.AddLocation(n2);
        keep.MapId = 9;

        Assert.True(batch.Remove(n1));
        Assert.DoesNotContain(n1, batch.Drafts);
        Assert.Single(batch.EnumerateLocationsForPublish());
        Assert.Equal(20063, batch.EnumerateLocationsForPublish()[0].NpcId);
        Assert.Equal(9, batch.EnumerateLocationsForPublish()[0].Location.MapId);
    }

    [Fact]
    public void Add_location_never_invents_map_or_cell()
    {
        var batch = new NpcDraftBatch();
        batch.SetDbMaxId(20061);
        var npc = batch.CreateNew();
        npc.Nombre = "Pitor Reo";
        var loc = batch.AddLocation(npc);
        Assert.Equal(0, loc.MapId);
        Assert.Equal(0, loc.CellId);
        Assert.Equal("", loc.Name);
        // User must supply real map/cell — nothing auto-filled from world/maps.
        Assert.DoesNotContain(12501, new[] { loc.MapId });
    }

    [Fact]
    public void Workspace_roundtrip_keeps_locations()
    {
        var ws = new ContentDraftWorkspace();
        ws.Npcs.SetDbMaxId(20061);
        var npc = ws.Npcs.CreateNew();
        npc.Nombre = "Pitor";
        var loc = ws.Npcs.AddLocation(npc);
        loc.MapId = 12501;
        loc.CellId = 104;
        loc.Orientation = 1;
        loc.Condition = "";

        var loaded = ContentWorkspaceSerializer.Deserialize(ContentWorkspaceSerializer.Serialize(ws));
        Assert.Single(loaded.Npcs.Drafts);
        Assert.Equal(20062, loaded.Npcs.Drafts[0].Id);
        Assert.Equal(71, loaded.Npcs.Drafts[0].GfxId);
        Assert.Single(loaded.Npcs.Drafts[0].Locations);
        var l = loaded.Npcs.Drafts[0].Locations[0];
        Assert.Equal(12501, l.MapId);
        Assert.Equal(104, l.CellId);
        Assert.Equal(1, l.Orientation);
        Assert.Equal(20062, loaded.Npcs.Drafts[0].ResolveLocationNpcId(l));
    }
}
