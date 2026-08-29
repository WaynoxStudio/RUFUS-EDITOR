namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.SwfSprite;

internal readonly struct SwfColorTransform
{
    public float MulR { get; init; } = 1f;
    public float MulG { get; init; } = 1f;
    public float MulB { get; init; } = 1f;
    public float MulA { get; init; } = 1f;
    public int AddR { get; init; }
    public int AddG { get; init; }
    public int AddB { get; init; }
    public int AddA { get; init; }

    public SwfColorTransform() { }

    public static SwfColorTransform Identity => new();

    public static SwfColorTransform Read(SwfBitReader br, bool withAlpha)
    {
        br.Align();
        var hasAdd = br.ReadUb(1) != 0;
        var hasMul = br.ReadUb(1) != 0;
        var n = br.ReadUb(4);
        var mulR = 1f;
        var mulG = 1f;
        var mulB = 1f;
        var mulA = 1f;
        if (hasMul)
        {
            mulR = br.ReadSb(n) / 256f;
            mulG = br.ReadSb(n) / 256f;
            mulB = br.ReadSb(n) / 256f;
            if (withAlpha)
                mulA = br.ReadSb(n) / 256f;
        }

        var addR = 0;
        var addG = 0;
        var addB = 0;
        var addA = 0;
        if (hasAdd)
        {
            addR = br.ReadSb(n);
            addG = br.ReadSb(n);
            addB = br.ReadSb(n);
            if (withAlpha)
                addA = br.ReadSb(n);
        }

        br.Align();
        return new SwfColorTransform
        {
            MulR = mulR, MulG = mulG, MulB = mulB, MulA = withAlpha ? mulA : 1f,
            AddR = addR, AddG = addG, AddB = addB, AddA = withAlpha ? addA : 0,
        };
    }

    public SwfColorTransform Combine(SwfColorTransform parent)
    {
        return new SwfColorTransform
        {
            MulR = parent.MulR * MulR,
            MulG = parent.MulG * MulG,
            MulB = parent.MulB * MulB,
            MulA = parent.MulA * MulA,
            AddR = parent.AddR + (int)(AddR * parent.MulR),
            AddG = parent.AddG + (int)(AddG * parent.MulG),
            AddB = parent.AddB + (int)(AddB * parent.MulB),
            AddA = parent.AddA + (int)(AddA * parent.MulA),
        };
    }

    public System.Drawing.Color Apply(System.Drawing.Color c)
    {
        static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
        var r = Clamp((int)(c.R * MulR) + AddR);
        var g = Clamp((int)(c.G * MulG) + AddG);
        var b = Clamp((int)(c.B * MulB) + AddB);
        var a = Clamp((int)(c.A * MulA) + AddA);
        return System.Drawing.Color.FromArgb(a, r, g, b);
    }
}
