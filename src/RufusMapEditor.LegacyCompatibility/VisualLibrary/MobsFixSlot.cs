namespace RufusMapEditor.LegacyCompatibility.VisualLibrary;

/// <summary>LIB.4 — one monster slot for a fixed group (mobs_modelo.id, not gfx).</summary>
public sealed record MobsFixSlot(int MobId, int MinLvl, int MaxLvl);
