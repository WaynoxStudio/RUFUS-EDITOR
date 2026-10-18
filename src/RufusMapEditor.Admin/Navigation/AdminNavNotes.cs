namespace RufusMapEditor.Admin.Navigation;

public enum AdminSection
{
    Maps,
    Content,
    Missions,
    Settings,
}

/// <summary>
/// Mapas: hospeda MapsEditorView (módulo real compartido con USER).
/// Contenido: hospeda ContentWorkspaceView (NPC/diálogos/IA compartido).
/// </summary>
public static class AdminNavNotes
{
    public const string MissionsPlaceholder =
        "Próximamente.\n\nADMIN.UI.4 integrará el módulo real de Misiones.";
}
