using RufusMapEditor.Domain.Maps;

namespace RufusMapEditor.App.Services;

public sealed class MovementDisplayItem
{
    public required int RawValue { get; init; }
    public required string Label { get; init; }

    public Domain.Maps.MovementType? TypedMovement =>
        Enum.IsDefined(typeof(Domain.Maps.MovementType), RawValue) ? (Domain.Maps.MovementType)RawValue : null;

    public static IReadOnlyList<MovementDisplayItem> StandardOptions { get; } =
    [
        For(Domain.Maps.MovementType.Unwalkable),
        For(Domain.Maps.MovementType.Door),
        For(Domain.Maps.MovementType.Trigger),
        For(Domain.Maps.MovementType.Walkable),
        For(Domain.Maps.MovementType.Paddock),
        For(Domain.Maps.MovementType.Path),
    ];

    public static MovementDisplayItem For(Domain.Maps.MovementType movement) => new()
    {
        RawValue = (int)movement,
        Label = LabelForRaw((int)movement),
    };

    public static MovementDisplayItem ForRaw(int raw) => new()
    {
        RawValue = raw & 7,
        Label = LabelForRaw(raw & 7),
    };

    public static string LabelForRaw(int raw) => raw switch
    {
        0 => "0 — No transitable",
        1 => "1 — Puerta",
        2 => "2 — Trigger",
        3 => "3 — Raw sin significado confirmado",
        4 => "4 — Transitable",
        5 => "5 — Enclos",
        6 => "6 — Raw sin significado confirmado",
        7 => "7 — Camino",
        _ => $"{raw} — Raw",
    };

    public override string ToString() => Label;
}
