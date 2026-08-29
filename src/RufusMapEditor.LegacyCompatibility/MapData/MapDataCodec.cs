using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.MapData;

/// <summary>
/// Lossless classical MapData codec (10 chars / cell), ported from Astria
/// <c>Builder.GetCellData</c> / <c>Map.UncompressCell</c>.
/// Stores movement as a raw 3-bit value to avoid Astria's Trigger/TriggerCell bug.
/// </summary>
public static class MapDataCodec
{
    /// <summary>Same alphabet as Astria <c>Builder.ZKARRAY</c> / <c>Decryptage.HashCodes</c>.</summary>
    public const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    private static readonly int[] AlphabetIndex = BuildAlphabetIndex();

    public static CellData DecodeCell(ReadOnlySpan<char> cellData)
    {
        if (cellData.Length != MapDataConstants.CharsPerCell)
            throw new ArgumentException($"Cell MapData must be {MapDataConstants.CharsPerCell} characters.", nameof(cellData));

        Span<int> n = stackalloc int[MapDataConstants.CharsPerCell];
        for (var i = 0; i < MapDataConstants.CharsPerCell; i++)
            n[i] = HashCode(cellData[i]);

        var cell = new CellData
        {
            Active = (n[0] & 0x20) != 0,
            LineOfSight = (n[0] & 1) != 0,
            GroundRotation = (n[1] & 0x30) >> 4,
            GroundLevel = n[1] & 0x0F,
            Movement = (MovementType)(((n[2] & 0x38) >> 3) & 7),
            GroundGfxId = ((n[0] & 0x18) << 6) + ((n[2] & 7) << 6) + n[3],
            GroundSlope = (n[4] & 0x3C) >> 2,
            FlipGround = ((n[4] & 2) >> 1) != 0,
            Object1GfxId = ((n[0] & 4) << 11) + ((n[4] & 1) << 12) + (n[5] << 6) + n[6],
            Object1Rotation = (n[7] & 0x30) >> 4,
            FlipObject1 = ((n[7] & 8) >> 3) != 0,
            FlipObject2 = ((n[7] & 4) >> 2) != 0,
            InteractiveObject = ((n[7] & 2) >> 1) != 0,
            Object2GfxId = ((n[0] & 2) << 12) + ((n[7] & 1) << 12) + (n[8] << 6) + n[9],
        };

        return cell;
    }

    public static string EncodeCell(CellData cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var gfx1 = cell.GroundGfxId;
        var gfx2 = cell.Object1GfxId;
        var gfx3 = cell.Object2GfxId;

        Span<int> n = stackalloc int[MapDataConstants.CharsPerCell];

        // Astria Builder always forces Active (0x20). For bit-perfect roundtrip of real maps
        // we preserve the decoded Active flag when encoding (existing fixtures have it set).
        n[0] = cell.Active ? 0x20 : 0;
        if (cell.LineOfSight)
            n[0] |= 1;

        n[0] |= (gfx1 & 0x600) >> 6;
        n[0] |= (gfx2 & 0x2000) >> 11;
        n[0] |= (gfx3 & 0x2000) >> 12;

        n[1] = (cell.GroundRotation & 3) << 4;
        n[1] |= cell.GroundLevel & 15;

        n[2] = ((int)cell.Movement & 7) << 3;
        n[2] |= (gfx1 >> 6) & 7;

        n[3] = gfx1 & 0x3F;

        n[4] = (cell.GroundSlope & 15) << 2;
        if (cell.FlipGround)
            n[4] |= 2;
        n[4] |= (gfx2 >> 12) & 1;

        n[5] = (gfx2 >> 6) & 0x3F;
        n[6] = gfx2 & 0x3F;

        n[7] = (cell.Object1Rotation & 3) << 4;
        if (cell.FlipObject1)
            n[7] |= 8;
        if (cell.FlipObject2)
            n[7] |= 4;
        if (cell.InteractiveObject)
            n[7] |= 2;
        n[7] |= (gfx3 >> 12) & 1;

        n[8] = (gfx3 >> 6) & 0x3F;
        n[9] = gfx3 & 0x3F;

        return string.Create(MapDataConstants.CharsPerCell, n, static (dest, values) =>
        {
            for (var i = 0; i < MapDataConstants.CharsPerCell; i++)
                dest[i] = Alphabet[values[i]];
        });
    }

    public static CellData[] DecodeMap(string mapData)
    {
        ArgumentNullException.ThrowIfNull(mapData);
        if (mapData.Length % MapDataConstants.CharsPerCell != 0)
            throw new ArgumentException($"MapData length {mapData.Length} is not a multiple of {MapDataConstants.CharsPerCell}.", nameof(mapData));

        var cellCount = mapData.Length / MapDataConstants.CharsPerCell;
        var cells = new CellData[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            var offset = i * MapDataConstants.CharsPerCell;
            cells[i] = DecodeCell(mapData.AsSpan(offset, MapDataConstants.CharsPerCell));
        }

        return cells;
    }

    public static string EncodeMap(IReadOnlyList<CellData> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return string.Create(cells.Count * MapDataConstants.CharsPerCell, cells, static (dest, source) =>
        {
            for (var i = 0; i < source.Count; i++)
            {
                var encoded = EncodeCell(source[i]);
                encoded.AsSpan().CopyTo(dest.Slice(i * MapDataConstants.CharsPerCell, MapDataConstants.CharsPerCell));
            }
        });
    }

    public static string RoundTrip(string mapData) => EncodeMap(DecodeMap(mapData));

    /// <summary>
    /// Serializes one cell using the same encoder as <see cref="EncodeMap"/>.
    /// Astria equivalent: <c>Cell.GetDatas</c> / builder block (10 chars).
    /// </summary>
    public static string EncodeCellBlock(CellData cell) => EncodeCell(cell);

    /// <summary>
    /// Extracts the cell block at <paramref name="cellIndex"/> from a full MapData string.
    /// Cell index matches decode order / Astria Cell ID in our editor.
    /// </summary>
    public static string ExtractCellBlock(string mapData, int cellIndex)
    {
        ArgumentNullException.ThrowIfNull(mapData);
        if (cellIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cellIndex));

        var offset = cellIndex * MapDataConstants.CharsPerCell;
        if (offset + MapDataConstants.CharsPerCell > mapData.Length)
            throw new ArgumentOutOfRangeException(nameof(cellIndex), "Cell index out of range for MapData length.");

        return mapData.Substring(offset, MapDataConstants.CharsPerCell);
    }

    /// <summary>
    /// Inclusive character range of a cell block inside full MapData (0-based).
    /// </summary>
    public static (int Start, int End) GetCellBlockCharRange(int cellIndex)
    {
        if (cellIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cellIndex));
        var start = cellIndex * MapDataConstants.CharsPerCell;
        return (start, start + MapDataConstants.CharsPerCell - 1);
    }

    /// <summary>
    /// Returns diff positions (0..9) between two 10-char cell blocks.
    /// </summary>
    public static IReadOnlyList<int> GetChangedPositions(string baseline, string current)
    {
        if (baseline.Length != MapDataConstants.CharsPerCell || current.Length != MapDataConstants.CharsPerCell)
            return Array.Empty<int>();

        var list = new List<int>(MapDataConstants.CharsPerCell);
        for (var i = 0; i < MapDataConstants.CharsPerCell; i++)
        {
            if (baseline[i] != current[i])
                list.Add(i);
        }

        return list;
    }

    public static string FormatChangedPositionsHint(string baseline, string current)
    {
        var positions = GetChangedPositions(baseline, current);
        if (positions.Count == 0)
            return string.Empty;

        return "Pos " + string.Join(", ", positions.Select(p => p.ToString()));
    }

    public static int HashCode(char c)
    {
        var index = AlphabetIndex[c];
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(c), c, "Character is not part of the MapData alphabet.");
        return index;
    }

    private static int[] BuildAlphabetIndex()
    {
        var table = new int[128];
        Array.Fill(table, -1);
        for (var i = 0; i < Alphabet.Length; i++)
            table[Alphabet[i]] = i;
        return table;
    }
}
