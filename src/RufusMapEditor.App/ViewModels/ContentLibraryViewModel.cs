using System.Collections.ObjectModel;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.ViewModels;

public sealed class ContentLibraryCardVm : ViewModelBase
{
    private readonly ContentLibraryViewModel _owner;
    private bool _isChecked;

    public ContentLibraryCardVm(ContentLibraryViewModel owner, ContentLibraryEntry entry)
    {
        _owner = owner;
        Entry = entry;
    }

    public ContentLibraryEntry Entry { get; }

    public bool CanMultiSelect => Entry.CanRemove;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!CanMultiSelect)
            {
                if (_isChecked)
                {
                    _isChecked = false;
                    OnPropertyChanged(nameof(IsChecked));
                    _owner.NotifySelectionChanged();
                }

                return;
            }

            if (!SetProperty(ref _isChecked, value))
                return;
            _owner.NotifySelectionChanged();
        }
    }
}

public sealed class ContentLibrarySectionVm
{
    public required string Title { get; init; }
    public bool IsSubSection { get; init; }
    public ObservableCollection<ContentLibraryCardVm> Cards { get; init; } = new();
}

public sealed class ContentLibraryViewModel : ViewModelBase
{
    private readonly Func<ContentDraftWorkspace> _workspace;
    private readonly Action<ContentLibraryEntry> _open;
    private readonly Action<ContentLibraryEntry> _duplicate;
    private readonly Action<ContentLibraryEntry> _remove;
    private readonly Action<IReadOnlyList<ContentLibraryEntry>> _removeMany;
    private string _searchQuery = "";
    private ContentLibraryMode _mode = ContentLibraryMode.NpcAndDialog;
    private ContentLibraryFilter _filter = ContentLibraryFilter.All;
    private ContentLibraryEntry? _selectedEntry;
    private IReadOnlyList<ContentLibraryEntry> _all = Array.Empty<ContentLibraryEntry>();
    private ContentLibraryCounters _counters = new();

    public ContentLibraryViewModel(
        Func<ContentDraftWorkspace> workspace,
        Action<ContentLibraryEntry> open,
        Action<ContentLibraryEntry> duplicate,
        Action<ContentLibraryEntry> remove,
        Action<IReadOnlyList<ContentLibraryEntry>> removeMany)
    {
        _workspace = workspace;
        _open = open;
        _duplicate = duplicate;
        _remove = remove;
        _removeMany = removeMany;
        OpenCommand = new RelayCommand(OpenSelected, () => SelectedEntry is { CanOpen: true });
        DuplicateCommand = new RelayCommand(DuplicateSelected, () => SelectedEntry is { CanDuplicate: true });
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedEntry is { CanRemove: true });
        RemoveCheckedCommand = new RelayCommand(RemoveChecked, () => CheckedCount > 0);
        SelectVisibleDraftsCommand = new RelayCommand(SelectVisibleDrafts, () => VisibleRemovableDraftCount > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => CheckedCount > 0);
        RefreshCommand = new RelayCommand(Refresh);
        SetModeCommand = new RelayCommand(p =>
        {
            if (p is ContentLibraryMode m)
                Mode = m;
            else if (p is string s && Enum.TryParse(s, true, out ContentLibraryMode parsed))
                Mode = parsed;
        });
        SetFilterCommand = new RelayCommand(p =>
        {
            if (p is ContentLibraryFilter f)
                Filter = f;
            else if (p is string s && Enum.TryParse(s, true, out ContentLibraryFilter parsed))
                Filter = parsed;
        });
        Refresh();
    }

    public ObservableCollection<ContentLibrarySectionVm> Sections { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? ""))
                return;
            ApplyFilter();
        }
    }

    public ContentLibraryMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;
            if (_filter == ContentLibraryFilter.WithoutStartNpc)
                _filter = ContentLibraryFilter.All;
            OnPropertyChanged(nameof(Filter));
            OnPropertyChanged(nameof(IsNpcAndDialogMode));
            OnPropertyChanged(nameof(IsMissionsMode));
            OnPropertyChanged(nameof(EmptyLinkFilterLabel));
            ApplyFilter();
        }
    }

    public bool IsNpcAndDialogMode => Mode == ContentLibraryMode.NpcAndDialog;
    public bool IsMissionsMode => Mode == ContentLibraryMode.Missions;

    public string EmptyLinkFilterLabel =>
        Mode == ContentLibraryMode.Missions ? "Sin NPC inicial" : "Sin misión";

    public ContentLibraryFilter Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value))
                return;
            ApplyFilter();
        }
    }

    public ContentLibraryEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
                return;
            RaiseRowCommands();
            OnPropertyChanged(nameof(SelectedCanDuplicate));
            OnPropertyChanged(nameof(SelectedCanRemove));
            OnPropertyChanged(nameof(SelectedRemoveLabel));
        }
    }

    public bool SelectedCanDuplicate => SelectedEntry?.CanDuplicate == true;
    public bool SelectedCanRemove => SelectedEntry?.CanRemove == true;
    public string SelectedRemoveLabel => SelectedEntry?.RemoveActionLabel ?? "Eliminar";

    public int CheckedCount => Sections.SelectMany(s => s.Cards).Count(c => c.IsChecked);

    public int VisibleRemovableDraftCount =>
        Sections.SelectMany(s => s.Cards).Count(c => c.Entry.CanDeleteDraft);

    public string SelectionLine =>
        CheckedCount == 0
            ? "Selecciona tarjetas (casilla o Ctrl+clic) para borrar varias"
            : $"{CheckedCount} seleccionado(s)";

    public string RemoveCheckedLabel =>
        CheckedCount == 0 ? "Eliminar seleccionados" : $"Eliminar seleccionados ({CheckedCount})";

    public ContentLibraryCounters Counters => _counters;

    public string CountersLine =>
        $"Borradores ({_counters.Drafts}) · Cambios pendientes ({_counters.PendingChanges}) · Publicados ({_counters.Published})";

    public string StatusLine
    {
        get
        {
            var n = Sections.Sum(s => s.Cards.Count);
            return n == 0
                ? "Biblioteca vacía · los borradores aparecen al guardar en workspace.json"
                : $"{n} elemento(s) · workspace local";
        }
    }

    public RelayCommand OpenCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand RemoveCheckedCommand { get; }
    public RelayCommand SelectVisibleDraftsCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SetModeCommand { get; }
    public RelayCommand SetFilterCommand { get; }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(SelectionLine));
        OnPropertyChanged(nameof(RemoveCheckedLabel));
        RemoveCheckedCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        SelectVisibleDraftsCommand.RaiseCanExecuteChanged();
    }

    public void ToggleChecked(ContentLibraryCardVm card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.CanMultiSelect) return;
        card.IsChecked = !card.IsChecked;
    }

    public void Refresh()
    {
        _all = ContentLibraryCatalog.Build(_workspace());
        _counters = ContentLibraryCatalog.Count(_all);
        OnPropertyChanged(nameof(Counters));
        OnPropertyChanged(nameof(CountersLine));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var previousKeys = Sections
            .SelectMany(s => s.Cards)
            .Where(c => c.IsChecked)
            .Select(c => SelectionKey(c.Entry))
            .ToHashSet(StringComparer.Ordinal);

        var filtered = ContentLibraryCatalog.Filter(_all, Mode, Filter, SearchQuery);
        var sections = ContentLibraryCatalog.OrganizeSections(filtered, Mode, Filter);
        Sections.Clear();
        foreach (var s in sections)
        {
            var vm = new ContentLibrarySectionVm
            {
                Title = s.Title,
                IsSubSection = s.IsSubSection,
            };
            foreach (var e in s.Entries)
            {
                var card = new ContentLibraryCardVm(this, e)
                {
                    IsChecked = previousKeys.Contains(SelectionKey(e)),
                };
                vm.Cards.Add(card);
            }

            Sections.Add(vm);
        }

        if (SelectedEntry is not null
            && !Sections.SelectMany(s => s.Cards).Any(c => SameIdentity(c.Entry, SelectedEntry)))
        {
            SelectedEntry = Sections.SelectMany(s => s.Cards).Select(c => c.Entry).FirstOrDefault();
        }

        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(VisibleRemovableDraftCount));
        NotifySelectionChanged();
        RaiseRowCommands();
    }

    private static string SelectionKey(ContentLibraryEntry e) => e.Kind switch
    {
        ContentLibraryEntryKind.Mission => "m:" + e.MissionDraftId,
        ContentLibraryEntryKind.Npc => "n:" + e.NpcId,
        ContentLibraryEntryKind.Dialog => "d:" + e.NpcId + ":" + e.Title,
        _ => e.Title,
    };

    private static bool SameIdentity(ContentLibraryEntry a, ContentLibraryEntry b)
    {
        if (a.Kind != b.Kind) return false;
        return a.Kind switch
        {
            ContentLibraryEntryKind.Mission => a.MissionDraftId == b.MissionDraftId,
            ContentLibraryEntryKind.Npc => a.NpcId == b.NpcId,
            ContentLibraryEntryKind.Dialog => a.NpcId == b.NpcId && a.Title == b.Title,
            _ => false,
        };
    }

    private void RaiseRowCommands()
    {
        OpenCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }

    private void OpenSelected()
    {
        if (SelectedEntry is null) return;
        _open(SelectedEntry);
    }

    private void DuplicateSelected()
    {
        if (SelectedEntry is not { CanDuplicate: true }) return;
        _duplicate(SelectedEntry);
        Refresh();
    }

    private void RemoveSelected()
    {
        if (SelectedEntry is not { CanRemove: true }) return;
        _remove(SelectedEntry);
        Refresh();
    }

    private void RemoveChecked()
    {
        var list = Sections.SelectMany(s => s.Cards)
            .Where(c => c.IsChecked && c.Entry.CanRemove)
            .Select(c => c.Entry)
            .ToList();
        if (list.Count == 0) return;
        _removeMany(list);
        Refresh();
    }

    private void SelectVisibleDrafts()
    {
        foreach (var card in Sections.SelectMany(s => s.Cards))
            card.IsChecked = card.Entry.CanDeleteDraft;
    }

    private void ClearSelection()
    {
        foreach (var card in Sections.SelectMany(s => s.Cards))
            card.IsChecked = false;
    }
}
