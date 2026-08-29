namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal readonly record struct SwfSpritePick(int SpriteId, int FrameIndex, string Reason, string? LinkageName = null);

/// <summary>
/// ADMIN.UI.4B.2A.3E.1 — NPC preview selection aligned with Dofus Retro
/// <c>attachMovie(linkage)</c> semantics (ChooseCharacterSprite / MonsterListItem).
/// </summary>
internal static class SwfSpriteSelection
{
    /// <summary>Primary linkage for NPC preview: setAnim("static", true) → direction R.</summary>
    public const string PrimaryNpcPreviewLinkage = "staticR";

    /// <summary>
    /// Client-real fallback order (exact linkage names only):
    /// 1. staticR — ChooseCharacterSprite.setAnim("static", true); MonsterListItem grid icon.
    /// 2. staticF — ChooseCharacterSprite.changeSpriteOrientation first attachMovie attempt.
    /// 3. staticL — ank.battlefield.mc.Sprite.setAnim direction 5 (L).
    /// 4. staticS — ank.battlefield.mc.Sprite.setAnim direction 0 (S).
    /// 5. staticB — ank.battlefield.mc.Sprite.setAnim direction 6 (B).
    /// </summary>
    public static readonly string[] ClientStaticFallbackOrder =
    [
        "staticR",
        "staticF",
        "staticL",
        "staticS",
        "staticB",
    ];

    public static SwfSpritePick SelectThumbnail(SwfMovie movie) =>
        GetThumbnailCandidates(movie).FirstOrDefault();

    public static IReadOnlyList<SwfSpritePick> GetThumbnailCandidates(SwfMovie movie)
    {
        var list = new List<SwfSpritePick>();

        foreach (var linkage in ClientStaticFallbackOrder)
        {
            if (TryPickExactExport(movie, linkage, out var pick))
                list.Add(pick);
        }

        if (list.Count > 0)
            return list;

        if (TryWalkCycleHeuristic(movie, out var walkPick))
            list.Add(walkPick);

        if (TryHeuristic(movie, out var heuristic))
            list.Add(heuristic);

        if (movie.Sprites.Count > 0)
        {
            var any = movie.Sprites.Values.OrderByDescending(s => s.PayloadBytes).First();
            list.Add(new SwfSpritePick(any.CharacterId, 0, "fallback largest sprite payload", null));
        }
        else
        {
            list.Add(new SwfSpritePick(0, 0, "no sprites", null));
        }

        return list;
    }

    /// <summary>Exact ExportAssets / SymbolClass name lookup — no Contains, no emote substitutes.</summary>
    public static bool TryResolveExactExport(SwfMovie movie, string linkageName, out int characterId)
    {
        characterId = 0;
        return movie.ExportedNames.TryGetValue(linkageName, out characterId);
    }

    public static bool TryPickExactExport(SwfMovie movie, string linkageName, out SwfSpritePick pick)
    {
        pick = default;
        if (!TryResolveExactExport(movie, linkageName, out var charId))
            return false;
        if (!movie.Sprites.TryGetValue(charId, out var sprite))
            return false;

        var frame = PickRepresentativeFrame(sprite);
        var wrapper = sprite.PayloadBytes < 80;
        pick = new SwfSpritePick(
            charId,
            frame,
            wrapper
                ? $"ExportAssets '{linkageName}' (wrapper {sprite.PayloadBytes}b)"
                : $"ExportAssets '{linkageName}'",
            linkageName);
        return true;
    }

    private static int PickRepresentativeFrame(SwfSpriteDefinition sprite) => 0;

    private static bool TryWalkCycleHeuristic(SwfMovie movie, out SwfSpritePick pick)
    {
        pick = default;
        SwfSpriteDefinition? best = null;
        var bestScore = 0L;

        foreach (var sprite in movie.Sprites.Values)
        {
            if (sprite.PayloadBytes < 2000)
                continue;
            if (sprite.FrameCount is < 24 or > 120)
                continue;

            var score = (long)sprite.PayloadBytes * sprite.FrameCount;
            if (score > bestScore)
            {
                bestScore = score;
                best = sprite;
            }
        }

        if (best is null)
            return false;

        pick = new SwfSpritePick(
            best.CharacterId,
            0,
            $"walk-cycle heuristic payload={best.PayloadBytes}b frames={best.FrameCount}",
            null);
        return true;
    }

    private static bool TryHeuristic(SwfMovie movie, out SwfSpritePick pick)
    {
        pick = default;
        SwfSpriteDefinition? best = null;
        var bestScore = 0L;

        foreach (var sprite in movie.Sprites.Values)
        {
            if (sprite.PayloadBytes < 200)
                continue;
            if (sprite.FrameCount <= 0)
                continue;

            var score = (long)sprite.PayloadBytes * Math.Min(sprite.FrameCount, 64);
            if (score > bestScore)
            {
                bestScore = score;
                best = sprite;
            }
        }

        if (best is null)
            return false;

        pick = new SwfSpritePick(
            best.CharacterId,
            0,
            $"heuristic payload={best.PayloadBytes}b frames={best.FrameCount}",
            null);
        return true;
    }
}
