using System.Globalization;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>Parse/build preg* fields stored as npcId;preguntaId in misiones.</summary>
public static class MissionPregPairCodec
{
    public static bool TryParse(string? value, out int npcId, out int preguntaId)
    {
        npcId = 0;
        preguntaId = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(';', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out npcId))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out preguntaId))
            return false;
        return npcId > 0 && preguntaId > 0;
    }

    public static string Build(int? npcId, int? preguntaId) =>
        MissionDraft.BuildPregPair(npcId, preguntaId);
}
