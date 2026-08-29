using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.App.Services;

public static class UiDisplayLabels
{
    public static string LayerTarget(PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => "Suelo",
        PaintLayer.Object1 => "Capa 1 (GFX)",
        _ => "Capa 2 (GFX)",
    };

    public static string GfxSidebarLabel(PaintLayer layer) => layer switch
    {
        PaintLayer.Object1 => "C1",
        PaintLayer.Object2 => "C2",
        _ => "S",
    };

    public static string ActiveLayerStatus(PaintLayer layer) =>
        layer == PaintLayer.Ground
            ? "GFX: Suelo"
            : $"GFX: {LayerTarget(layer)}";

    public static string ResourceType(PaintLayer layer) =>
        layer == PaintLayer.Ground ? "Suelo" : "Objeto";

    public static string CatalogNamespace(PaintLayer layer) =>
        layer == PaintLayer.Ground ? "SUELOS" : "OBJETOS";

    public static string CategoryRoot(PaintLayer layer) =>
        CategoryRoot(layer.ToGfxCategory());

    public static string CategoryRoot(GfxCategory category) =>
        category == GfxCategory.Ground ? "Suelos" : "Objetos";

    public static string CatalogHeader(PaintLayer layer) =>
        $"CATÁLOGO · {CatalogNamespace(layer)}";

    public static string CatalogDestination(PaintLayer layer) =>
        $"Destino: {LayerTarget(layer).ToUpperInvariant()}";
}
