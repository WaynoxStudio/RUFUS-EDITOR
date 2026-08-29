using RufusMapEditor.Domain.Gfx;

namespace RufusMapEditor.LegacyCompatibility.Gfx;

/// <summary>
/// Post-build validation helpers. Most checks already run during <see cref="AstriaGfxCatalogBuilder.Build"/>;
/// this type aggregates severity counts for reporting.
/// </summary>
public static class GfxCatalogValidator
{
    public sealed class ValidationSummary
    {
        public required IReadOnlyList<GfxCatalogIssue> Issues { get; init; }
        public int ErrorCount { get; init; }
        public int WarningCount { get; init; }
        public int InfoCount { get; init; }
        public int DuplicateGfxIdCount { get; init; }
        public int XmlWithoutImageCount { get; init; }
        public int ImageWithoutAnchorCount { get; init; }
        public int DuplicateAnchorCount { get; init; }
        public int InvalidFileNameCount { get; init; }
        public int MalformedXmlCount { get; init; }
    }

    public static ValidationSummary Summarize(IEnumerable<GfxCatalogIssue> issues)
    {
        var list = issues.ToList();
        return new ValidationSummary
        {
            Issues = list,
            ErrorCount = list.Count(i => i.Severity == GfxIssueSeverity.Error),
            WarningCount = list.Count(i => i.Severity == GfxIssueSeverity.Warning),
            InfoCount = list.Count(i => i.Severity == GfxIssueSeverity.Info),
            DuplicateGfxIdCount = list.Count(i => i.Code == GfxIssueCode.DuplicateGfxId),
            XmlWithoutImageCount = list.Count(i => i.Code == GfxIssueCode.XmlEntryWithoutImage),
            ImageWithoutAnchorCount = list.Count(i => i.Code == GfxIssueCode.ImageWithoutExpectedAnchor),
            DuplicateAnchorCount = list.Count(i => i.Code == GfxIssueCode.DuplicateAnchor),
            InvalidFileNameCount = list.Count(i => i.Code == GfxIssueCode.InvalidFileName),
            MalformedXmlCount = list.Count(i => i.Code == GfxIssueCode.MalformedXml),
        };
    }
}
