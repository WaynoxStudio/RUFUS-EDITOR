using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.VisualLibrary;
using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.Tests.VisualLibrary;

/// <summary>Offline audit helper — not run in default CI filter.</summary>
public sealed class NpcSpritePreviewCoverageAuditTests
{
    private static string? ResolveClipsRoot()
    {
        var c = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "RUFUS RETRO", "resources", "app", "retroclient", "clips");
        return Directory.Exists(Path.Combine(c, "sprites")) ? c : Environment.GetEnvironmentVariable("RUFUS_CLIPS_ROOT");
    }

    [Fact(Skip = "Manual/offline audit — run explicitly to measure 251 GFX coverage.")]
    public void Audit_confirmed_npc_gfx_sprite_coverage()
    {
        var clips = ResolveClipsRoot();
        Assert.NotNull(clips);

        var lib = Path.Combine(Path.GetTempPath(), "rufus-audit-" + Guid.NewGuid().ToString("N"), "Library");
        Directory.CreateDirectory(lib);
        var svc = new NpcGfxPreviewService();
        svc.Configure(clips, lib);

        // Representative sample; full 251 requires DB — use catalog file if present.
        var sampleIds = new[] { 30, 71, 120, 1245, 9073 };
        var spriteOk = 0;
        var artworkOnly = 0;
        var none = 0;

        foreach (var id in sampleIds)
        {
            svc.SpriteCache.ClearFailed(id);
            var swf = Path.Combine(clips!, "sprites", id + ".swf");
            if (!File.Exists(swf)) { none++; continue; }
            try
            {
                SwfSpriteThumbnailRenderer.RasterizeToPng(File.ReadAllBytes(swf), 96, id);
                spriteOk++;
            }
            catch
            {
                artworkOnly++;
            }
        }

        Assert.True(spriteOk > 0);
        _ = artworkOnly;
        _ = none;
    }
}
