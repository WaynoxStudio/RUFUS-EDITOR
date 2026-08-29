namespace RufusMapEditor.Domain.Gfx;

/// <summary>
/// Anchor / pivot point from Astria <c>grounds.xml</c> / <c>objects.xml</c> (<c>Tile.Pos</c>).
/// Negative coordinates are valid and preserved as-is.
/// </summary>
public readonly struct GfxAnchor : IEquatable<GfxAnchor>
{
    public GfxAnchor(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }

    public bool Equals(GfxAnchor other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is GfxAnchor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
