using System.Collections.ObjectModel;

namespace RufusMapEditor.LegacyCompatibility.Content;

/// <summary>CONT-DIALOG.3 — two real dialog modes confirmed in BD audit.</summary>
public enum NpcDialogMode
{
    /// <summary>Default for JSON missing field (legacy drafts = interactive CONT.3).</summary>
    Interactive = 0,

    /// <summary>Single phrase via npcs_modelo.pregunta = existing dialog text id. No npc_preguntas/respuestas.</summary>
    Simple = 1,
}

/// <summary>
/// Local draft mirroring estaticos.npcs_modelo (CONT.2) + npcs_ubicacion rows (CONT.2.1).
/// Never written to production BD in this phase.
/// </summary>
public sealed class NpcsModeloDraft
{
    public const string StatusBorrador = "Borrador";

    public const int DefaultGfxId = 71;
    public const int DefaultScaleX = 100;
    public const int DefaultScaleY = 100;
    public const int DefaultColor1 = -1;
    public const int DefaultColor2 = -1;
    public const int DefaultColor3 = -1;
    public const string DefaultAccesorios = "0,0,0,0,0";
    public const int DefaultFoto = 0;
    public const int DefaultPregunta = 0;
    public const int DefaultObjetoCompra = 0;

    /// <summary>Provisional id for the current draft batch (read-only in UI).</summary>
    public int Id { get; set; }

    public int GfxId { get; set; }
    public int ScaleX { get; set; } = DefaultScaleX;
    public int ScaleY { get; set; } = DefaultScaleY;
    /// <summary>0/1 matching tinyint(1) sexo.</summary>
    public int Sexo { get; set; }
    public int Color1 { get; set; } = DefaultColor1;
    public int Color2 { get; set; } = DefaultColor2;
    public int Color3 { get; set; } = DefaultColor3;
    public string Accesorios { get; set; } = DefaultAccesorios;
    public int Foto { get; set; } = DefaultFoto;
    public int Pregunta { get; set; } = DefaultPregunta;
    public string Ventas { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int ObjetoCompra { get; set; } = DefaultObjetoCompra;

    /// <summary>
    /// CONT-DIALOG.3 — Simple vs Interactive.
    /// Default Interactive so legacy JSON without this field keeps CONT.3 trees.
    /// New NPCs set Simple in <see cref="CreateWithDefaults"/>.
    /// </summary>
    public NpcDialogMode DialogMode { get; set; } = NpcDialogMode.Interactive;

    /// <summary>Local NPC phrase for Simple mode (future dialog_es). Not published yet.</summary>
    public string SimpleDialogTextLocal { get; set; } = "";

    /// <summary>CONT.6C — true after dialog_es SFTP publish for this NPC's new texts.</summary>
    public bool DialogEsPublished { get; set; }

    /// <summary>CONT.6C — active dialog_es version after successful publish (N+1).</summary>
    public int? DialogEsPublishedVersion { get; set; }

    /// <summary>CONT.7B — true after npc_es SFTP publish matching name+actions.</summary>
    public bool NpcEsPublished { get; set; }

    /// <summary>CONT.7B — active npc_es version after successful publish (N+1).</summary>
    public int? NpcEsPublishedVersion { get; set; }

    /// <summary>CONT.7B.1 — name last successfully published to npc_es.</summary>
    public string NpcEsPublishedName { get; set; } = "";

    /// <summary>CONT.7B.1 — user-selected client action ids (Hablar[3] may be added by resolver).</summary>
    public List<int> NpcEsActionIds { get; set; } = new();

    /// <summary>CONT.7B.1 — actions last successfully published (normalized CSV snapshot).</summary>
    public List<int> NpcEsPublishedActionIds { get; set; } = new();

    /// <summary>True after successful CONT.5 BD publish — blocks accidental re-INSERT.</summary>
    public bool PublishedBd { get; set; }

    /// <summary>Simple mode with an existing dialog text ID ready for npcs_modelo.pregunta.</summary>
    public bool IsSimpleDialogComplete =>
        DialogMode == NpcDialogMode.Simple && Pregunta > 0;

    /// <summary>Simple mode has local text but no dialog_es ID yet — block BD publish.</summary>
    public bool IsPendingDialogEs =>
        DialogMode == NpcDialogMode.Simple
        && Pregunta <= 0
        && !string.IsNullOrWhiteSpace(SimpleDialogTextLocal);

    /// <summary>
    /// CONT.7B.1 — needs npc_es publish/repair when name set and remote/local published state
    /// does not match expected name+actions. Requires workspace for dialog-aware Hablar[3].
    /// </summary>
    public bool IsPendingNpcEsFor(ContentDraftWorkspace workspace)
    {
        if (Id <= 0 || string.IsNullOrWhiteSpace(Nombre))
            return false;
        var expected = NpcEsActionResolver.ResolveExpected(workspace, this);
        if (!NpcEsPublished)
            return true;
        if (!string.Equals(Nombre.Trim(), NpcEsPublishedName.Trim(), StringComparison.Ordinal))
            return true;
        return !NpcEsClientActions.SameSet(expected, NpcEsPublishedActionIds);
    }

    /// <summary>Legacy accessor — without workspace cannot apply Hablar[3]; prefer <see cref="IsPendingNpcEsFor"/>.</summary>
    public bool IsPendingNpcEs =>
        Id > 0
        && !string.IsNullOrWhiteSpace(Nombre)
        && (!NpcEsPublished
            || !string.Equals(Nombre.Trim(), NpcEsPublishedName.Trim(), StringComparison.Ordinal)
            || !NpcEsClientActions.SameSet(NpcEsActionIds, NpcEsPublishedActionIds));

    public bool IsNpcEsIncompleteFor(ContentDraftWorkspace workspace)
    {
        if (!NpcEsPublished || Id <= 0) return false;
        if (!string.Equals(Nombre.Trim(), NpcEsPublishedName.Trim(), StringComparison.Ordinal))
            return false;
        var expected = NpcEsActionResolver.ResolveExpected(workspace, this);
        if (NpcEsClientActions.SameSet(expected, NpcEsPublishedActionIds))
            return false;
        // Incomplete = published name OK but actions missing expected (esp. Hablar).
        return expected.Any(e => !NpcEsPublishedActionIds.Contains(e));
    }

    /// <summary>Simple mode missing a usable dialog ID (with or without local text).</summary>
    public bool IsSimpleDialogBlocked =>
        DialogMode == NpcDialogMode.Simple && Pregunta <= 0;

    /// <summary>Local npcs_ubicacion drafts for this NPC (0..N). Contained here so they never cross NPCs.</summary>
    public ObservableCollection<NpcLocationDraft> Locations { get; set; } = new();

    public string Status => PublishedBd ? "Publicado BD" : StatusBorrador;

    /// <summary>
    /// Column <c>npcs_ubicacion.npc</c> for publish (CONT.5). Always this draft's provisional Id —
    /// never stored separately on the location (avoids stale links after duplicate).
    /// </summary>
    public int ResolveLocationNpcId(NpcLocationDraft location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!Locations.Contains(location))
            throw new InvalidOperationException("La ubicación no pertenece a este NPC.");
        return Id;
    }

    public static NpcsModeloDraft CreateWithDefaults(int provisionalId) => new()
    {
        Id = provisionalId,
        GfxId = DefaultGfxId,
        ScaleX = DefaultScaleX,
        ScaleY = DefaultScaleY,
        Sexo = 0,
        Color1 = DefaultColor1,
        Color2 = DefaultColor2,
        Color3 = DefaultColor3,
        Accesorios = DefaultAccesorios,
        Foto = DefaultFoto,
        Pregunta = DefaultPregunta,
        Ventas = "",
        Nombre = "",
        ObjetoCompra = DefaultObjetoCompra,
        DialogMode = NpcDialogMode.Simple,
        SimpleDialogTextLocal = "",
    };

    public NpcsModeloDraft CloneData()
    {
        var copy = new NpcsModeloDraft
        {
            Id = Id,
            GfxId = GfxId,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            Sexo = Sexo,
            Color1 = Color1,
            Color2 = Color2,
            Color3 = Color3,
            Accesorios = Accesorios,
            Foto = Foto,
            Pregunta = Pregunta,
            Ventas = Ventas,
            Nombre = Nombre,
            ObjetoCompra = ObjetoCompra,
            DialogMode = DialogMode,
            SimpleDialogTextLocal = SimpleDialogTextLocal,
            DialogEsPublished = DialogEsPublished,
            DialogEsPublishedVersion = DialogEsPublishedVersion,
            NpcEsPublished = NpcEsPublished,
            NpcEsPublishedVersion = NpcEsPublishedVersion,
            NpcEsPublishedName = NpcEsPublishedName,
            NpcEsActionIds = NpcEsActionIds.ToList(),
            NpcEsPublishedActionIds = NpcEsPublishedActionIds.ToList(),
            PublishedBd = PublishedBd,
        };
        foreach (var loc in Locations)
            copy.Locations.Add(loc.Clone());
        return copy;
    }
}

/// <summary>
/// Local draft row for estaticos.npcs_ubicacion.
/// Map/Cell are user-supplied only — never auto-invented.
/// Column <c>npc</c> is resolved from the owning <see cref="NpcsModeloDraft.Id"/>.
/// </summary>
public sealed class NpcLocationDraft
{
    /// <summary>mapa — user input; 0 means unset (not invented).</summary>
    public int MapId { get; set; }

    /// <summary>celda — user input; 0 means unset (not invented).</summary>
    public int CellId { get; set; }

    /// <summary>
    /// orientacion — raw int. Visual UI uses 1–8 (<see cref="NpcOrientationCatalog"/>); 0 = unset.
    /// </summary>
    public int Orientation { get; set; }

    /// <summary>
    /// Legacy draft field only (old workspace JSON). Not edited in UI.
    /// Publish always uses <see cref="NpcsModeloDraft.Nombre"/> via the plan builder.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>condicion — may be empty.</summary>
    public string Condition { get; set; } = "";

    public NpcLocationDraft Clone() => new()
    {
        MapId = MapId,
        CellId = CellId,
        Orientation = Orientation,
        Name = "",
        Condition = Condition,
    };
}
