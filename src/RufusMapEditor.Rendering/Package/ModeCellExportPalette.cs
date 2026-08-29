using System.Drawing;

namespace RufusMapEditor.Rendering.Package;

/// <summary>
/// Fixed technical-export colors for ModeCell PNGs.
/// Independent of UI Light/Dark theme (DynamicResource must not affect package output).
/// </summary>
public static class ModeCellExportPalette
{
    public static readonly Color UnwalkableFill = Color.FromArgb(0x50, 0xE5, 0x39, 0x35);
    public static readonly Color UnwalkableStroke = Color.FromArgb(0xFF, 0xC6, 0x28, 0x28);

    public static readonly Color LosBlockFill = Color.FromArgb(0x55, 0x42, 0xA5, 0xF5);
    public static readonly Color LosBlockStroke = Color.FromArgb(0xFF, 0x1E, 0x88, 0xE5);

    public static readonly Color Fight1Fill = Color.FromArgb(0x99, 0xE5, 0x39, 0x35);
    public static readonly Color Fight1Stroke = Color.FromArgb(0xFF, 0xC6, 0x28, 0x28);

    public static readonly Color Fight2Fill = Color.FromArgb(0xCC, 0x19, 0x76, 0xD2);
    public static readonly Color Fight2Stroke = Color.FromArgb(0xFF, 0x0D, 0x47, 0xA1);

    public static readonly Color FightLabel = Color.White;

    public static readonly Color GridStroke = Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF);
    public static readonly Color CellIdFill = Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF);
    public static readonly Color CellIdShadow = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);

    public static readonly Color ExportLimitStroke = Color.FromArgb(0xFF, 0xD4, 0xA0, 0x17);
}
