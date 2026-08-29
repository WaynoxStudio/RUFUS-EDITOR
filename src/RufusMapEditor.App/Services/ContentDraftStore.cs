using System.IO;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.Services;

/// <summary>Persists Content drafts locally (NPC + diálogos). Never touches BD/SFTP.</summary>
public static class ContentDraftStore
{
    private static readonly object Gate = new();
    private static ContentDraftWorkspace? _current;

    public static string StoreDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RufusMapEditor", "content-drafts");

    public static string WorkspacePath => Path.Combine(StoreDirectory, "workspace.json");

    public static ContentDraftWorkspace Current
    {
        get
        {
            lock (Gate)
            {
                _current ??= LoadOrCreate();
                return _current;
            }
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            _current ??= LoadOrCreate();
            Directory.CreateDirectory(StoreDirectory);
            File.WriteAllText(WorkspacePath, ContentWorkspaceSerializer.Serialize(_current));
        }
    }

    public static void Reload()
    {
        lock (Gate)
        {
            _current = LoadOrCreate();
        }
    }

    /// <summary>Test helper — replace in-memory workspace without disk I/O.</summary>
    public static void ReplaceForTests(ContentDraftWorkspace workspace)
    {
        lock (Gate)
        {
            _current = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }
    }

    private static ContentDraftWorkspace LoadOrCreate()
    {
        try
        {
            if (File.Exists(WorkspacePath))
            {
                var json = File.ReadAllText(WorkspacePath);
                if (!string.IsNullOrWhiteSpace(json))
                    return ContentWorkspaceSerializer.Deserialize(json);
            }
        }
        catch
        {
            // Corrupt file → fresh workspace
        }
        return new ContentDraftWorkspace();
    }
}
