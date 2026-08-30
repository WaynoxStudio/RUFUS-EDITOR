using RufusMapEditor.App.Services;

namespace RufusMapEditor.App;

/// <summary>
/// Filter/sort prefs for map pickers and the MAPAS list. Persists until cleared manually.
/// </summary>
public sealed class MapPickerFilterState
{
    public string SearchText { get; set; } = "";
    public bool Ascending { get; set; } = true;
    public string RangeFromText { get; set; } = "";
    public string RangeToText { get; set; } = "";

    public event Action? Changed;

    public void LoadFrom(MapListFilterSettings settings)
    {
        if (settings is null) return;
        SearchText = settings.SearchText ?? "";
        RangeFromText = settings.RangeFromText ?? "";
        RangeToText = settings.RangeToText ?? "";
        Ascending = settings.Ascending;
    }

    public void SaveTo(MapListFilterSettings settings)
    {
        if (settings is null) return;
        settings.SearchText = SearchText ?? "";
        settings.RangeFromText = RangeFromText ?? "";
        settings.RangeToText = RangeToText ?? "";
        settings.Ascending = Ascending;
    }

    public void Clear()
    {
        SearchText = "";
        RangeFromText = "";
        RangeToText = "";
        Ascending = true;
        NotifyChanged();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public bool TryGetRange(out int? fromId, out int? toId)
    {
        fromId = null;
        toId = null;
        if (int.TryParse((RangeFromText ?? "").Trim(), out var from))
            fromId = from;
        if (int.TryParse((RangeToText ?? "").Trim(), out var to))
            toId = to;
        return fromId is not null || toId is not null || !string.IsNullOrWhiteSpace(SearchText);
    }

    public IEnumerable<int> FilterIds(IEnumerable<int> ids)
    {
        TryGetRange(out var fromId, out var toId);
        var q = (SearchText ?? "").Trim();
        IEnumerable<int> query = ids;
        if (fromId is int min)
            query = query.Where(id => id >= min);
        if (toId is int max)
            query = query.Where(id => id <= max);
        if (q.Length > 0)
            query = query.Where(id => id.ToString().Contains(q, StringComparison.Ordinal));
        return Ascending ? query.OrderBy(id => id) : query.OrderByDescending(id => id);
    }
}
