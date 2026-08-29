namespace RufusMapEditor.App.Services;

public sealed class UiLayoutSettings
{
    public const double ReferenceWidth = 1400;
    public const double ReferenceHeight = 900;
    public const double DefaultLeftWidth = 160;
    public const double DefaultRightWidth = 280;
    public const double DefaultCatalogHeight = 220;
    public const double DefaultCategoriesWidth = 420;
    public const double DefaultBrushWidth = 155;
    public const double DefaultLogsPanelHeight = 280;
    public const double LogsCollapsedHeight = 24;

    public const double DefaultLeftRatio = DefaultLeftWidth / ReferenceWidth;
    public const double DefaultRightRatio = DefaultRightWidth / ReferenceWidth;
    public const double DefaultCatalogHeightRatio = DefaultCatalogHeight / ReferenceHeight;
    public const double DefaultCategoriesRatio = DefaultCategoriesWidth / ReferenceWidth;
    public const double DefaultBrushRatio = DefaultBrushWidth / ReferenceWidth;

    public double LeftPanelWidth { get; set; } = DefaultLeftWidth;
    public double RightPanelWidth { get; set; } = DefaultRightWidth;
    public double CatalogHeight { get; set; } = DefaultCatalogHeight;
    public bool CatalogCollapsed { get; set; }
    public bool MapsCollapsed { get; set; }

    public double MapWindowLeft { get; set; } = -1;
    public double MapWindowTop { get; set; } = -1;
    public double MapWindowWidth { get; set; } = 720;
    public double MapWindowHeight { get; set; } = 520;
    public bool MapWindowMaximized { get; set; }
    public bool MapWindowMinimized { get; set; }

    public double LeftPanelRatio { get; set; }
    public double RightPanelRatio { get; set; }
    public double CatalogHeightRatio { get; set; }
    public double CategoriesPanelRatio { get; set; }
    public double BrushPanelRatio { get; set; }

    public bool ShowMapsPanel { get; set; } = true;
    public bool ShowInspectorPanel { get; set; } = true;
    public bool ShowCatalogPanel { get; set; } = true;
    public bool ShowCategoriesPanel { get; set; } = true;
    public bool ShowBrushPanel { get; set; } = false;
    public bool ShowToolBar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool LogsExpanded { get; set; }
    public double LogsPanelHeight { get; set; } = DefaultLogsPanelHeight;

    public UiLayoutSettings Clone() => new()
    {
        LeftPanelWidth = LeftPanelWidth,
        RightPanelWidth = RightPanelWidth,
        CatalogHeight = CatalogHeight,
        CatalogCollapsed = CatalogCollapsed,
        MapsCollapsed = MapsCollapsed,
        MapWindowLeft = MapWindowLeft,
        MapWindowTop = MapWindowTop,
        MapWindowWidth = MapWindowWidth,
        MapWindowHeight = MapWindowHeight,
        MapWindowMaximized = MapWindowMaximized,
        MapWindowMinimized = MapWindowMinimized,
        LeftPanelRatio = LeftPanelRatio,
        RightPanelRatio = RightPanelRatio,
        CatalogHeightRatio = CatalogHeightRatio,
        CategoriesPanelRatio = CategoriesPanelRatio,
        BrushPanelRatio = BrushPanelRatio,
        ShowMapsPanel = ShowMapsPanel,
        ShowInspectorPanel = ShowInspectorPanel,
        ShowCatalogPanel = ShowCatalogPanel,
        ShowCategoriesPanel = ShowCategoriesPanel,
        ShowBrushPanel = ShowBrushPanel,
        ShowToolBar = ShowToolBar,
        ShowStatusBar = ShowStatusBar,
        LogsExpanded = LogsExpanded,
        LogsPanelHeight = LogsPanelHeight,
    };

    public void EnsureRatios()
    {
        if (LeftPanelRatio <= 0)
            LeftPanelRatio = LeftPanelWidth / ReferenceWidth;
        if (RightPanelRatio <= 0)
            RightPanelRatio = RightPanelWidth / ReferenceWidth;
        if (CatalogHeightRatio <= 0)
            CatalogHeightRatio = CatalogHeight / ReferenceHeight;
        if (CategoriesPanelRatio <= 0)
            CategoriesPanelRatio = 0.5;
        if (BrushPanelRatio <= 0)
            BrushPanelRatio = DefaultBrushRatio;
    }

    public void ResetToDefaults()
    {
        LeftPanelWidth = DefaultLeftWidth;
        RightPanelWidth = DefaultRightWidth;
        CatalogHeight = DefaultCatalogHeight;
        CatalogCollapsed = false;
        MapsCollapsed = false;
        MapWindowLeft = -1;
        MapWindowTop = -1;
        MapWindowWidth = 720;
        MapWindowHeight = 520;
        MapWindowMaximized = false;
        MapWindowMinimized = false;
        LeftPanelRatio = DefaultLeftRatio;
        RightPanelRatio = DefaultRightRatio;
        CatalogHeightRatio = DefaultCatalogHeightRatio;
        CategoriesPanelRatio = 0.5;
        BrushPanelRatio = DefaultBrushRatio;
        LogsExpanded = false;
        LogsPanelHeight = DefaultLogsPanelHeight;
    }

    public void Clamp()
    {
        EnsureRatios();
        LeftPanelRatio = Math.Clamp(LeftPanelRatio, 0.08, 0.35);
        RightPanelRatio = Math.Clamp(RightPanelRatio, 0.14, 0.45);
        CatalogHeightRatio = Math.Clamp(CatalogHeightRatio, 0.12, 0.55);
        CategoriesPanelRatio = Math.Clamp(CategoriesPanelRatio, 0.2, 0.7);
        BrushPanelRatio = Math.Clamp(BrushPanelRatio, 0.08, 0.30);
        LeftPanelWidth = Math.Clamp(LeftPanelWidth, 120, 480);
        RightPanelWidth = Math.Clamp(RightPanelWidth, 240, 520);
        CatalogHeight = Math.Clamp(CatalogHeight, 120, 480);
        MapWindowWidth = Math.Clamp(MapWindowWidth, 320, 2400);
        MapWindowHeight = Math.Clamp(MapWindowHeight, 200, 1600);
        LogsPanelHeight = Math.Clamp(
            LogsPanelHeight <= 0 ? DefaultLogsPanelHeight : LogsPanelHeight,
            80,
            480);
    }

    public double ResolveLeftPanelWidth(double workspaceWidth, bool visible)
    {
        if (!visible) return 0;
        EnsureRatios();
        var available = Math.Max(0, workspaceWidth - 8);
        return ClampRange(available * LeftPanelRatio, 120, available * 0.35);
    }

    public double ResolveRightPanelWidth(double workspaceWidth, bool visible)
    {
        if (!visible) return 0;
        EnsureRatios();
        var available = Math.Max(0, workspaceWidth - 8);
        return ClampRange(available * RightPanelRatio, 240, available * 0.45);
    }

    public double ResolveCatalogHeight(double rootHeight, bool visible, bool collapsed)
    {
        if (!visible || collapsed) return 0;
        EnsureRatios();
        return ClampRange(rootHeight * CatalogHeightRatio, 120, rootHeight * 0.55);
    }

    public double ResolveCategoriesWidth(double bottomBandWidth, bool visible)
    {
        if (!visible) return 0;
        EnsureRatios();
        var available = Math.Max(0, bottomBandWidth - 4);
        // Hasta ~mitad de la banda inferior (Categorías | Logs)
        return ClampRange(available * CategoriesPanelRatio, 180, Math.Max(180, available * 0.7));
    }

    public double ResolveBrushWidth(double catalogWidth, bool visible)
    {
        if (!visible) return 0;
        EnsureRatios();
        var available = Math.Max(0, catalogWidth - 4);
        return ClampRange(available * BrushPanelRatio, 120, available * 0.30);
    }

    /// <summary>Evita Math.Clamp(min,max) cuando el layout aún no tiene tamaño (max &lt; min).</summary>
    private static double ClampRange(double value, double min, double max)
    {
        if (max < min)
            return Math.Max(0, max);
        return Math.Clamp(value, min, max);
    }

    public void SyncRatiosFromPixels(double workspaceWidth, double rootHeight, double catalogWidth)
    {
        if (workspaceWidth > 0)
        {
            if (LeftPanelWidth > 0)
                LeftPanelRatio = LeftPanelWidth / workspaceWidth;
            if (RightPanelWidth > 0)
                RightPanelRatio = RightPanelWidth / workspaceWidth;
        }

        if (rootHeight > 0 && CatalogHeight > 0)
            CatalogHeightRatio = CatalogHeight / rootHeight;

        if (catalogWidth > 0)
        {
            CategoriesPanelRatio = DefaultCategoriesWidth / catalogWidth;
            BrushPanelRatio = DefaultBrushWidth / catalogWidth;
        }

        Clamp();
    }
}
