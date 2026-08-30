using System.IO;
using RufusMapEditor.App.ViewModels;
using RufusMapEditor.Domain.Maps;
using RufusMapEditor.Domain.World;
using RufusMapEditor.LegacyCompatibility.Editing;
using RufusMapEditor.LegacyCompatibility.MapData;
using RufusMapEditor.LegacyCompatibility.World;
using RufusMapEditor.Rendering;

namespace RufusMapEditor.App.Services;

/// <summary>
/// Coordinates cross-map editing: stroke batching, composite undo, selection by (DocumentKey, CellId).
/// Reuses MapCellEditor and CellBatchEditCommand — no second paint engine.
/// </summary>
public sealed class MultiMapEditService
{
    private readonly AstriaLibraryService _library;
    private readonly WorldThumbnailCache _thumbs;
    private readonly Dictionary<string, IsoHitTester> _hitTesters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, CellSnapshot>> _strokeBefore = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyDocumentKeys = new(StringComparer.Ordinal);
    private readonly HashSet<WorldCellRef> _selection = new();
    private readonly WorldEditHistory _history = new();

    private WorldDocument? _world;
    private HashSet<string> _editableKeys = new(StringComparer.Ordinal);
    private bool _strokeOpen;
    private string _strokeName = "";
    private string _lastStrokeKey = "";
    private double? _lastStrokeWorldX;
    private double? _lastStrokeWorldY;
    private WorldCellHit? _hovered;
    private bool _rectSelecting;
    private double _rectX0;
    private double _rectY0;
    private double _rectX1;
    private double _rectY1;
    private WorldCellClipboard? _clipboard;

    public MultiMapEditService(AstriaLibraryService library, WorldThumbnailCache thumbs)
    {
        _library = library;
        _thumbs = thumbs;
    }

    public event Action? StateChanged;
    public event Action<string>? ThumbnailInvalidateRequested;

    public bool IsActive => _world is not null;
    public WorldDocument? World => _world;
    public IReadOnlySet<string> EditableKeys => _editableKeys;
    public IReadOnlySet<WorldCellRef> Selection => _selection;
    public WorldCellHit? HoveredCell => _hovered;
    public WorldEditHistory History => _history;
    public bool IsRectSelecting => _rectSelecting;
    public (double X0, double Y0, double X1, double Y1) RectSelectBounds => (_rectX0, _rectY0, _rectX1, _rectY1);
    public int ModifiedMapCount => _dirtyDocumentKeys.Count;
    public IReadOnlyCollection<string> DirtyDocumentKeys => _dirtyDocumentKeys;
    public bool HasClipboard => _clipboard is { Entries.Count: > 0 };

    public bool Enter(WorldDocument world, IReadOnlySet<string> selectedKeys)
    {
        if (selectedKeys.Count == 0) return false;
        Exit();
        _world = world;
        _editableKeys = new HashSet<string>(selectedKeys, StringComparer.Ordinal);
        _history.Clear();
        _dirtyDocumentKeys.Clear();
        _selection.Clear();
        _hitTesters.Clear();
        foreach (var key in _editableKeys)
        {
            if (world.Documents.TryGetValue(key, out var entry))
                _hitTesters[key] = WorldMapHitTest.CreateHitTester(entry.Document);
        }

        NotifyState();
        return true;
    }

    /// <summary>Allows painting on newly added mosaic maps without resetting undo history.</summary>
    public void EnsureEditable(string documentKey)
    {
        if (_world is null || string.IsNullOrEmpty(documentKey)) return;
        if (!_editableKeys.Add(documentKey)) return;
        if (_world.Documents.TryGetValue(documentKey, out var entry))
            _hitTesters[documentKey] = WorldMapHitTest.CreateHitTester(entry.Document);
        NotifyState();
    }

    public bool Exit(bool confirmDiscard = true)
    {
        if (_world is null) return true;
        EndStrokeInternal();
        if (confirmDiscard && _history.IsDirty)
        {
            var result = System.Windows.MessageBox.Show(
                $"{ModifiedMapCount} mapa(s) con cambios sin guardar.\n\n¿Guardar antes de salir?",
                "Edición multimap",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);
            if (result == System.Windows.MessageBoxResult.Cancel)
                return false;
            if (result == System.Windows.MessageBoxResult.Yes && SaveModifiedMaps() == 0)
                return false;
        }

        _world = null;
        _editableKeys.Clear();
        _selection.Clear();
        _hovered = null;
        _hitTesters.Clear();
        _history.Clear();
        _dirtyDocumentKeys.Clear();
        _strokeBefore.Clear();
        _strokeOpen = false;
        NotifyState();
        return true;
    }

    public IEnumerable<(int WorldX, int WorldY, string DocumentKey, MapDocument Map)> EnumerateEditablePlacements()
    {
        if (_world is null) yield break;
        foreach (var p in _world.Placements)
        {
            if (!_editableKeys.Contains(p.DocumentKey)) continue;
            if (_world.Documents.TryGetValue(p.DocumentKey, out var entry))
                yield return (p.WorldX, p.WorldY, p.DocumentKey, entry.Document);
        }
    }

    public IsoHitTester? GetHitTester(string documentKey) =>
        _hitTesters.GetValueOrDefault(documentKey);

    public MapDocument? GetDocument(string documentKey) =>
        _world?.Documents.GetValueOrDefault(documentKey)?.Document;

    public WorldCellHit? HitTest(double worldPixelX, double worldPixelY, bool mosaicMode) =>
        WorldMapHitTest.HitTestCell(
            worldPixelX,
            worldPixelY,
            EnumerateEditablePlacements(),
            mosaicMode,
            _editableKeys);

    public void UpdateHover(double worldPixelX, double worldPixelY, bool mosaicMode)
    {
        _hovered = HitTest(worldPixelX, worldPixelY, mosaicMode);
        NotifyState();
    }

    public void ClearHover()
    {
        _hovered = null;
        NotifyState();
    }

    public void BeginStroke(EditorTool tool, PaintLayer layer)
    {
        EndStrokeInternal();
        if (tool is not (EditorTool.Paint or EditorTool.Erase)) return;
        _strokeOpen = true;
        _strokeName = tool == EditorTool.Paint ? PaintStrokeName(layer) : EraseStrokeName(layer);
        _strokeBefore.Clear();
        _lastStrokeKey = "";
        _lastStrokeWorldX = null;
        _lastStrokeWorldY = null;
    }

    public void ResetStrokePointer()
    {
        _lastStrokeWorldX = null;
        _lastStrokeWorldY = null;
    }

    public void ContinueStroke(
        double worldX,
        double worldY,
        EditorTool tool,
        PaintLayer paintLayer,
        int? selectedGfxId,
        bool brushFlip,
        int brushRotation,
        bool mosaicMode,
        bool eraseOnlySelectedGfx = false,
        bool paintMarksUnwalkable = false,
        bool paintSeam = false)
    {
        if (!_strokeOpen || _world is null) return;
        if (tool is not EditorTool.Paint and not EditorTool.Erase) return;

        if (_lastStrokeWorldX is null || _lastStrokeWorldY is null)
        {
            var hit = WorldMapHitTest.HitTestCell(
                worldX, worldY, EnumerateEditablePlacements(), mosaicMode, _editableKeys);
            if (hit is WorldCellHit h)
                HandleCellClick(
                    new WorldCellRef(h.DocumentKey, h.CellId),
                    tool, paintLayer, selectedGfxId, brushFlip, brushRotation,
                    isDrag: true, ctrl: false, eraseOnlySelectedGfx: eraseOnlySelectedGfx,
                    paintMarksUnwalkable: paintMarksUnwalkable,
                    paintSeam: paintSeam);
        }
        else
        {
            foreach (var h in WorldMapHitTest.CellsAlongSegment(
                         _lastStrokeWorldX.Value, _lastStrokeWorldY.Value, worldX, worldY,
                         EnumerateEditablePlacements(), mosaicMode, _editableKeys))
            {
                HandleCellClick(
                    new WorldCellRef(h.DocumentKey, h.CellId),
                    tool, paintLayer, selectedGfxId, brushFlip, brushRotation,
                    isDrag: true, ctrl: false, eraseOnlySelectedGfx: eraseOnlySelectedGfx,
                    paintMarksUnwalkable: paintMarksUnwalkable,
                    paintSeam: paintSeam);
            }
        }

        _lastStrokeWorldX = worldX;
        _lastStrokeWorldY = worldY;
    }

    public void FinishStroke()
    {
        if (!EndStrokeInternal()) return;
        MarkAffectedDirty();
        NotifyState();
    }

    public void HandleCellClick(
        WorldCellRef cell,
        EditorTool tool,
        PaintLayer paintLayer,
        int? selectedGfxId,
        bool brushFlip,
        int brushRotation,
        bool isDrag,
        bool ctrl,
        Action<int>? onGfxPicked = null,
        bool eraseOnlySelectedGfx = false,
        bool paintMarksUnwalkable = false,
        bool paintSeam = false)
    {
        if (_world is null || !_editableKeys.Contains(cell.DocumentKey)) return;
        if (GetDocument(cell.DocumentKey) is not MapDocument doc || cell.CellId < 0 || cell.CellId >= doc.Cells.Count) return;

        switch (tool)
        {
            case EditorTool.Select:
                if (isDrag) return;
                if (ctrl)
                {
                    if (!_selection.Remove(cell))
                        _selection.Add(cell);
                }
                else
                {
                    _selection.Clear();
                    _selection.Add(cell);
                }
                break;

            case EditorTool.Paint:
                if (selectedGfxId is not int gfxId) return;
                if (isDrag && cell.StrokeKey == _lastStrokeKey) return;
                var layer = paintLayer.ToEditorLayer();
                var rot = paintLayer == PaintLayer.Object2 ? (int?)null : brushRotation;
                var markBlocked = paintMarksUnwalkable;
                StrokeMutate(cell, c =>
                {
                    MapCellEditor.SetLayerGfx(c, layer, gfxId, brushFlip, rot);
                    if (markBlocked)
                        MapCellEditor.SetMovement(c, MovementType.Unwalkable);
                });
                _lastStrokeKey = cell.StrokeKey;
                if (!isDrag)
                {
                    _selection.Clear();
                    _selection.Add(cell);
                }
                ThumbnailInvalidateRequested?.Invoke(cell.DocumentKey);

                if (paintSeam && _library.Catalog is not null)
                {
                    var replicas = SeamPaintHelper.FindReplicaCells(
                        _world, cell.DocumentKey, cell.CellId, gfxId, paintLayer,
                        brushFlip, brushRotation, _library.Catalog, mosaicMode: true);
                    foreach (var replica in replicas)
                    {
                        if (!_editableKeys.Contains(replica.DocumentKey))
                            EnsureEditable(replica.DocumentKey);
                        if (!_editableKeys.Contains(replica.DocumentKey))
                            continue;
                        StrokeMutate(replica, c =>
                        {
                            MapCellEditor.SetLayerGfx(c, layer, gfxId, brushFlip, rot);
                            if (markBlocked)
                                MapCellEditor.SetMovement(c, MovementType.Unwalkable);
                        });
                        ThumbnailInvalidateRequested?.Invoke(replica.DocumentKey);
                    }
                }

                break;

            case EditorTool.Erase:
                if (isDrag && cell.StrokeKey == _lastStrokeKey) return;
                if (eraseOnlySelectedGfx)
                {
                    if (selectedGfxId is not int matchId) return;
                    if (GetLayerGfx(doc.Cells[cell.CellId], paintLayer) != matchId) return;
                }

                var eraseLayer = paintLayer.ToEditorLayer();
                StrokeMutate(cell, c => MapCellEditor.ClearLayer(c, eraseLayer));
                _lastStrokeKey = cell.StrokeKey;
                if (!isDrag)
                {
                    _selection.Clear();
                    _selection.Add(cell);
                }
                ThumbnailInvalidateRequested?.Invoke(cell.DocumentKey);
                break;

            case EditorTool.Eyedropper:
                if (isDrag) return;
                ApplyEyedropper(cell, paintLayer, onGfxPicked);
                _selection.Clear();
                _selection.Add(cell);
                break;
        }

        NotifyState();
    }

    public void BeginRectSelect(double worldX, double worldY)
    {
        _rectSelecting = true;
        _rectX0 = _rectX1 = worldX;
        _rectY0 = _rectY1 = worldY;
        NotifyState();
    }

    public void UpdateRectSelect(double worldX, double worldY, bool mosaicMode)
    {
        if (!_rectSelecting || _world is null) return;
        _rectX1 = worldX;
        _rectY1 = worldY;
        _selection.Clear();
        foreach (var r in WorldMapHitTest.CellsInWorldRect(
                     _rectX0, _rectY0, _rectX1, _rectY1,
                     EnumerateEditablePlacements(),
                     mosaicMode,
                     _editableKeys))
            _selection.Add(r);
        NotifyState();
    }

    public void EndRectSelect(double worldX, double worldY, bool mosaicMode)
    {
        if (!_rectSelecting) return;
        UpdateRectSelect(worldX, worldY, mosaicMode);
        _rectSelecting = false;
        NotifyState();
    }

    public void ClearSelection()
    {
        _selection.Clear();
        NotifyState();
    }

    public void SetSelection(IEnumerable<WorldCellRef> cells)
    {
        _selection.Clear();
        foreach (var cell in cells)
            _selection.Add(cell);
        NotifyState();
    }

    public void DeleteSelection(PaintLayer layer)
    {
        if (_world is null || _selection.Count == 0) return;
        BeginStroke(EditorTool.Erase, layer);
        var eraseLayer = layer.ToEditorLayer();
        foreach (var cell in _selection.ToList())
            StrokeMutate(cell, c => MapCellEditor.ClearLayer(c, eraseLayer));
        FinishStroke();
    }

    public int CountReplace(int findId, PaintLayer layer)
    {
        if (_world is null || _selection.Count == 0) return 0;
        var count = 0;
        foreach (var cell in _selection)
        {
            if (GetDocument(cell.DocumentKey) is not MapDocument doc || cell.CellId < 0 || cell.CellId >= doc.Cells.Count) continue;
            var c = doc.Cells[cell.CellId];
            if (GetLayerGfx(c, layer) == findId) count++;
        }
        return count;
    }

    public int ApplyReplace(int findId, int replaceId, PaintLayer layer)
    {
        if (_world is null || _selection.Count == 0 || findId == replaceId) return 0;
        BeginStroke(EditorTool.Paint, layer);
        var editLayer = layer.ToEditorLayer();
        var changed = 0;
        foreach (var cell in _selection.ToList())
        {
            StrokeMutate(cell, c =>
            {
                if (GetLayerGfx(c, layer) == findId)
                {
                    MapCellEditor.SetLayerGfx(c, editLayer, replaceId);
                    changed++;
                }
            });
            ThumbnailInvalidateRequested?.Invoke(cell.DocumentKey);
        }
        FinishStroke();
        return changed;
    }

    public int CountReplaceInEditableMaps(int findId, PaintLayer layer)
    {
        if (_world is null) return 0;
        var count = 0;
        foreach (var key in _editableKeys)
        {
            var doc = GetDocument(key);
            if (doc is null) continue;
            foreach (var cell in doc.Cells)
            {
                if (GetLayerGfx(cell, layer) == findId) count++;
            }
        }
        return count;
    }

    public int ApplyReplaceInEditableMaps(int findId, int replaceId, PaintLayer layer)
    {
        if (_world is null || findId == replaceId) return 0;
        BeginStroke(EditorTool.Paint, layer);
        var editLayer = layer.ToEditorLayer();
        var changed = 0;
        foreach (var key in _editableKeys)
        {
            var doc = GetDocument(key);
            if (doc is null) continue;
            for (var id = 0; id < doc.Cells.Count; id++)
            {
                var cellRef = new WorldCellRef(key, id);
                StrokeMutate(cellRef, c =>
                {
                    if (GetLayerGfx(c, layer) == findId)
                    {
                        MapCellEditor.SetLayerGfx(c, editLayer, replaceId);
                        changed++;
                    }
                });
            }
            if (changed > 0)
                ThumbnailInvalidateRequested?.Invoke(key);
        }
        FinishStroke();
        return changed;
    }

    public int CountMatchingGfx(IReadOnlyCollection<string> documentKeys, PaintLayer layer, int gfxId)
    {
        if (_world is null || documentKeys.Count == 0 || gfxId <= 0) return 0;
        var count = 0;
        foreach (var key in documentKeys)
        {
            var doc = GetDocument(key);
            if (doc is null) continue;
            foreach (var cell in doc.Cells)
            {
                if (GetLayerGfx(cell, layer) == gfxId)
                    count++;
            }
        }

        return count;
    }

    public List<WorldCellRef> FindMatchingCells(IReadOnlyCollection<string> documentKeys, PaintLayer layer, int gfxId)
    {
        var result = new List<WorldCellRef>();
        if (_world is null || documentKeys.Count == 0 || gfxId <= 0) return result;
        foreach (var key in documentKeys)
        {
            var doc = GetDocument(key);
            if (doc is null) continue;
            for (var id = 0; id < doc.Cells.Count; id++)
            {
                if (GetLayerGfx(doc.Cells[id], layer) == gfxId)
                    result.Add(new WorldCellRef(key, id));
            }
        }

        return result;
    }

    /// <summary>Batch-mutate every cell with <paramref name="gfxId"/> on <paramref name="layer"/> across maps.</summary>
    public int MutateMatchingGfx(
        IReadOnlyCollection<string> documentKeys,
        PaintLayer layer,
        int gfxId,
        string commandName,
        Action<CellData> mutate)
    {
        if (_world is null || documentKeys.Count == 0 || gfxId <= 0) return 0;
        BeginStroke(EditorTool.Paint, layer);
        _strokeName = commandName;
        var changed = 0;
        foreach (var key in documentKeys)
        {
            var doc = GetDocument(key);
            if (doc is null) continue;
            var touched = false;
            for (var id = 0; id < doc.Cells.Count; id++)
            {
                if (GetLayerGfx(doc.Cells[id], layer) != gfxId) continue;
                StrokeMutate(new WorldCellRef(key, id), c =>
                {
                    mutate(c);
                    changed++;
                });
                touched = true;
            }

            if (touched)
                ThumbnailInvalidateRequested?.Invoke(key);
        }

        FinishStroke();
        return changed;
    }

    public void CopySelection()
    {
        if (_world is null || _selection.Count == 0) return;
        var anchor = _selection.First();
        if (GetDocument(anchor.DocumentKey) is not MapDocument anchorDoc ||
            GetHitTester(anchor.DocumentKey) is not IsoHitTester anchorTester ||
            !anchorTester.TryGetCellCornersInHitSpace(anchor.CellId, out var anchorCorners))
            return;

        var placement = _world.Placements.FirstOrDefault(p => p.DocumentKey == anchor.DocumentKey);
        if (placement is null) return;
        var (rx, ry, _, _) = WorldGeometry.GetMapRect(
            placement.WorldX, placement.WorldY,
            anchorDoc,
            mosaicMode: true);
        var anchorWx = rx + (anchorCorners.A.X + anchorCorners.C.X) / 2.0;
        var anchorWy = ry + (anchorCorners.B.Y + anchorCorners.D.Y) / 2.0;

        var entries = new List<WorldCellClipboardEntry>();
        foreach (var cell in _selection)
        {
            if (GetHitTester(cell.DocumentKey) is not IsoHitTester tester ||
                !tester.TryGetCellCornersInHitSpace(cell.CellId, out var c))
                continue;
            var p = _world.Placements.FirstOrDefault(x => x.DocumentKey == cell.DocumentKey);
            if (p is null) continue;
            var (crx, cry, _, _) = WorldGeometry.GetMapRect(
                p.WorldX, p.WorldY, GetDocument(cell.DocumentKey)!, mosaicMode: true);
            var wx = crx + (c.A.X + c.C.X) / 2.0;
            var wy = cry + (c.B.Y + c.D.Y) / 2.0;
            entries.Add(new WorldCellClipboardEntry(
                CellSnapshot.Capture(cell.CellId, GetDocument(cell.DocumentKey)!.Cells[cell.CellId]),
                wx - anchorWx,
                wy - anchorWy));
        }

        _clipboard = new WorldCellClipboard(entries);
    }

    public (int Pasted, int Skipped) PasteAt(WorldCellHit dest, bool mosaicMode)
    {
        if (_world is null || _clipboard is null || _clipboard.Entries.Count == 0)
            return (0, 0);

        var placement = _world.Placements.FirstOrDefault(p => p.DocumentKey == dest.DocumentKey);
        if (placement is null) return (0, _clipboard.Entries.Count);
        if (GetDocument(dest.DocumentKey) is not MapDocument destDoc ||
            GetHitTester(dest.DocumentKey) is not IsoHitTester destTester ||
            !destTester.TryGetCellCornersInHitSpace(dest.CellId, out var destC))
            return (0, _clipboard.Entries.Count);

        var (drx, dry, _, _) = WorldGeometry.GetMapRect(
            placement.WorldX, placement.WorldY, destDoc, mosaicMode);
        var anchorWx = drx + (destC.A.X + destC.C.X) / 2.0;
        var anchorWy = dry + (destC.B.Y + destC.D.Y) / 2.0;

        BeginStroke(EditorTool.Paint, PaintLayer.Ground);
        var pasted = 0;
        var skipped = 0;
        var newSelection = new List<WorldCellRef>();

        foreach (var entry in _clipboard.Entries)
        {
            var targetWx = anchorWx + entry.OffsetWorldX;
            var targetWy = anchorWy + entry.OffsetWorldY;
            var hit = WorldMapHitTest.HitTestCell(
                targetWx, targetWy,
                EnumerateEditablePlacements(),
                mosaicMode,
                _editableKeys);
            if (hit is not WorldCellHit worldHit)
            {
                skipped++;
                continue;
            }

            StrokeMutate(new WorldCellRef(worldHit.DocumentKey, worldHit.CellId),
                c => entry.Snapshot.ApplyTo(c));
            newSelection.Add(new WorldCellRef(worldHit.DocumentKey, worldHit.CellId));
            pasted++;
            ThumbnailInvalidateRequested?.Invoke(worldHit.DocumentKey);
        }

        FinishStroke();
        _selection.Clear();
        foreach (var s in newSelection) _selection.Add(s);
        return (pasted, skipped);
    }

    public bool Undo()
    {
        if (_world is null) return false;
        EndStrokeInternal();
        if (!_history.Undo(_world)) return false;
        RefreshDirtyFromHistory();
        InvalidateAllEditableThumbnails();
        NotifyState();
        return true;
    }

    public bool Redo()
    {
        if (_world is null) return false;
        if (!_history.Redo(_world)) return false;
        MarkAffectedDirty();
        InvalidateAllEditableThumbnails();
        NotifyState();
        return true;
    }

    public int SaveModifiedMaps()
    {
        if (_world is null) return 0;
        EndStrokeInternal();
        var saved = 0;
        foreach (var key in _dirtyDocumentKeys.ToList())
        {
            if (!_world.Documents.TryGetValue(key, out var entry)) continue;
            var path = entry.LinkedRufmapPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RufusMapEditor",
                    "world-maps",
                    $"{entry.Document.Id}_{key}.rufmap");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.LinkedRufmapPath = path;
            }

            try
            {
                ProjectPersistence.SaveDocument(entry.Document, key, path, entry.Origin);
                saved++;
            }
            catch
            {
                /* skip failed saves */
            }
        }

        if (saved > 0)
        {
            _history.MarkClean();
            _dirtyDocumentKeys.Clear();
            _world.IsDirty = true;
        }

        NotifyState();
        return saved;
    }

    private void StrokeMutate(WorldCellRef cell, Action<CellData> mutate)
    {
        if (!_strokeOpen || _world is null) return;
        if (!_world.Documents.TryGetValue(cell.DocumentKey, out var entry)) return;
        if (cell.CellId < 0 || cell.CellId >= entry.Document.Cells.Count) return;

        if (!_strokeBefore.TryGetValue(cell.DocumentKey, out var mapBefore))
        {
            mapBefore = new Dictionary<int, CellSnapshot>();
            _strokeBefore[cell.DocumentKey] = mapBefore;
        }

        if (!mapBefore.ContainsKey(cell.CellId))
            mapBefore[cell.CellId] = CellSnapshot.Capture(cell.CellId, entry.Document.Cells[cell.CellId]);

        mutate(entry.Document.Cells[cell.CellId]);
    }

    private bool EndStrokeInternal()
    {
        if (!_strokeOpen)
            return false;
        _strokeOpen = false;
        _lastStrokeKey = "";
        _lastStrokeWorldX = null;
        _lastStrokeWorldY = null;

        if (_strokeBefore.Count == 0 || _world is null)
        {
            _strokeBefore.Clear();
            return false;
        }

        var parts = new List<(string, CellBatchEditCommand)>();
        foreach (var (docKey, beforeMap) in _strokeBefore)
        {
            if (!_world.Documents.TryGetValue(docKey, out var entry)) continue;
            var changes = new List<(CellSnapshot Before, CellSnapshot After)>();
            foreach (var (id, before) in beforeMap)
            {
                var after = CellSnapshot.Capture(id, entry.Document.Cells[id]);
                if (!before.ContentEquals(after))
                    changes.Add((before, after));
                else
                    before.ApplyTo(entry.Document.Cells[id]);
            }

            MapCellEditor.SyncMapDataString(entry.Document);
            if (changes.Count > 0)
                parts.Add((docKey, new CellBatchEditCommand(_strokeName, changes)));
        }

        _strokeBefore.Clear();
        if (parts.Count == 0) return false;

        var composite = new CompositeMapEditCommand(_strokeName, parts);

        _history.PushExecuted(composite);
        foreach (var (key, _) in parts)
            _dirtyDocumentKeys.Add(key);
        return true;
    }

    private void ApplyEyedropper(WorldCellRef cell, PaintLayer layer, Action<int>? onGfxPicked)
    {
        var doc = GetDocument(cell.DocumentKey);
        if (doc is null) return;
        var c = doc.Cells[cell.CellId];
        var gfx = GetLayerGfx(c, layer);
        onGfxPicked?.Invoke(gfx);
    }

    private void MarkAffectedDirty()
    {
        if (_world is null) return;
        foreach (var key in _editableKeys)
            _dirtyDocumentKeys.Add(key);
    }

    private void RefreshDirtyFromHistory()
    {
        _dirtyDocumentKeys.Clear();
        if (_history.IsDirty)
        {
            foreach (var key in _editableKeys)
                _dirtyDocumentKeys.Add(key);
        }
    }

    private void InvalidateAllEditableThumbnails()
    {
        foreach (var key in _editableKeys)
            ThumbnailInvalidateRequested?.Invoke(key);
    }

    private void NotifyState() => StateChanged?.Invoke();

    private static int GetLayerGfx(CellData cell, PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => cell.GroundGfxId,
        PaintLayer.Object1 => cell.Object1GfxId,
        _ => cell.Object2GfxId,
    };

    private static string PaintStrokeName(PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => "Pintar Ground",
        PaintLayer.Object1 => "Pintar Layer 1",
        _ => "Pintar Layer 2",
    };

    private static string EraseStrokeName(PaintLayer layer) => layer switch
    {
        PaintLayer.Ground => "Borrar Ground",
        PaintLayer.Object1 => "Borrar Layer 1",
        _ => "Borrar Layer 2",
    };

    private sealed class WorldCellClipboardEntry(CellSnapshot snapshot, double offsetWorldX, double offsetWorldY)
    {
        public CellSnapshot Snapshot { get; } = snapshot;
        public double OffsetWorldX { get; } = offsetWorldX;
        public double OffsetWorldY { get; } = offsetWorldY;
    }

    private sealed class WorldCellClipboard(IReadOnlyList<WorldCellClipboardEntry> entries)
    {
        public IReadOnlyList<WorldCellClipboardEntry> Entries { get; } = entries;
    }
}

internal static class CellDataListExtensions
{
    public static bool IsValidIndex(this IList<CellData> cells, int index) =>
        index >= 0 && index < cells.Count;
}
