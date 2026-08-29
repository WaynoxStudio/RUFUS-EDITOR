namespace RufusMapEditor.App.Services;

public sealed class FightCellDisplayItem
{
    public required int Value { get; init; }
    public required string Label { get; init; }

    public static IReadOnlyList<FightCellDisplayItem> Options { get; } =
    [
        new() { Value = 0, Label = "Ninguno" },
        new() { Value = 1, Label = "Equipo 1" },
        new() { Value = 2, Label = "Equipo 2" },
    ];

    public override string ToString() => Label;
}
