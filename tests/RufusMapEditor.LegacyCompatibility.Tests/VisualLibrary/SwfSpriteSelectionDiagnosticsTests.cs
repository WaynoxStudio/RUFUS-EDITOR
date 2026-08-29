using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

public sealed class SwfSpriteSelectionDiagnosticsTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(71)]
    public void Selection_uses_staticR_export_not_emote(int gfxId)
    {
        var clips = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "RUFUS RETRO", "resources", "app", "retroclient", "clips");
        if (!Directory.Exists(clips)) return;

        var swf = Path.Combine(clips, "sprites", gfxId + ".swf");
        if (!File.Exists(swf)) return;

        var movie = SwfMovieParser.Parse(File.ReadAllBytes(swf));
        var pick = SwfSpriteSelection.SelectThumbnail(movie);
        Assert.DoesNotContain("emote", pick.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("staticR", pick.LinkageName);
        Assert.True(pick.SpriteId > 0);
    }

    [Fact]
    public void Log_export_names_for_target_gfx_when_clips_present()
    {
        var clips = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "RUFUS RETRO", "resources", "app", "retroclient", "clips");
        if (!Directory.Exists(clips)) return;

        foreach (var gfx in new[] { 30, 71, 1245, 9073 })
        {
            var swf = Path.Combine(clips, "sprites", gfx + ".swf");
            if (!File.Exists(swf)) continue;
            var movie = SwfMovieParser.Parse(File.ReadAllBytes(swf));
            var pick = SwfSpriteSelection.SelectThumbnail(movie);
            var exportSample = string.Join(", ", movie.ExportedNames.Keys.Take(3));
            Console.WriteLine(
                $"GFX {gfx}: linkage={pick.LinkageName} sprite={pick.SpriteId} frame={pick.FrameIndex} reason={pick.Reason} exports={movie.ExportedNames.Count} sample=[{exportSample}]");
            Assert.True(pick.SpriteId > 0);
            Assert.Equal("staticR", pick.LinkageName);
        }
    }
}
