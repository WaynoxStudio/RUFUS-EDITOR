using RufusMapEditor.LegacyCompatibility.Portable;

namespace RufusMapEditor.LegacyCompatibility.Tests.Portable;

public sealed class PortableLibraryTests
{
    [Fact]
    public void GetApplicationDirectory_uses_process_path_not_cwd()
    {
        var cwd = Directory.GetCurrentDirectory();
        var temp = Path.Combine(Path.GetTempPath(), $"rufus_portable_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            Directory.SetCurrentDirectory(temp);
            var appDir = PortableLibraryPaths.GetApplicationDirectory();
            Assert.NotEqual(temp, appDir);
            Assert.True(Directory.Exists(appDir));
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
            try { Directory.Delete(temp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Sibling_library_path_is_next_to_application_directory()
    {
        var appDir = PortableLibraryPaths.GetApplicationDirectory();
        var expected = Path.Combine(appDir, PortableLibraryPaths.LibraryFolderName);
        Assert.Equal(expected, PortableLibraryPaths.GetSiblingLibraryPath());
    }

    [Fact]
    public void Validator_accepts_minimal_valid_layout()
    {
        var root = CreateMinimalLibrary();
        try
        {
            var result = PortableLibraryValidator.Validate(root);
            Assert.True(result.IsValidForEditor);
            Assert.False(result.HasFlasmExport);
            Assert.Equal(0, result.MapCount);
            Assert.Contains(result.Warnings, w => w.Contains("Flasm", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Validator_rejects_missing_xml()
    {
        var root = CreateMinimalLibrary(includeXml: false);
        try
        {
            var result = PortableLibraryValidator.Validate(root);
            Assert.False(result.IsValidForEditor);
            Assert.Contains(result.Errors, e => e.Contains("grounds.xml", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Validator_counts_maps_and_detects_flasm()
    {
        var root = CreateMinimalLibrary(includeFlasm: true, mapId: 10420);
        try
        {
            var result = PortableLibraryValidator.Validate(root);
            Assert.True(result.IsValidForEditor);
            Assert.True(result.HasFlasmExport);
            Assert.Equal(1, result.MapCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Paths_with_spaces_are_supported()
    {
        var root = Path.Combine(Path.GetTempPath(), "RUFUS Portable Test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var lib = Path.Combine(root, "Library");
            BuildMinimalTree(lib, includeXml: true, includeFlasm: false, mapId: null);
            var validation = PortableLibraryValidator.Validate(lib);
            Assert.True(validation.IsValidForEditor);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    private static string CreateMinimalLibrary(bool includeXml = true, bool includeFlasm = false, int? mapId = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"rufus_lib_{Guid.NewGuid():N}");
        BuildMinimalTree(root, includeXml, includeFlasm, mapId);
        return root;
    }

    private static void BuildMinimalTree(string root, bool includeXml, bool includeFlasm, int? mapId)
    {
        Directory.CreateDirectory(Path.Combine(root, "Maps"));
        Directory.CreateDirectory(Path.Combine(root, "Images", "backgrounds"));
        Directory.CreateDirectory(Path.Combine(root, "Images", "grounds", "cat"));
        Directory.CreateDirectory(Path.Combine(root, "Images", "objects", "cat"));

        if (includeXml)
        {
            Directory.CreateDirectory(Path.Combine(root, "XML"));
            File.WriteAllText(Path.Combine(root, "XML", "grounds.xml"),
                """<?xml version="1.0"?><ArrayOfPos xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"></ArrayOfPos>""");
            File.WriteAllText(Path.Combine(root, "XML", "objects.xml"),
                """<?xml version="1.0"?><ArrayOfPos xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"></ArrayOfPos>""");
        }

        if (includeFlasm)
        {
            var flasmDir = Path.Combine(root, "Flasm");
            Directory.CreateDirectory(flasmDir);
            File.WriteAllBytes(Path.Combine(flasmDir, "blank.swf"), new byte[200]);
            File.WriteAllText(Path.Combine(flasmDir, "flasm.exe"), "stub");
        }

        if (mapId is int id)
        {
            var mapDir = Path.Combine(root, "Maps", id.ToString());
            Directory.CreateDirectory(mapDir);
            File.WriteAllText(Path.Combine(mapDir, $"{id}.sql"), "-- stub");
        }
    }
}
