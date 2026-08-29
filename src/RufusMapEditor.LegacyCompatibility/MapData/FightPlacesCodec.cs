using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.LegacyCompatibility.MapData;

/// <summary>
/// Astria-compatible fight placement hash ("team1|team2") stored outside MapData.
/// </summary>
public static class FightPlacesCodec
{
    private const string Hash = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    public static void ApplyToCells(IList<CellData> cells, string? fightPlaces)
    {
        ArgumentNullException.ThrowIfNull(cells);
        foreach (var cell in cells)
            cell.FightCell = 0;

        if (string.IsNullOrEmpty(fightPlaces))
            return;

        var parts = fightPlaces.Split('|');
        if (parts.Length == 0)
            return;

        ParseTeam(parts[0], cells, 1);
        if (parts.Length > 1)
            ParseTeam(parts[1], cells, 2);
    }

    public static string Encode(IReadOnlyList<CellData> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var team1 = new System.Text.StringBuilder();
        var team2 = new System.Text.StringBuilder();
        for (var i = 0; i < cells.Count; i++)
        {
            switch (cells[i].FightCell)
            {
                case 1: team1.Append(EncodeCellId(i)); break;
                case 2: team2.Append(EncodeCellId(i)); break;
            }
        }

        return $"{team1}|{team2}";
    }

    private static void ParseTeam(string data, IList<CellData> cells, int team)
    {
        if (string.IsNullOrEmpty(data))
            return;

        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var cellId = DecodeCellId(data[i], data[i + 1]);
            if (cellId >= 0 && cellId < cells.Count)
                cells[cellId].FightCell = team;
        }
    }

    private static string EncodeCellId(int cellId)
    {
        var mod = cellId % Hash.Length;
        var div = cellId / Hash.Length;
        return $"{Hash[div]}{Hash[mod]}";
    }

    private static int DecodeCellId(char hi, char lo)
    {
        var div = Hash.IndexOf(hi);
        var mod = Hash.IndexOf(lo);
        if (div < 0 || mod < 0)
            return -1;
        return div * Hash.Length + mod;
    }
}
