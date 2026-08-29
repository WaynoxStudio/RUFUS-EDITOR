using System.Drawing;

namespace RufusMapEditor.Rendering;

/// <summary>
/// Astria-compatible tile placement (single source of truth for preview, bounds, and <c>Draw_Tile</c>).
/// Port of <c>Cell.Draw_Tile</c> / <c>Cell.SurRound</c>.
/// </summary>
public static class GfxPlacementMath
{
    public readonly struct PlacementRect
    {
        public PlacementRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public PlacementRect ToHitSpace(int cropX, int cropY) =>
            new(X - cropX, Y - cropY, Width, Height);

        public int DeltaX(PlacementRect other) => X - other.X;
        public int DeltaY(PlacementRect other) => Y - other.Y;
    }

    /// <summary>
    /// Canonical draw rectangle in full-canvas map coordinates (pre-export crop).
    /// Matches Astria <c>Cell.Draw_Tile</c> destination exactly.
    /// </summary>
    public static PlacementRect CalculateDrawPlacement(
        IsoGeometry.CellCorners cellFull,
        int imageWidth,
        int imageHeight,
        int anchorX,
        int anchorY,
        bool flip,
        int rotation,
        bool isObject,
        int sizeCell = IsoGeometry.SizeBaseCell)
    {
        var scale = sizeCell / (double)IsoGeometry.SizeBaseCell;
        var (posX, posY, sizeImage) = ComputePlacementOffsets(
            imageWidth, imageHeight, anchorX, anchorY, flip, rotation, isObject, scale);

        // Astria: Location(3)=D, Location(2)=C
        var x = cellFull.D.X + sizeCell - VbInt(posX);
        var y = cellFull.C.Y - (sizeCell / 2) - VbInt(posY);
        return new PlacementRect(x, y, sizeImage.Width, sizeImage.Height);
    }

    /// <summary>Alias kept for bounds/preview callers.</summary>
    public static PlacementRect ComputeBounds(
        IsoGeometry.CellCorners cellFull,
        int imageWidth,
        int imageHeight,
        int anchorX,
        int anchorY,
        bool flip,
        int rotation,
        bool isObject,
        int sizeCell = IsoGeometry.SizeBaseCell) =>
        CalculateDrawPlacement(cellFull, imageWidth, imageHeight, anchorX, anchorY, flip, rotation, isObject, sizeCell);

    /// <summary>
    /// Placement in export-image (cropped) coordinates for editor overlays.
    /// </summary>
    public static bool TryCalculateDrawPlacementInHitSpace(
        int mapWidth,
        int mapHeight,
        int cellId,
        int imageWidth,
        int imageHeight,
        int anchorX,
        int anchorY,
        bool flip,
        int rotation,
        bool isObject,
        out PlacementRect hitRect,
        int sizeCell = IsoGeometry.SizeBaseCell)
    {
        hitRect = default;
        var corners = IsoGeometry.BuildCellCorners(mapWidth, mapHeight, sizeCell);
        if (cellId < 0 || cellId >= corners.Length)
            return false;

        var full = CalculateDrawPlacement(
            corners[cellId], imageWidth, imageHeight, anchorX, anchorY, flip, rotation, isObject, sizeCell);
        var crop = IsoGeometry.ExportCrop(mapWidth, mapHeight, sizeCell);
        hitRect = full.ToHitSpace(crop.X, crop.Y);
        return true;
    }

    public static (int X, int Y) ResolveAnchor(int? anchorX, int? anchorY, int imageWidth, int imageHeight)
    {
        if (anchorX is int ax && anchorY is int ay)
            return (ax, ay);
        return (imageWidth / 2, imageHeight / 2);
    }

    /// <summary>
    /// Applies flip/rotation transforms to a working bitmap copy (Astria <c>Draw_Tile</c> path).
    /// Caller owns the returned bitmap. Logical size matches <see cref="CalculateDrawPlacement"/>.
    /// </summary>
    public static Bitmap TransformBitmap(
        Bitmap source,
        int anchorX,
        int anchorY,
        bool flip,
        int rotation,
        bool isObject,
        int sizeCell,
        out PlacementRect logicalSize)
    {
        var scale = sizeCell / (double)IsoGeometry.SizeBaseCell;
        var (_, _, sizeImage) = ComputePlacementOffsets(
            source.Width, source.Height, anchorX, anchorY, flip, rotation, isObject, scale);

        var aImage = (Bitmap)source.Clone();

        if (flip)
            aImage.RotateFlip(RotateFlipType.RotateNoneFlipX);

        if (rotation != 0)
        {
            switch (rotation)
            {
                case 1:
                    aImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    aImage = ResizeImg(aImage, sizeImage.Height, sizeImage.Width);
                    break;
                case 2:
                    aImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    break;
                case 3:
                    aImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    aImage = ResizeImg(aImage, sizeImage.Height, sizeImage.Width);
                    break;
            }
        }

        logicalSize = new PlacementRect(0, 0, sizeImage.Width, sizeImage.Height);
        return aImage;
    }

    /// <summary>
    /// Shared offset/size math from Astria <c>Draw_Tile</c>.
    /// Rotation cases 1/3 use post-rotate bitmap dimensions (width/height swap after Rotate90/270).
    /// </summary>
    internal static (double PosX, double PosY, Size SizeImage) ComputePlacementOffsets(
        int imageWidth,
        int imageHeight,
        int anchorX,
        int anchorY,
        bool flip,
        int rotation,
        bool isObject,
        double scale)
    {
        var sizeImage = new Size(VbInt(imageWidth * scale), VbInt(imageHeight * scale));
        double posX = anchorX * scale;
        double posY = anchorY * scale;

        if (flip && isObject)
            posX = imageWidth - (anchorX * scale);

        if (rotation != 0)
        {
            switch (rotation)
            {
                case 1:
                {
                    // After Rotate90FlipNone: aImage.Width = original height, aImage.Height = original width
                    var rotatedW = imageHeight;
                    var rotatedH = imageWidth;
                    sizeImage = new Size(
                        (int)Math.Ceiling(rotatedW / 100.0 * 192.86 * scale),
                        (int)Math.Ceiling(rotatedH / 100.0 * 51.85 * scale));
                    posY = (anchorX * scale) / 100.0 * 51.85;
                    posX = sizeImage.Width - ((anchorY * scale) / 100.0 * 192.86);
                    break;
                }
                case 2:
                    if (isObject)
                        posX = sizeImage.Width - (anchorX * scale);
                    posY = sizeImage.Height - (anchorY * scale);
                    break;
                case 3:
                {
                    var rotatedW = imageHeight;
                    var rotatedH = imageWidth;
                    sizeImage = new Size(
                        (int)Math.Ceiling(rotatedW / 100.0 * 192.86 * scale),
                        (int)Math.Ceiling(rotatedH / 100.0 * 51.85 * scale));
                    posY = (anchorX * scale) / 100.0 * 51.85;
                    posX = (anchorY * scale) / 100.0 * 192.86;
                    break;
                }
            }
        }

        return (posX, posY, sizeImage);
    }

    private static Bitmap ResizeImg(Bitmap source, int newWidth, int newHeight)
    {
        var thumb = new Bitmap(newWidth, newHeight);
        using (var gra = Graphics.FromImage(thumb))
        {
            gra.DrawImage(source, new Rectangle(0, 0, newWidth, newHeight),
                new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        }

        source.Dispose();
        return thumb;
    }

    private static int VbInt(double value) => Convert.ToInt32(value);
}
