using System.Text;
using System.Xml.Linq;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.LegacyCompatibility.Gfx;
using RufusMapEditor.LegacyCompatibility.MapData;

namespace RufusMapEditor.LegacyCompatibility.Tests.Gfx;

/// <summary>
/// Ground and Object share numeric IDs but are distinct namespaces.
/// Homonyms like Ground 1710 / Object 1710 must never be confused.
/// </summary>
public sealed class GfxCategoryNamespaceTests : IDisposable
{
    private readonly string _root;

    public GfxCategoryNamespaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rufus-gfx-ns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Images", "grounds", "Mo Tao"));
        Directory.CreateDirectory(Path.Combine(_root, "Images", "objects", "Casas"));
        Directory.CreateDirectory(Path.Combine(_root, "XML"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    [Fact]
    public void Ground_1710_and_Object_1710_load_as_independent_resources()
    {
        CreatePng("Images/grounds/Mo Tao/1710.png");
        CreatePng("Images/objects/Casas/1710.png");
        WriteAnchors("XML/grounds.xml", (1710, 1, 2));
        WriteAnchors("XML/objects.xml", (1710, 9, -3));

        var catalog = AstriaGfxCatalogBuilder.Build(_root).Catalog;

        Assert.True(catalog.TryGet(GfxCategory.Ground, 1710, out var ground));
        Assert.True(catalog.TryGet(GfxCategory.Object, 1710, out var obj));
        Assert.NotNull(ground);
        Assert.NotNull(obj);
        Assert.Equal(GfxCategory.Ground, ground!.Category);
        Assert.Equal(GfxCategory.Object, obj!.Category);
        Assert.Contains("Mo Tao", ground.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Casas", obj.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(ground.FilePath, obj.FilePath);
        Assert.Equal(new GfxAnchor(1, 2), ground.Anchor);
        Assert.Equal(new GfxAnchor(9, -3), obj.Anchor);
    }

    [Fact]
    public void Resolver_keeps_homonyms_in_their_category()
    {
        CreatePng("Images/grounds/Mo Tao/1710.png");
        CreatePng("Images/objects/Casas/1710.png");
        WriteAnchors("XML/grounds.xml", (1710, 0, 0));
        WriteAnchors("XML/objects.xml", (1710, 0, 0));
        var catalog = AstriaGfxCatalogBuilder.Build(_root).Catalog;

        Assert.True(GfxResourceResolver.TryResolve(catalog, GfxCategory.Ground, 1710, out var ground));
        Assert.True(GfxResourceResolver.TryResolve(catalog, GfxCategory.Object, 1710, out var obj));
        Assert.Contains("Mo Tao", ground.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Casas", obj.FilePath, StringComparison.OrdinalIgnoreCase);

        var cats = GfxResourceResolver.GetCategoriesWithId(catalog, 1710);
        Assert.Contains(GfxCategory.Ground, cats);
        Assert.Contains(GfxCategory.Object, cats);
    }

    [Fact]
    public void Move_Object1_to_Object2_preserves_id_and_clears_source()
    {
        var cell = new CellData
        {
            Object1GfxId = 1710,
            FlipObject1 = true,
            Object1Rotation = 2,
            GroundGfxId = 1710,
        };

        Assert.True(GfxLayerNamespace.TryMoveGfxToLayer(
            cell, MapCellEditor.Layer.Object1, MapCellEditor.Layer.Object2, 1710));

        Assert.Equal(0, cell.Object1GfxId);
        Assert.Equal(1710, cell.Object2GfxId);
        Assert.True(cell.FlipObject2);
        // Homonym on Ground must remain untouched.
        Assert.Equal(1710, cell.GroundGfxId);
    }

    [Fact]
    public void Move_Object2_to_Object1_is_allowed()
    {
        var cell = new CellData { Object2GfxId = 1710, FlipObject2 = true };
        Assert.True(GfxLayerNamespace.TryMoveGfxToLayer(
            cell, MapCellEditor.Layer.Object2, MapCellEditor.Layer.Object1, 1710));
        Assert.Equal(1710, cell.Object1GfxId);
        Assert.Equal(0, cell.Object2GfxId);
        Assert.True(cell.FlipObject1);
    }

    [Fact]
    public void Move_Ground_to_Object1_is_blocked_even_with_same_id()
    {
        var cell = new CellData { GroundGfxId = 1710, Object1GfxId = 0 };
        Assert.False(GfxLayerNamespace.SharesNamespace(
            MapCellEditor.Layer.Ground, MapCellEditor.Layer.Object1));
        Assert.False(GfxLayerNamespace.TryMoveGfxToLayer(
            cell, MapCellEditor.Layer.Ground, MapCellEditor.Layer.Object1, 1710));
        Assert.Equal(1710, cell.GroundGfxId);
        Assert.Equal(0, cell.Object1GfxId);
    }

    [Fact]
    public void Move_Object_to_Ground_is_blocked()
    {
        var cell = new CellData { Object1GfxId = 1710, GroundGfxId = 0 };
        Assert.False(GfxLayerNamespace.TryMoveGfxToLayer(
            cell, MapCellEditor.Layer.Object1, MapCellEditor.Layer.Ground, 1710));
        Assert.Equal(1710, cell.Object1GfxId);
        Assert.Equal(0, cell.GroundGfxId);
    }

    [Fact]
    public void Delete_object_layer_does_not_clear_homonym_ground()
    {
        var cell = new CellData { GroundGfxId = 1710, Object1GfxId = 1710 };
        MapCellEditor.ClearLayer(cell, MapCellEditor.Layer.Object1);
        Assert.Equal(0, cell.Object1GfxId);
        Assert.Equal(1710, cell.GroundGfxId);
    }

    [Fact]
    public void Replace_on_object_layer_does_not_touch_homonym_ground()
    {
        var cell = new CellData { GroundGfxId = 1710, Object1GfxId = 1710 };
        MapCellEditor.SetLayerGfx(cell, MapCellEditor.Layer.Object1, 5004);
        Assert.Equal(5004, cell.Object1GfxId);
        Assert.Equal(1710, cell.GroundGfxId);
    }

    [Fact]
    public void MapData_roundtrip_keeps_ground_and_object_homonyms_apart()
    {
        var cell = new CellData
        {
            Active = true,
            LineOfSight = true,
            Movement = MovementType.Walkable,
            GroundGfxId = 1710,
            Object1GfxId = 1710,
            Object2GfxId = 0,
        };
        var encoded = MapDataCodec.EncodeCell(cell);
        var decoded = MapDataCodec.DecodeCell(encoded);
        Assert.Equal(1710, decoded.GroundGfxId);
        Assert.Equal(1710, decoded.Object1GfxId);
        Assert.Equal(0, decoded.Object2GfxId);
    }

    private void CreatePng(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Minimal valid 1x1 PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        File.WriteAllBytes(path, png);
    }

    private void WriteAnchors(string relativeXml, params (int Id, int X, int Y)[] entries)
    {
        var path = Path.Combine(_root, relativeXml.Replace('/', Path.DirectorySeparatorChar));
        var root = new XElement("ArrayOfPos");
        foreach (var (id, x, y) in entries)
        {
            root.Add(new XElement("Pos",
                new XElement("ID", id),
                new XElement("X", x),
                new XElement("Y", y)));
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        doc.Save(writer);
    }
}
