namespace RufusMapEditor.LegacyCompatibility.Content;

public sealed class DbNpcCatalogRow
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public int Pregunta { get; init; }
}

public sealed class DbQuestionCatalogRow
{
    public required int Id { get; init; }
    public string Respuestas { get; init; } = "";
    public string Params { get; init; } = "";
    public string Alternos { get; init; } = "";
}

public sealed class DbMissionCatalogRow
{
    public required int Id { get; init; }
    public required string Nombre { get; init; }
    public string Etapas { get; init; } = "";
    public string PregDarMision { get; init; } = "";
    public string PregMisCompletada { get; init; } = "";
    public string PregMisIncompleta { get; init; } = "";
    public bool PuedeRepetirse { get; init; }

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Nombre) ? $"#{Id}" : $"#{Id} · {Nombre}";
}

public sealed class ContentDbCatalogSnapshot
{
    public static ContentDbCatalogSnapshot Empty { get; } = new([], [], [], null, false);

    public ContentDbCatalogSnapshot(
        IReadOnlyList<DbNpcCatalogRow> npcs,
        IReadOnlyList<DbQuestionCatalogRow> questions,
        IReadOnlyList<DbMissionCatalogRow> missions,
        DialogEsSnapshot? dialogEs,
        bool loadedFromDatabase)
    {
        Npcs = npcs;
        Questions = questions;
        Missions = missions;
        DialogEs = dialogEs;
        LoadedFromDatabase = loadedFromDatabase;
    }

    public IReadOnlyList<DbNpcCatalogRow> Npcs { get; }
    public IReadOnlyList<DbQuestionCatalogRow> Questions { get; }
    public IReadOnlyList<DbMissionCatalogRow> Missions { get; }
    public DialogEsSnapshot? DialogEs { get; }
    public bool LoadedFromDatabase { get; }

    private Dictionary<int, DbNpcCatalogRow>? _npcById;
    private Dictionary<int, DbQuestionCatalogRow>? _questionById;

    public DbNpcCatalogRow? FindNpc(int id) =>
        ( _npcById ??= Npcs.ToDictionary(n => n.Id)).TryGetValue(id, out var row) ? row : null;

    public DbQuestionCatalogRow? FindQuestion(int id) =>
        (_questionById ??= Questions.ToDictionary(q => q.Id)).TryGetValue(id, out var row) ? row : null;
}

public sealed class ContentDbCatalogReadResult
{
    public bool Success { get; init; }
    public ContentDbCatalogSnapshot Snapshot { get; init; } = ContentDbCatalogSnapshot.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed class MissionQuestionChoiceItem
{
    public required int Id { get; init; }
    public required string Label { get; init; }

    public override string ToString() => Label;
}

public sealed class DbMissionCatalogItemVm
{
    public DbMissionCatalogItemVm(DbMissionCatalogRow row) => Row = row;
    public DbMissionCatalogRow Row { get; }
    public int Id => Row.Id;
    public string Nombre => Row.Nombre;
    public string DisplayLabel => Row.DisplayLabel;
    public bool IsReadOnly => true;
}
