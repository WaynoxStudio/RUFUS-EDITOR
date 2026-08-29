namespace RufusMapEditor.Domain.Gfx;

public enum GfxIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public enum GfxIssueCode
{
    DuplicateGfxId,
    InvalidFileName,
    UnsupportedExtension,
    UnreadableFile,
    MalformedXml,
    XmlEntryWithoutImage,
    ImageWithoutExpectedAnchor,
    DuplicateAnchor,
    InvalidAnchorData,
    CrossCategoryIdOverlap,
    XmlNullPaddingStripped,
}

public sealed class GfxCatalogIssue
{
    public required GfxIssueSeverity Severity { get; init; }
    public required GfxIssueCode Code { get; init; }
    public required string Message { get; init; }
    public GfxCategory? Category { get; init; }
    public int? GfxId { get; init; }
    public string? Path { get; init; }
}
