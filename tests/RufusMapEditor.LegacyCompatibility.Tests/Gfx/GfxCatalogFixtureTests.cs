using System.Text;
using System.Xml.Linq;
using RufusMapEditor.Domain.Gfx;
using RufusMapEditor.LegacyCompatibility.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Tests.Gfx;

public sealed class GfxCatalogFixtureTests : IDisposable
{
    private readonly string _root;

    public GfxCatalogFixtureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rufus-gfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Images", "backgrounds"));
        Directory.CreateDirectory(Path.Combine(_root, "Images", "grounds"));
        Directory.CreateDirectory(Path.Combine(_root, "Images", "objects"));
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
            // best-effort cleanup
        }
    }

    [Fact]
    public void Builder_discovers_backgrounds_grounds_and_objects()
    {
        CreateImage("Images/backgrounds/10.png");
        CreateImage("Images/grounds/Herbes/20.png");
        CreateImage("Images/objects/Arbres/30.png");
        WriteAnchors("XML/grounds.xml", (20, 5, 7));
        WriteAnchors("XML/objects.xml", (30, 11, -3));

        var result = AstriaGfxCatalogBuilder.Build(_root);
        var catalog = result.Catalog;

        Assert.Equal(1, catalog.BackgroundCount);
        Assert.Equal(1, catalog.GroundCount);
        Assert.Equal(1, catalog.ObjectCount);
        Assert.Equal(3, catalog.TotalCount);

        Assert.True(catalog.TryGetBackground(10, out var bg));
        Assert.Equal(GfxCategory.Background, bg!.Category);
        Assert.Equal(string.Empty, bg.Folder);

        Assert.True(catalog.TryGetGround(20, out var ground));
        Assert.Equal("Herbes", ground!.Folder);
        Assert.Equal(new GfxAnchor(5, 7), ground.Anchor);

        Assert.True(catalog.TryGetObject(30, out var obj));
        Assert.Equal("Arbres", obj!.Folder);
        Assert.Equal(new GfxAnchor(11, -3), obj.Anchor);
    }

    [Fact]
    public void TryGet_missing_id_returns_false_without_fabricating_resource()
    {
        CreateImage("Images/grounds/Herbes/1.png");
        WriteAnchors("XML/grounds.xml", (1, 0, 0));
        WriteAnchors("XML/objects.xml");

        var catalog = AstriaGfxCatalogBuilder.Build(_root).Catalog;

        Assert.False(catalog.TryGetGround(999999, out var missing));
        Assert.Null(missing);
        Assert.False(catalog.TryGetObject(1, out _)); // ground id must not resolve as object
        Assert.False(catalog.TryGetBackground(1, out _));
    }

    [Fact]
    public void Duplicate_gfx_ids_are_detected_and_last_path_wins()
    {
        // Sorted order: A_Murs then B_Murs → last write wins (B_Murs), matching Astria overwrite.
        CreateImage("Images/objects/A_Murs/100.png");
        CreateImage("Images/objects/B_Murs/100.png");
        WriteAnchors("XML/grounds.xml");
        WriteAnchors("XML/objects.xml", (100, 1, 2));

        var result = AstriaGfxCatalogBuilder.Build(_root);
        Assert.True(result.DuplicateImageIds >= 1);
        Assert.Contains(result.Issues, i => i.Code == GfxIssueCode.DuplicateGfxId && i.GfxId == 100);

        Assert.True(result.Catalog.TryGetObject(100, out var resource));
        Assert.Contains("B_Murs", resource!.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_file_name_is_reported_and_skipped()
    {
        CreateImage("Images/grounds/Herbes/not-an-id.png");
        CreateImage("Images/grounds/Herbes/42.png");
        WriteAnchors("XML/grounds.xml", (42, 3, 4));
        WriteAnchors("XML/objects.xml");

        var result = AstriaGfxCatalogBuilder.Build(_root);
        Assert.Contains(result.Issues, i => i.Code == GfxIssueCode.InvalidFileName);
        Assert.Equal(1, result.Catalog.GroundCount);
        Assert.True(result.Catalog.TryGetGround(42, out _));
    }

    [Fact]
    public void Corrupt_xml_produces_controlled_error()
    {
        CreateImage("Images/grounds/Herbes/1.png");
        Directory.CreateDirectory(Path.Combine(_root, "XML"));
        var xmlPath = Path.Combine(_root, "XML", "grounds.xml");
        File.WriteAllText(xmlPath, "<not-valid>");
        WriteAnchors("XML/objects.xml");

        var result = AstriaGfxCatalogBuilder.Build(_root);
        Assert.Contains(result.Issues, i => i.Code == GfxIssueCode.MalformedXml && i.Severity == GfxIssueSeverity.Error);
        Assert.True(result.Catalog.TryGetGround(1, out var ground));
        Assert.False(ground!.HasAnchor);
    }

    [Fact]
    public void Xml_null_padding_is_stripped_like_astria_shipped_files()
    {
        CreateImage("Images/grounds/Herbes/8.png");
        WriteAnchors("XML/objects.xml");

        var xml = """
                  <?xml version="1.0"?>
                  <ArrayOfPos>
                    <Pos><ID>8</ID><X>12</X><Y>-4</Y></Pos>
                  </ArrayOfPos>
                  """;
        var bytes = Encoding.UTF8.GetBytes(xml);
        var padded = new byte[4096];
        Buffer.BlockCopy(bytes, 0, padded, 0, bytes.Length);
        Directory.CreateDirectory(Path.Combine(_root, "XML"));
        File.WriteAllBytes(Path.Combine(_root, "XML", "grounds.xml"), padded);

        var parsed = GfxAnchorXmlParser.ParseFile(Path.Combine(_root, "XML", "grounds.xml"), GfxCategory.Ground);
        Assert.True(parsed.HadNullPadding);
        Assert.True(parsed.AnchorsById.TryGetValue(8, out var anchor));
        Assert.Equal(12, anchor.X);
        Assert.Equal(-4, anchor.Y);

        var catalog = AstriaGfxCatalogBuilder.Build(_root).Catalog;
        Assert.True(catalog.TryGetGround(8, out var resource));
        Assert.Equal(new GfxAnchor(12, -4), resource!.Anchor);
    }

    [Fact]
    public void Xml_entry_without_image_is_warning()
    {
        CreateImage("Images/grounds/Herbes/1.png");
        CreateImage("Images/objects/Arbres/2.png");
        CreateImage("Images/backgrounds/3.png");
        WriteAnchors("XML/grounds.xml", (1, 0, 0), (999, 1, 1));
        WriteAnchors("XML/objects.xml", (2, 0, 0));

        var result = AstriaGfxCatalogBuilder.Build(_root);
        Assert.Contains(result.Issues, i => i.Code == GfxIssueCode.XmlEntryWithoutImage && i.GfxId == 999);
    }

    [Fact]
    public void Enumerate_supports_future_search_by_category_and_id()
    {
        CreateImage("Images/backgrounds/5.png");
        CreateImage("Images/grounds/Herbes/5.png");
        CreateImage("Images/objects/Arbres/5.png");
        WriteAnchors("XML/grounds.xml", (5, 1, 1));
        WriteAnchors("XML/objects.xml", (5, 2, 2));

        var catalog = AstriaGfxCatalogBuilder.Build(_root).Catalog;
        var byId = catalog.EnumerateById(5).ToList();
        Assert.Equal(3, byId.Count);
        Assert.Single(catalog.Enumerate(GfxCategory.Ground));
    }

    [Fact]
    public async Task FileGfxImageProvider_loads_bytes_on_demand()
    {
        CreateImage("Images/backgrounds/3.png");
        WriteAnchors("XML/grounds.xml");
        WriteAnchors("XML/objects.xml");

        var catalog = AstriaGfxCatalogBuilder.Build(_root).Catalog;
        Assert.True(catalog.TryGetBackground(3, out var resource));

        var provider = new FileGfxImageProvider();
        var data = await provider.GetImageAsync(resource!);
        Assert.NotEmpty(data.Bytes);
        Assert.Equal("image/png", data.ContentTypeHint);
    }

    private void CreateImage(string relativePath)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // Minimal valid 1x1 PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        File.WriteAllBytes(full, png);
    }

    private void WriteAnchors(string relativePath, params (int id, int x, int y)[] entries)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var root = new XElement("ArrayOfPos",
            entries.Select(e => new XElement("Pos",
                new XElement("ID", e.id),
                new XElement("X", e.x),
                new XElement("Y", e.y))));
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
        };
        using var writer = System.Xml.XmlWriter.Create(full, settings);
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
    }
}
