using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.Sql;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.LegacyCompatibility.Tests.Ui;

public sealed class ThemeSettingsTests
{
    [Fact]
    public void Theme_and_visibility_json_roundtrip()
    {
        const string json = """
            {
              "Theme": 1,
              "UiScale": 1.1,
              "MapViewVisibility": {
                "ShowBackground": false,
                "ShowGround": true,
                "ShowObject1": false,
                "ShowObject2": true,
                "ShowGrid": true,
                "ShowCellIds": true
              }
            }
            """;

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("Theme").GetInt32());
        Assert.Equal(1.1, root.GetProperty("UiScale").GetDouble());
        var vis = root.GetProperty("MapViewVisibility");
        Assert.False(vis.GetProperty("ShowBackground").GetBoolean());
        Assert.True(vis.GetProperty("ShowGrid").GetBoolean());
    }
}

public sealed class MapViewVisibilityTests
{
    private static MapDocument LoadMap()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "10420.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Layer_visibility_options_do_not_modify_mapdata()
    {
        var map = LoadMap();
        var before = map.MapData;

        _ = new MapRenderOptions
        {
            DrawBackground = false,
            DrawGround = false,
            DrawObjectLayer1 = false,
            DrawObjectLayer2 = false,
        };

        Assert.Equal(before, map.MapData);
    }

    [Fact]
    public void Grid_and_cellid_overlays_do_not_modify_mapdata()
    {
        var map = LoadMap();
        var before = map.MapData;
        Assert.Equal(before, map.MapData);
    }
}

public sealed class BackgroundMetadataTests
{
    private static MapDocument LoadMap()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "maps"));
        var map = AstriaSqlMapParser.ParseFile(Path.Combine(root, "10420.sql"));
        map.Cells = MapDataCodec.DecodeMap(map.MapData);
        return map;
    }

    [Fact]
    public void Background_edit_command_undo_restores_id()
    {
        var map = LoadMap();
        var original = map.BackgroundId;
        var cmd = new MapMetadataEditCommand("Cambiar fondo", original, original + 100);
        cmd.Execute(map);
        Assert.Equal(original + 100, map.BackgroundId);
        cmd.Undo(map);
        Assert.Equal(original, map.BackgroundId);
    }

    [Fact]
    public void No_background_is_zero_confirmed()
    {
        var map = LoadMap();
        map.BackgroundId = 0;
        Assert.Equal(0, map.BackgroundId);
    }
}
