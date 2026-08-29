namespace RufusMapEditor.Admin.Navigation;

public enum AdminSection
{
    Maps,
    Content,
    Missions,
    Licenses,
    AiUsage,
    Settings,
}

/// <summary>
/// ADMIN.UI.2 — Mapas: hospeda MapsEditorView (módulo real compartido con USER).
/// ADMIN.UI.3 — Contenido: hospeda ContentWorkspaceView (NPC/diálogos/IA compartido).
/// ADMIN.USAGE.1 — Uso IA: métricas de rufus_ai_usage_events (solo lectura).
/// </summary>
public static class AdminNavNotes
{
    public const string MissionsPlaceholder =
        "Próximamente.\n\nADMIN.UI.4 integrará el módulo real de Misiones.";
}
