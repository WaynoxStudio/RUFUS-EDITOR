namespace RufusMapEditor.LegacyCompatibility.MapPublishQueue;

/// <summary>MAP-BATCH.1 — queued map metadata only (no full document).</summary>
public sealed class MapPublishQueueItem
{
    public const int DefaultEp = 2;

    public int MapId { get; set; }

    /// <summary>SHA-256 hex of official .rufmap at enqueue/update time.</summary>
    public string RufmapSha256 { get; set; } = "";

    /// <summary>Editor revision (DateMap) at enqueue time.</summary>
    public string DateMapSnapshot { get; set; } = "";

    public long RufmapUtcTicks { get; set; }

    public DateTimeOffset QueuedUtc { get; set; }

    /// <summary>True when the user has set SubArea (sa). Never invent sa.</summary>
    public bool SubAreaDefined { get; set; }

    /// <summary>LANG SubArea (sa) — only meaningful when <see cref="SubAreaDefined"/>.</summary>
    public int SubArea { get; set; }

    /// <summary>LANG EP — defaults to <see cref="DefaultEp"/> until edited.</summary>
    public int Ep { get; set; } = DefaultEp;

    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public bool WorldCoordinatesSet { get; set; }
}

public sealed class MapPublishQueueDocument
{
    /// <summary>2+ = SubAreaDefined flag is authoritative (MAP-BATCH.1.3).</summary>
    public int Version { get; set; } = 2;
    public List<MapPublishQueueItem> Items { get; set; } = new();
}

public enum MapPublishQueueItemStatus
{
    Ready,
    ModifiedAfterQueued,
    UnsavedChanges,
    MissingLocalSave,
    /// <summary>In queue but missing sa and/or X/Y — not publishable yet.</summary>
    MissingPublishFields,
}

public enum MapPublishDbKind
{
    Unknown,
    Insert,
    Update,
}

public sealed class MapPublishQueueItemView
{
    public required MapPublishQueueItem Item { get; init; }
    public MapPublishQueueItemStatus Status { get; init; }
    public MapPublishDbKind DbKind { get; init; }
    public string StatusLabel { get; init; } = "";
    public string DbKindLabel { get; init; } = "";
}
