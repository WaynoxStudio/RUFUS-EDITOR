namespace RufusMapEditor.Domain.World;

public enum WorldMapOrigin
{
    Library = 0,
    LocalDuplicate = 1,
    LocalNew = 2,
    Imported = 3,
}

public enum WorldMapPublicationState
{
    FromLibrary = 0,
    LocalUnpublished = 1,
}
