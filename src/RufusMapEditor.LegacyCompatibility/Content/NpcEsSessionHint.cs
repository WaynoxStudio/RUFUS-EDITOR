namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT.7B — last known active npc_es version from RO preview/publish (UI hint only).</summary>
public static class NpcEsSessionHint
{
    public static int? LastKnownActiveVersion { get; private set; }

    public static void SetActiveVersion(int version)
    {
        if (version > 0)
            LastKnownActiveVersion = version;
    }

    public static void Clear() => LastKnownActiveVersion = null;
}
