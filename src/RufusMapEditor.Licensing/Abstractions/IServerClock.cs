namespace RufusMapEditor.Licensing.Abstractions;

/// <summary>Server clock — never client Windows clock.</summary>
public interface IServerClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemServerClock : IServerClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Test clock; mutable.</summary>
public sealed class FakeServerClock : IServerClock
{
    public FakeServerClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; set; }
}
