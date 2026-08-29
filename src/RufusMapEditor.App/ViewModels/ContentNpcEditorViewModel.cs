using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Content;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.App.ViewModels;

public sealed class ContentNpcEditorViewModel : ViewModelBase
{
    private readonly ContentDraftWorkspace? _injectedWorkspace;
    private readonly Func<INpcsModeloReadRepository>? _repoFactory;
    private NpcDraftItemViewModel? _selected;
    private string _statusText = "Sin conectar";
    private string _searchQuery = "";
    private bool _dbReady;
    private int _dbMaxId;
    private bool _isBusy;

    private ContentDraftWorkspace Workspace => _injectedWorkspace ?? ContentDraftStore.Current;
    private NpcDraftBatch Batch => Workspace.Npcs;

    public ContentNpcEditorViewModel(
        ContentDraftWorkspace? workspace = null,
        Func<INpcsModeloReadRepository>? repoFactory = null)
    {
        _injectedWorkspace = workspace;
        _repoFactory = repoFactory;
        Items = new ObservableCollection<NpcDraftItemViewModel>();
        FilteredItems = CollectionViewSource.GetDefaultView(Items);
        FilteredItems.Filter = FilterNpc;
        foreach (var d in Batch.Drafts)
        {
            var vm = new NpcDraftItemViewModel(d);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(NpcDraftItemViewModel.Nombre))
                    FilteredItems.Refresh();
                Persist();
            };
            Items.Add(vm);
        }

        RefreshMaxCommand = new RelayCommand(async () => await RefreshMaxAsync(), () => !IsBusy);
        NewNpcCommand = new RelayCommand(CreateNew, () => DbReady && !IsBusy);
        DuplicateCommand = new RelayCommand(DuplicateSelected, () => Selected is not null && DbReady && !IsBusy);
        DeleteCommand = new RelayCommand(DeleteSelected, () => Selected is not null && !IsBusy);
        AddLocationCommand = new RelayCommand(AddLocation, () => Selected is not null && !IsBusy);
        RemoveLocationCommand = new RelayCommand(RemoveLocation, () => Selected is not null && SelectedLocation is not null && !IsBusy);
        PickGfxAppearanceCommand = new RelayCommand(async () => await PickGfxAppearanceAsync(), () => EditorEnabled && !IsBusy);
    }

    public ObservableCollection<NpcDraftItemViewModel> Items { get; }

    public ICollectionView FilteredItems { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                FilteredItems.Refresh();
                OnPropertyChanged(nameof(ItemCountLabel));
            }
        }
    }

    public string ItemCountLabel => $"{Items.Count} NPC{(Items.Count == 1 ? "" : "s")}";

    public NpcDraftItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            SelectedLocation = null;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(EditorEnabled));
            DuplicateCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            AddLocationCommand.RaiseCanExecuteChanged();
            RemoveLocationCommand.RaiseCanExecuteChanged();
            PickGfxAppearanceCommand.RaiseCanExecuteChanged();
            Persist();
        }
    }

    private NpcLocationDraft? _selectedLocation;
    public NpcLocationDraft? SelectedLocation
    {
        get => _selectedLocation;
        set
        {
            if (SetProperty(ref _selectedLocation, value))
                RemoveLocationCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;
    public bool EditorEnabled => Selected is not null;
    public bool DbReady
    {
        get => _dbReady;
        private set
        {
            if (SetProperty(ref _dbReady, value))
                NewNpcCommand.RaiseCanExecuteChanged();
        }
    }

    public int DbMaxId
    {
        get => _dbMaxId;
        private set => SetProperty(ref _dbMaxId, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshMaxCommand.RaiseCanExecuteChanged();
                NewNpcCommand.RaiseCanExecuteChanged();
                DuplicateCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                AddLocationCommand.RaiseCanExecuteChanged();
                RemoveLocationCommand.RaiseCanExecuteChanged();
                PickGfxAppearanceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int NextProvisionalId => Batch.NextProvisionalId;

    public RelayCommand RefreshMaxCommand { get; }
    public RelayCommand NewNpcCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand AddLocationCommand { get; }
    public RelayCommand RemoveLocationCommand { get; }
    public RelayCommand PickGfxAppearanceCommand { get; }

    public async Task InitializeAsync() => await RefreshMaxAsync();

    public async Task RefreshMaxAsync()
    {
        IsBusy = true;
        try
        {
            var repo = CreateRepository();
            if (repo is null)
            {
                DbReady = false;
                StatusText = "Configura MySQL (Archivo → Configuración BD) para leer MAX(id).";
                return;
            }

            var max = await repo.GetMaxIdAsync().ConfigureAwait(true);
            Batch.SetDbMaxId(max);
            DbMaxId = max;
            DbReady = true;
            StatusText = $"MAX(npcs_modelo.id) = {max} · próximo ID provisional = {Batch.NextProvisionalId} · solo lectura BD";
            OnPropertyChanged(nameof(NextProvisionalId));
            Persist();
        }
        catch (Exception ex)
        {
            DbReady = false;
            StatusText = "Error leyendo MAX(id): " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CreateNew()
    {
        if (!DbReady)
            return;
        var draft = Batch.CreateNew();
        var vm = new NpcDraftItemViewModel(draft);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NpcDraftItemViewModel.Nombre))
                FilteredItems.Refresh();
            Persist();
        };
        Items.Add(vm);
        Selected = vm;
        OnPropertyChanged(nameof(ItemCountLabel));
        StatusText = $"Borrador {draft.Id} creado · próximo = {Batch.NextProvisionalId}";
        OnPropertyChanged(nameof(NextProvisionalId));
        Persist();
    }

    private void DuplicateSelected()
    {
        if (Selected is null || !DbReady)
            return;
        var copy = Batch.Duplicate(Selected.Model);
        var vm = new NpcDraftItemViewModel(copy);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NpcDraftItemViewModel.Nombre))
                FilteredItems.Refresh();
            Persist();
        };
        Items.Add(vm);
        Selected = vm;
        OnPropertyChanged(nameof(ItemCountLabel));
        StatusText = $"Duplicado → ID provisional {copy.Id}";
        OnPropertyChanged(nameof(NextProvisionalId));
        Persist();
    }

    private void DeleteSelected()
    {
        if (Selected is null)
            return;
        var id = Selected.Id;
        var missionRefs = Workspace.Missions.MissionsReferencingNpc(id);
        if (missionRefs.Count > 0)
        {
            var msg =
                $"El NPC {id} está referenciado por {missionRefs.Count} misión(es).\n\n" +
                "OK = limpiar referencias de misión y eliminar NPC.\nCancelar = no borrar.";
            if (MessageBox.Show(msg, "NPC referenciado", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
                return;
            Workspace.Missions.ClearNpcReferences(id);
        }

        if (!Batch.Remove(Selected.Model))
            return;
        Items.Remove(Selected);
        Selected = Items.LastOrDefault();
        OnPropertyChanged(nameof(ItemCountLabel));
        FilteredItems.Refresh();
        StatusText = $"Eliminado borrador {id} · sin IDs duplicados = {!Batch.HasDuplicateIds()}";
        OnPropertyChanged(nameof(NextProvisionalId));
        Persist();
    }

    private void AddLocation()
    {
        if (Selected is null) return;
        var loc = Batch.AddLocation(Selected.Model);
        SelectedLocation = loc;
        StatusText = $"Ubicación añadida · npc={Selected.Id} (mapa/celda a completar)";
        Persist();
    }

    private void RemoveLocation()
    {
        if (Selected is null || SelectedLocation is null) return;
        var npcId = Selected.Id;
        if (!Batch.RemoveLocation(Selected.Model, SelectedLocation))
            return;
        SelectedLocation = Selected.Model.Locations.LastOrDefault();
        StatusText = $"Ubicación eliminada · NPC {npcId} intacto";
        Persist();
    }

    /// <summary>Called from XAML when location fields change.</summary>
    public void NotifyLocationEdited() => Persist();

    private bool FilterNpc(object obj)
    {
        if (obj is not NpcDraftItemViewModel npc)
            return false;
        if (string.IsNullOrWhiteSpace(_searchQuery))
            return true;
        var q = _searchQuery.Trim();
        return npc.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
               || npc.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Persist()
    {
        if (_injectedWorkspace is null)
            ContentDraftStore.Save();
    }

    private INpcsModeloReadRepository? CreateRepository()
    {
        if (_repoFactory is not null)
            return _repoFactory();

        var settings = AppSettingsStore.Load().Database;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
            return null;
        var password = DatabasePasswordProtector.Unprotect(settings.PasswordProtectedBase64);
        return new MysqlNpcsModeloReadRepository(settings, password);
    }

    private async Task PickGfxAppearanceAsync()
    {
        if (Selected is null)
            return;

        var settings = AppSettingsStore.Load();
        VisualLibraryBootstrap.ConfigurePreviewFromSettings(settings);
        NpcGfxCatalogService.Shared.SetClipsRoot(settings.ClipsRootPath);

        try
        {
            if (!NpcGfxCatalogService.Shared.IsLoaded)
            {
                IsBusy = true;
                var db = settings.Database;
                if (string.IsNullOrWhiteSpace(db.Host) || string.IsNullOrWhiteSpace(db.User))
                {
                    MessageBox.Show(
                        "No se pudo cargar el catálogo de apariencias NPC.\n\nConfigura MySQL en Archivo → Configuración BD.",
                        "Apariencias NPC",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var password = DatabasePasswordProtector.Unprotect(db.PasswordProtectedBase64);
                await NpcGfxCatalogService.Shared.LoadAsync(
                    db,
                    password,
                    settings.ClipsRootPath).ConfigureAwait(true);
            }
            else
            {
                NpcGfxCatalogService.Shared.ReloadSpriteMetadata(settings.ClipsRootPath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo cargar el catálogo de apariencias NPC.\n\n" + ex.Message,
                "Apariencias NPC",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (!NpcGfxCatalogService.Shared.IsLoaded)
        {
            MessageBox.Show(
                "No se pudo cargar el catálogo de apariencias NPC.",
                "Apariencias NPC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var owner = Application.Current?.MainWindow;
        if (owner is null && Application.Current?.Windows.Count > 0)
            owner = Application.Current.Windows[0];

        var dlg = new NpcGfxPickerWindow(NpcGfxCatalogService.Shared, Selected.GfxId)
        {
            Owner = owner,
        };
        if (dlg.ShowDialog() != true || dlg.SelectedGfxId is not int gfxId)
            return;

        Selected.GfxId = gfxId;
        Persist();
    }
}

public sealed class NpcDraftItemViewModel : ViewModelBase
{
    private bool _userEditedTamaño;

    public NpcDraftItemViewModel(NpcsModeloDraft model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public NpcsModeloDraft Model { get; }

    public int Id => Model.Id;
    public string IdDisplay => $"#{Id}";
    public string Status => Model.Status;

    public static IReadOnlyList<NpcSexoUi> SexoUiOptions { get; } =
        new[] { NpcSexoUi.Hombre, NpcSexoUi.Mujer };

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Nombre) ? $"(sin nombre) · {Id}" : $"{Nombre} · {Id}";

    public string Nombre
    {
        get => Model.Nombre;
        set
        {
            if (Model.Nombre == value) return;
            Model.Nombre = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public int GfxId
    {
        get => Model.GfxId;
        set
        {
            if (Model.GfxId == value)
                return;
            Model.GfxId = value;
            NotifyGfxPresentation();
        }
    }

    public string GfxDisplayName =>
        NpcGfxCatalogService.Shared.IsLoaded
            ? NpcGfxCatalogService.Shared.ResolveDisplayName(GfxId)
            : NpcGfxAppearanceNames.Resolve(GfxId, AppSettingsStore.Load().ClipsRootPath);

    public string GfxIdTechnicalLabel => $"GFX #{GfxId}";

    public string GfxUsageHint
    {
        get
        {
            var entry = NpcGfxCatalogService.Shared.TryGet(GfxId);
            return entry?.UsageSummary ?? "";
        }
    }

    private void NotifyGfxPresentation()
    {
        OnPropertyChanged(nameof(GfxId));
        OnPropertyChanged(nameof(GfxDisplayName));
        OnPropertyChanged(nameof(GfxIdTechnicalLabel));
        OnPropertyChanged(nameof(GfxUsageHint));
    }

    public int Sexo
    {
        get => Model.Sexo;
        set
        {
            var v = value != 0 ? 1 : 0;
            if (Model.Sexo != v)
            {
                Model.Sexo = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SexoUi));
            }
        }
    }

    public NpcSexoUi SexoUi
    {
        get => NpcIdentityUi.SexoToUi(Sexo);
        set => Sexo = NpcIdentityUi.SexoFromUi(value);
    }

    public bool HasUnequalScale => NpcIdentityUi.HasUnequalScale(ScaleX, ScaleY);

    public bool ShowUnequalScaleHint => HasUnequalScale && !_userEditedTamaño;

    public string UnequalScaleHint => NpcIdentityUi.FormatUnequalScaleHint(ScaleX, ScaleY);

    public string Tamaño
    {
        get => NpcIdentityUi.FormatTamañoDisplay(ScaleX, ScaleY, _userEditedTamaño);
        set
        {
            if (!NpcIdentityUi.TryParseTamaño(value, out var v))
                return;
            var (sx, sy) = NpcIdentityUi.ApplyUniformTamaño(v);
            ScaleX = sx;
            ScaleY = sy;
            _userEditedTamaño = true;
            NotifyTamañoRelated();
        }
    }

    public int ScaleX
    {
        get => Model.ScaleX;
        set
        {
            if (Model.ScaleX != value)
            {
                Model.ScaleX = value;
                OnPropertyChanged();
                NotifyTamañoRelated();
            }
        }
    }

    public int ScaleY
    {
        get => Model.ScaleY;
        set
        {
            if (Model.ScaleY != value)
            {
                Model.ScaleY = value;
                OnPropertyChanged();
                NotifyTamañoRelated();
            }
        }
    }

    private void NotifyTamañoRelated()
    {
        OnPropertyChanged(nameof(Tamaño));
        OnPropertyChanged(nameof(HasUnequalScale));
        OnPropertyChanged(nameof(ShowUnequalScaleHint));
        OnPropertyChanged(nameof(UnequalScaleHint));
    }

    public int Color1
    {
        get => Model.Color1;
        set { if (Model.Color1 != value) { Model.Color1 = value; OnPropertyChanged(); } }
    }

    public int Color2
    {
        get => Model.Color2;
        set { if (Model.Color2 != value) { Model.Color2 = value; OnPropertyChanged(); } }
    }

    public int Color3
    {
        get => Model.Color3;
        set { if (Model.Color3 != value) { Model.Color3 = value; OnPropertyChanged(); } }
    }

    public string Accesorios
    {
        get => Model.Accesorios;
        set
        {
            var v = value ?? NpcsModeloDraft.DefaultAccesorios;
            if (Model.Accesorios == v) return;
            Model.Accesorios = v;
            OnPropertyChanged();
        }
    }

    public int Foto
    {
        get => Model.Foto;
        set { if (Model.Foto != value) { Model.Foto = value; OnPropertyChanged(); } }
    }

    public int Pregunta
    {
        get => Model.Pregunta;
        set { if (Model.Pregunta != value) { Model.Pregunta = value; OnPropertyChanged(); } }
    }

    public string Ventas
    {
        get => Model.Ventas;
        set
        {
            var v = value ?? "";
            if (Model.Ventas == v) return;
            Model.Ventas = v;
            OnPropertyChanged();
        }
    }

    public int ObjetoCompra
    {
        get => Model.ObjetoCompra;
        set { if (Model.ObjetoCompra != value) { Model.ObjetoCompra = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<NpcLocationDraft> Locations => Model.Locations;

    public ObservableCollection<NpcClientActionItemViewModel> ClientActions { get; } = new();

    public IReadOnlyList<int> SelectedClientActionIds =>
        NpcEsClientActions.Normalize(
            ClientActions.Where(a => a.IsChecked).Select(a => a.Id));

    public string ClientActionsSummary
    {
        get
        {
            var ids = ClientActions.Where(a => a.IsChecked).Select(a => a.Id).ToList();
            return NpcEsClientActions.FormatList(ids);
        }
    }

    public string ClientActionsCompactSummary =>
        NpcClientActionsUi.FormatCompactSummary(SelectedClientActionIds);

    public bool ShowCommerceFields =>
        NpcClientActionsUi.ShowCommerceFields(SelectedClientActionIds);

    public void RefreshFromModel()
    {
        _userEditedTamaño = false;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(IdDisplay));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Nombre));
        OnPropertyChanged(nameof(SexoUi));
        NotifyTamañoRelated();
        OnPropertyChanged(nameof(Pregunta));
        NotifyClientActionsChanged();
    }

    private void NotifyClientActionsChanged()
    {
        OnPropertyChanged(nameof(SelectedClientActionIds));
        OnPropertyChanged(nameof(ClientActionsSummary));
        OnPropertyChanged(nameof(ClientActionsCompactSummary));
        OnPropertyChanged(nameof(ShowCommerceFields));
    }

    public void SyncClientActionsForDialog(ContentDraftWorkspace workspace)
    {
        var forceTalk = NpcEsActionResolver.HasClientDialog(workspace, Model);
        if (forceTalk && !Model.NpcEsActionIds.Contains(NpcEsClientActions.Talk))
            Model.NpcEsActionIds.Add(NpcEsClientActions.Talk);
        RebuildClientActions(forceTalk);
    }

    private void RebuildClientActions(bool forceTalk)
    {
        ClientActions.Clear();
        foreach (var (id, label) in NpcEsClientActions.All)
        {
            var item = new NpcClientActionItemViewModel(
                id,
                label,
                Model.NpcEsActionIds.Contains(id) || (forceTalk && id == NpcEsClientActions.Talk),
                forceTalk && id == NpcEsClientActions.Talk,
                OnClientActionChanged);
            ClientActions.Add(item);
        }

        NotifyClientActionsChanged();
    }

    private void OnClientActionChanged()
    {
        Model.NpcEsActionIds = ClientActions
            .Where(a => a.IsChecked)
            .Select(a => a.Id)
            .Where(NpcEsClientActions.IsValid)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        NotifyClientActionsChanged();
    }
}

/// <summary>CONT.7B.1 — checkbox row for npc_es client actions.</summary>
public sealed class NpcClientActionItemViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isChecked;

    public NpcClientActionItemViewModel(int id, string label, bool isChecked, bool isLocked, Action onChanged)
    {
        Id = id;
        Label = label;
        Display = $"[{id}] {label}";
        IsLocked = isLocked;
        _isChecked = isChecked || isLocked;
        _onChanged = onChanged;
    }

    public int Id { get; }
    public string Label { get; }
    public string Display { get; }
    public bool IsLocked { get; }
    public bool CanToggle => !IsLocked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (IsLocked)
            {
                if (!_isChecked)
                {
                    _isChecked = true;
                    OnPropertyChanged();
                    _onChanged();
                }
                return;
            }

            if (SetProperty(ref _isChecked, value))
                _onChanged();
        }
    }
}
