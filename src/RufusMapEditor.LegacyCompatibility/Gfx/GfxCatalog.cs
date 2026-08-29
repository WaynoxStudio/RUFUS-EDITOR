using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Immutable O(1) GFX catalog. Category namespaces are kept separate.
/// </summary>
public sealed class GfxCatalog : IGfxCatalog
{
    private readonly Dictionary<int, GfxResource> _backgrounds;
    private readonly Dictionary<int, GfxResource> _grounds;
    private readonly Dictionary<int, GfxResource> _objects;
    private readonly Dictionary<int, GfxAnchor> _groundAnchors;
    private readonly Dictionary<int, GfxAnchor> _objectAnchors;

    public GfxCatalog(
        IDictionary<int, GfxResource> backgrounds,
        IDictionary<int, GfxResource> grounds,
        IDictionary<int, GfxResource> objects,
        IDictionary<int, GfxAnchor>? groundAnchors = null,
        IDictionary<int, GfxAnchor>? objectAnchors = null)
    {
        _backgrounds = new Dictionary<int, GfxResource>(backgrounds);
        _grounds = new Dictionary<int, GfxResource>(grounds);
        _objects = new Dictionary<int, GfxResource>(objects);
        _groundAnchors = groundAnchors is null
            ? _grounds.Where(kv => kv.Value.HasAnchor).ToDictionary(kv => kv.Key, kv => kv.Value.Anchor!.Value)
            : new Dictionary<int, GfxAnchor>(groundAnchors);
        _objectAnchors = objectAnchors is null
            ? _objects.Where(kv => kv.Value.HasAnchor).ToDictionary(kv => kv.Key, kv => kv.Value.Anchor!.Value)
            : new Dictionary<int, GfxAnchor>(objectAnchors);
    }

    public int BackgroundCount => _backgrounds.Count;
    public int GroundCount => _grounds.Count;
    public int ObjectCount => _objects.Count;
    public int TotalCount => BackgroundCount + GroundCount + ObjectCount;

    public bool TryGet(GfxCategory category, int id, out GfxResource? resource) =>
        category switch
        {
            GfxCategory.Background => TryGetBackground(id, out resource),
            GfxCategory.Ground => TryGetGround(id, out resource),
            GfxCategory.Object => TryGetObject(id, out resource),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

    public bool TryGetBackground(int id, out GfxResource? resource) =>
        TryGetFrom(_backgrounds, id, out resource);

    public bool TryGetGround(int id, out GfxResource? resource) =>
        TryGetFrom(_grounds, id, out resource);

    public bool TryGetObject(int id, out GfxResource? resource) =>
        TryGetFrom(_objects, id, out resource);

    public bool TryGetAnchor(GfxCategory category, int id, out GfxAnchor anchor)
    {
        var map = category switch
        {
            GfxCategory.Ground => _groundAnchors,
            GfxCategory.Object => _objectAnchors,
            // Astria backgrounds use Get_Ground_Pos(Background.ID).
            GfxCategory.Background => _groundAnchors,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        if (map.TryGetValue(id, out anchor))
            return true;

        anchor = default;
        return false;
    }

    public IEnumerable<GfxResource> Enumerate(GfxCategory? category = null)
    {
        if (category is null)
        {
            foreach (var resource in _backgrounds.Values.OrderBy(r => r.Id))
                yield return resource;
            foreach (var resource in _grounds.Values.OrderBy(r => r.Id))
                yield return resource;
            foreach (var resource in _objects.Values.OrderBy(r => r.Id))
                yield return resource;
            yield break;
        }

        var source = category.Value switch
        {
            GfxCategory.Background => _backgrounds.Values,
            GfxCategory.Ground => _grounds.Values,
            GfxCategory.Object => _objects.Values,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };

        foreach (var resource in source.OrderBy(r => r.Id))
            yield return resource;
    }

    public IEnumerable<GfxResource> EnumerateById(int id)
    {
        if (_backgrounds.TryGetValue(id, out var background))
            yield return background;
        if (_grounds.TryGetValue(id, out var ground))
            yield return ground;
        if (_objects.TryGetValue(id, out var obj))
            yield return obj;
    }

    private static bool TryGetFrom(Dictionary<int, GfxResource> map, int id, out GfxResource? resource)
    {
        if (map.TryGetValue(id, out var found))
        {
            resource = found;
            return true;
        }

        resource = null;
        return false;
    }
}
