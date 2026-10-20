using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace RufusMapEditor.LegacyCompatibility.VisualLibrary.NpcGfxPreviewPrep;

/// <summary>
/// ADMIN.UI.4B.2A.3G.2C — expand FFDec frame SVG viewBox to cover root-placed content
/// that sits outside the Stage (negative X/Y, past StageWidth/Height).
/// Does not invent geometry: AABB from root <c>&lt;use&gt;</c> width/height × matrix only.
/// </summary>
public static class SvgFrameBoundsExpander
{
    private static readonly Regex MatrixRx = new(
        @"matrix\s*\(\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public readonly struct Bounds
    {
        public Bounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public bool IsValid => Width > 0.5 && Height > 0.5 && !double.IsInfinity(MinX);
    }

    public sealed class ExpandResult
    {
        public required bool Ok { get; init; }
        public required string SvgText { get; init; }
        public Bounds ContentBounds { get; init; }
        public string? ViewBox { get; init; }
        public string? Reason { get; init; }
        public int RootUseCount { get; init; }
    }

    /// <summary>Compute AABB of transformed root <c>use</c> boxes (excludes <c>defs</c>).</summary>
    public static Bounds? TryComputeRootUseBounds(string svgText)
    {
        if (string.IsNullOrWhiteSpace(svgText))
            return null;

        var doc = new XmlDocument { XmlResolver = null };
        try
        {
            doc.LoadXml(svgText);
        }
        catch
        {
            return null;
        }

        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("svg", "http://www.w3.org/2000/svg");

        var rootG = doc.SelectSingleNode("/svg:svg/svg:g[1]", nsm)
                    ?? doc.SelectSingleNode("/*[local-name()='svg']/*[local-name()='g'][1]");
        if (rootG is null)
            return null;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        var any = false;

        foreach (XmlNode node in rootG.ChildNodes)
        {
            if (node is not XmlElement el)
                continue;
            if (!string.Equals(el.LocalName, "use", StringComparison.Ordinal))
                continue;
            if (!TryAccumulateUseBox(el, ref minX, ref minY, ref maxX, ref maxY))
                continue;
            any = true;
        }

        if (!any)
            return null;

        var b = new Bounds(minX, minY, maxX, maxY);
        return b.IsValid ? b : null;
    }

    /// <summary>
    /// Rewrite width/height/viewBox so the canvas covers content AABB (with small pad).
    /// Preserves existing transforms on elements.
    /// </summary>
    public static ExpandResult Expand(string svgText, double padFraction = 0.03)
    {
        if (string.IsNullOrWhiteSpace(svgText))
        {
            return new ExpandResult
            {
                Ok = false,
                SvgText = svgText ?? "",
                Reason = "SVG vacío",
            };
        }

        var bounds = TryComputeRootUseBounds(svgText);
        if (bounds is null)
        {
            return new ExpandResult
            {
                Ok = false,
                SvgText = svgText,
                Reason = "Sin AABB de root use",
            };
        }

        var b = bounds.Value;
        var padX = b.Width * Math.Clamp(padFraction, 0, 0.25);
        var padY = b.Height * Math.Clamp(padFraction, 0, 0.25);
        var vx = b.MinX - padX;
        var vy = b.MinY - padY;
        var vw = b.Width + 2 * padX;
        var vh = b.Height + 2 * padY;

        var doc = new XmlDocument { XmlResolver = null };
        doc.LoadXml(svgText);
        var svg = doc.DocumentElement;
        if (svg is null || !string.Equals(svg.LocalName, "svg", StringComparison.Ordinal))
        {
            return new ExpandResult
            {
                Ok = false,
                SvgText = svgText,
                ContentBounds = b,
                Reason = "Raíz SVG inválida",
            };
        }

        var inv = CultureInfo.InvariantCulture;
        var viewBox = string.Create(inv, $"{vx:0.####} {vy:0.####} {vw:0.####} {vh:0.####}");
        svg.SetAttribute("viewBox", viewBox);
        svg.SetAttribute("width", string.Create(inv, $"{vw:0.##}px"));
        svg.SetAttribute("height", string.Create(inv, $"{vh:0.##}px"));

        // Count root uses for diagnostics
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("svg", "http://www.w3.org/2000/svg");
        var rootG = doc.SelectSingleNode("/svg:svg/svg:g[1]", nsm)
                    ?? doc.SelectSingleNode("/*[local-name()='svg']/*[local-name()='g'][1]");
        var useCount = 0;
        if (rootG is not null)
        {
            foreach (XmlNode n in rootG.ChildNodes)
            {
                if (n is XmlElement e && string.Equals(e.LocalName, "use", StringComparison.Ordinal))
                    useCount++;
            }
        }

        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };
        using (var writer = XmlWriter.Create(ms, settings))
            doc.Save(writer);

        var svgOut = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        // XmlWriter may emit UTF-8 BOM via preamble in some hosts — strip if present in text form.
        if (svgOut.Length > 0 && svgOut[0] == '\uFEFF')
            svgOut = svgOut[1..];

        return new ExpandResult
        {
            Ok = true,
            SvgText = svgOut,
            ContentBounds = new Bounds(vx, vy, vx + vw, vy + vh),
            ViewBox = viewBox,
            RootUseCount = useCount,
            Reason = string.Create(inv, $"viewBox expandido {viewBox} · uses={useCount}"),
        };
    }

    public static ExpandResult ExpandFile(string svgPath, string? outputPath = null, double padFraction = 0.03)
    {
        var text = File.ReadAllText(svgPath);
        var result = Expand(text, padFraction);
        if (result.Ok)
        {
            var dest = outputPath ?? svgPath;
            File.WriteAllText(dest, result.SvgText, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return result;
    }

    private static bool TryAccumulateUseBox(
        XmlElement use,
        ref double minX,
        ref double minY,
        ref double maxX,
        ref double maxY)
    {
        if (!TryParseDouble(use.GetAttribute("width"), out var w) || w <= 0)
            return false;
        if (!TryParseDouble(use.GetAttribute("height"), out var h) || h <= 0)
            return false;

        var transform = use.GetAttribute("transform");
        double a = 1, b = 0, c = 0, d = 1, e = 0, f = 0;
        if (!string.IsNullOrWhiteSpace(transform))
        {
            var m = MatrixRx.Match(transform);
            if (!m.Success)
                return false;
            a = ParseInv(m.Groups[1].Value);
            b = ParseInv(m.Groups[2].Value);
            c = ParseInv(m.Groups[3].Value);
            d = ParseInv(m.Groups[4].Value);
            e = ParseInv(m.Groups[5].Value);
            f = ParseInv(m.Groups[6].Value);
        }

        Span<(double x, double y)> corners =
        [
            (0, 0),
            (w, 0),
            (0, h),
            (w, h),
        ];

        foreach (var (px, py) in corners)
        {
            var x = a * px + c * py + e;
            var y = b * px + d * py + f;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        return true;
    }

    private static bool TryParseDouble(string? s, out double v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        return double.TryParse(
            s.Trim().Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out v);
    }

    private static double ParseInv(string s) =>
        double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
