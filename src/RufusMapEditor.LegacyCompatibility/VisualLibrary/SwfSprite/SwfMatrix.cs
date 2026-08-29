using System.Drawing;
using System.Numerics;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal readonly struct SwfMatrix
{
    public float A { get; init; } = 1f;
    public float B { get; init; }
    public float C { get; init; }
    public float D { get; init; } = 1f;
    public float Tx { get; init; }
    public float Ty { get; init; }

    public SwfMatrix() { }

    public static SwfMatrix Identity => new();

    public static SwfMatrix Read(SwfBitReader br)
    {
        br.Align();
        var a = 1f;
        var b = 0f;
        var c = 0f;
        var d = 1f;

        if (br.ReadUb(1) != 0)
        {
            var n = br.ReadUb(5);
            a = br.ReadFb(n);
            d = br.ReadFb(n);
        }

        if (br.ReadUb(1) != 0)
        {
            var n = br.ReadUb(5);
            b = br.ReadFb(n);
            c = br.ReadFb(n);
        }

        var tn = br.ReadUb(5);
        var tx = br.ReadSb(tn) / 20f;
        var ty = br.ReadSb(tn) / 20f;
        br.Align();
        return new SwfMatrix { A = a, B = b, C = c, D = d, Tx = tx, Ty = ty };
    }

    public SwfMatrix Multiply(SwfMatrix other) =>
        new()
        {
            A = A * other.A + C * other.B,
            B = B * other.A + D * other.B,
            C = A * other.C + C * other.D,
            D = B * other.C + D * other.D,
            Tx = A * other.Tx + C * other.Ty + Tx,
            Ty = B * other.Tx + D * other.Ty + Ty,
        };

    public PointF Transform(float x, float y)
    {
        return new PointF(
            A * x + C * y + Tx,
            B * x + D * y + Ty);
    }

    public void TransformPoints(Span<PointF> points)
    {
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            points[i] = Transform(p.X, p.Y);
        }
    }
}
