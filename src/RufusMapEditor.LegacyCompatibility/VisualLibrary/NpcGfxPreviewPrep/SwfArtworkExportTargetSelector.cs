using RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>
/// ADMIN.UI.4B.2A.3G.2B/2C/2D — choose artwork export method from SWF structure.
/// Prefers a unique composite DefineSprite wrapper when it alone carries the root composition;
/// otherwise full-root-svg for multi-piece / off-stage root artwork; frame only for simple in-stage roots.
/// Does not use opaque-pixel metrics.
/// </summary>
public static class SwfArtworkExportTargetSelector
{
    public enum ExportKind
    {
        Frame = 0,
        Sprite = 1,
        /// <summary>Multi-piece / off-stage root — FFDec frame:svg → expand/drawing bounds → PNG.</summary>
        FullRootSvg = 2,
    }

    public sealed class Selection
    {
        public required ExportKind Kind { get; init; }
        public int? SpriteCharacterId { get; init; }
        public required string Reason { get; init; }
        public int NestedPlaceCount { get; init; }
        public int RootPlacedSpriteCount { get; init; }
        public int RootPlaceCount { get; init; }
        public int RootShapeCount { get; init; }
        public int ContentRootSpriteCount { get; init; }
        public bool HasOffStagePlace { get; init; }
    }

    /// <summary>Minimum nested PlaceObject tags inside a sprite to treat it as a composite artwork wrapper.</summary>
    public const int MinNestedPlacesForComposite = 3;

    /// <summary>
    /// Nested places ≥ this mark a root sprite as "content" (not just a mask/empty shell).
    /// Used to avoid picking one deep sprite when other content sprites share the root.
    /// </summary>
    public const int MinNestedPlacesForContentSprite = 2;

    /// <summary>Pixels: translate below this (negative) counts as off-stage artwork.</summary>
    public const float OffStagePixelEpsilon = -1f;

    public static Selection Select(byte[] swfBytes)
    {
        if (swfBytes is null || swfBytes.Length < 8)
        {
            return new Selection
            {
                Kind = ExportKind.Frame,
                Reason = "SWF inválido — frame",
            };
        }

        SwfMovie movie;
        try
        {
            movie = SwfMovieParser.Parse(swfBytes);
        }
        catch
        {
            return new Selection
            {
                Kind = ExportKind.Frame,
                Reason = "Parse SWF falló — frame",
            };
        }

        var places = CollectRootPlaces(movie);
        var rootSprites = places.Where(p => p.Kind == PlaceKind.Sprite).ToList();
        var rootShapes = places.Count(p => p.Kind == PlaceKind.Shape);
        var rootBitmaps = places.Count(p => p.Kind == PlaceKind.Bitmap);
        var hasOffStage = places.Any(p => p.HasOffStageTranslate);
        var contentSprites = rootSprites
            .Where(p => p.NestedPlaces >= MinNestedPlacesForContentSprite)
            .GroupBy(p => p.CharacterId)
            .Select(g => g.First())
            .ToList();
        var compositeSprites = rootSprites
            .Where(p => p.NestedPlaces >= MinNestedPlacesForComposite)
            .GroupBy(p => p.CharacterId)
            .Select(g => g.OrderByDescending(x => x.NestedPlaces).ThenByDescending(x => x.PayloadBytes).First())
            .OrderByDescending(p => p.NestedPlaces)
            .ThenByDescending(p => p.PayloadBytes)
            .ToList();

        var best = compositeSprites.FirstOrDefault()
                   ?? rootSprites.OrderByDescending(p => p.NestedPlaces).ThenByDescending(p => p.PayloadBytes).FirstOrDefault();

        // A) Unique composite wrapper that alone represents root content sprites
        //    (extra root shapes/hitboxes/masks with nested < 2 are allowed).
        if (compositeSprites.Count == 1
            && contentSprites.Count == 1
            && contentSprites[0].CharacterId == compositeSprites[0].CharacterId)
        {
            var w = compositeSprites[0];
            return new Selection
            {
                Kind = ExportKind.Sprite,
                SpriteCharacterId = w.CharacterId,
                NestedPlaceCount = w.NestedPlaces,
                RootPlacedSpriteCount = rootSprites.Select(s => s.CharacterId).Distinct().Count(),
                RootPlaceCount = places.Count,
                RootShapeCount = rootShapes,
                ContentRootSpriteCount = contentSprites.Count,
                HasOffStagePlace = hasOffStage,
                Reason =
                    $"Wrapper DefineSprite chid={w.CharacterId} único en root · nestedPlaces={w.NestedPlaces} — sprite",
            };
        }

        // B) Multi-piece root and/or off-stage composition without a unique wrapper
        if (places.Count > 1
            || hasOffStage
            || contentSprites.Count > 1
            || (rootShapes > 0 && rootSprites.Count == 0))
        {
            return new Selection
            {
                Kind = ExportKind.FullRootSvg,
                SpriteCharacterId = best?.CharacterId,
                NestedPlaceCount = best?.NestedPlaces ?? 0,
                RootPlacedSpriteCount = rootSprites.Select(s => s.CharacterId).Distinct().Count(),
                RootPlaceCount = places.Count,
                RootShapeCount = rootShapes,
                ContentRootSpriteCount = contentSprites.Count,
                HasOffStagePlace = hasOffStage,
                Reason =
                    $"Root composición (places={places.Count}, sprites={rootSprites.Select(s => s.CharacterId).Distinct().Count()}, shapes={rootShapes}, contentSprites={contentSprites.Count}, offStage={hasOffStage}) — full-root-svg",
            };
        }

        // C) Single root sprite (shallow or deep)
        if (rootSprites.Count == 1 && best is not null)
        {
            return new Selection
            {
                Kind = ExportKind.Sprite,
                SpriteCharacterId = best.CharacterId,
                NestedPlaceCount = best.NestedPlaces,
                RootPlacedSpriteCount = 1,
                RootPlaceCount = places.Count,
                RootShapeCount = rootShapes,
                ContentRootSpriteCount = contentSprites.Count,
                HasOffStagePlace = hasOffStage,
                Reason =
                    $"Único DefineSprite chid={best.CharacterId} en root · nestedPlaces={best.NestedPlaces} — sprite",
            };
        }

        // D) Simple in-stage root (single shape/bitmap or empty)
        return new Selection
        {
            Kind = ExportKind.Frame,
            Reason =
                places.Count == 0
                    ? "Sin PlaceObject en root — frame"
                    : $"Root simple in-stage (places={places.Count}, shapes={rootShapes}, bitmaps={rootBitmaps}) — frame",
            RootPlacedSpriteCount = 0,
            RootPlaceCount = places.Count,
            RootShapeCount = rootShapes,
            ContentRootSpriteCount = 0,
            HasOffStagePlace = hasOffStage,
            NestedPlaceCount = 0,
        };
    }

    private enum PlaceKind
    {
        Sprite,
        Shape,
        Bitmap,
        Other,
    }

    private sealed class RootPlace
    {
        public required int CharacterId { get; init; }
        public required PlaceKind Kind { get; init; }
        public int NestedPlaces { get; init; }
        public int PayloadBytes { get; init; }
        public bool HasOffStageTranslate { get; init; }
    }

    private static List<RootPlace> CollectRootPlaces(SwfMovie movie)
    {
        var list = new List<RootPlace>();
        foreach (var tag in SwfStream.EnumerateTags(movie.Body, movie.RootTimelineStart, movie.RootTimelineEnd))
        {
            if (tag.Code is not (4 or 26 or 70))
                continue;
            if (!TryReadPlace(tag.Code, tag.Data, out var charId, out var hasMatrix, out var tx, out var ty)
                || charId <= 0)
                continue;

            PlaceKind kind;
            var nested = 0;
            var payload = 0;
            if (movie.Sprites.TryGetValue(charId, out var spr))
            {
                kind = PlaceKind.Sprite;
                nested = CountPlaceObjects(spr.TagBuffer, spr.TagStart, spr.TagEnd);
                payload = spr.PayloadBytes;
            }
            else if (movie.Shapes.ContainsKey(charId))
            {
                kind = PlaceKind.Shape;
            }
            else if (movie.Bitmaps.ContainsKey(charId))
            {
                kind = PlaceKind.Bitmap;
            }
            else
            {
                kind = PlaceKind.Other;
            }

            var offStage = hasMatrix && (tx <= OffStagePixelEpsilon || ty <= OffStagePixelEpsilon);
            list.Add(new RootPlace
            {
                CharacterId = charId,
                Kind = kind,
                NestedPlaces = nested,
                PayloadBytes = payload,
                HasOffStageTranslate = offStage,
            });
        }

        return list;
    }

    private static int CountPlaceObjects(byte[] buffer, int start, int end)
    {
        var n = 0;
        foreach (var tag in SwfStream.EnumerateTags(buffer, start, end))
        {
            if (tag.Code is 4 or 26 or 70)
                n++;
        }

        return n;
    }

    private static bool TryReadPlace(
        int code,
        byte[] data,
        out int characterId,
        out bool hasMatrix,
        out float translateXPx,
        out float translateYPx)
    {
        characterId = 0;
        hasMatrix = false;
        translateXPx = 0;
        translateYPx = 0;
        if (data.Length < 3)
            return false;
        try
        {
            var br = new SwfBitReader(data);
            if (code == 4)
            {
                // PlaceObject (SWF1): character then depth then matrix…
                characterId = br.ReadUi16();
                _ = br.ReadUi16();
                var m = SwfMatrix.Read(br);
                hasMatrix = true;
                translateXPx = m.Tx;
                translateYPx = m.Ty;
                return characterId > 0;
            }

            var flags = br.ReadUi8();
            if (code == 70 && (flags & 0x80) != 0 && br.BytePosition < data.Length)
                _ = br.ReadUi8();
            if (br.BytePosition + 2 > data.Length)
                return false;
            _ = br.ReadUi16(); // depth
            var hasChar = (flags & 0x02) != 0;
            if (!hasChar)
                return false;
            characterId = br.ReadUi16();
            if ((flags & 0x04) != 0)
            {
                var m = SwfMatrix.Read(br);
                hasMatrix = true;
                translateXPx = m.Tx;
                translateYPx = m.Ty;
            }

            return characterId > 0;
        }
        catch
        {
            return false;
        }
    }
}
