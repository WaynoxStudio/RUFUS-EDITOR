namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>ADMIN.UI.4B.2A.3G.1 — outcome of one gfxId prep attempt (dev-only).</summary>
public enum NpcGfxPreviewPrepStatus
{
    Ok = 0,
    Review = 1,
    Failed = 2,
    NoArtwork = 3,
    ManualExists = 4,
}
