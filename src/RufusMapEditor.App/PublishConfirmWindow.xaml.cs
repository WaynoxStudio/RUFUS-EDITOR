using System.Text;
using System.Windows;
using RufusMapEditor.App.Services;
using RufusMapEditor.LegacyCompatibility.Database;

namespace RufusMapEditor.App;

public partial class PublishConfirmWindow : Window
{
    public PublishConfirmWindow(PublishDiff diff, string currentFecha, string newFecha, string databaseLabel)
    {
        InitializeComponent();
        ThemeService.ApplyToWindow(this);
        TitleText.Text = $"PUBLICAR MAPA {diff.MapId}";
        BodyText.Text = BuildBody(diff, currentFecha, newFecha, databaseLabel);
    }

    public bool Confirmed { get; private set; }

    private static string BuildBody(PublishDiff diff, string currentFecha, string newFecha, string databaseLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BD: {databaseLabel}");
        sb.AppendLine();
        sb.AppendLine($"Revisión:\n  {currentFecha} → {newFecha}");
        sb.AppendLine();
        Append(sb, diff.MapData);
        Append(sb, diff.FightPlaces);
        Append(sb, diff.Width);
        Append(sb, diff.Height);
        Append(sb, diff.Background);
        Append(sb, diff.Music);
        Append(sb, diff.Ambiance);
        Append(sb, diff.Outdoor);
        Append(sb, diff.Capabilities);
        Append(sb, diff.WorldX);
        Append(sb, diff.WorldY);
        sb.AppendLine();
        sb.AppendLine("No se modificarán:");
        sb.AppendLine("  key, mobs, subArea, maxGrupoMobs, maxMobsPorGrupo,");
        sb.AppendLine("  minNivelGrupoMob, maxNivelGrupoMob, maxMercantes,");
        sb.AppendLine("  maxPeleas, minMobsPorGrupo");
        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, FieldDiff f)
    {
        if (f.Label is "MapData" or "Posiciones combate")
        {
            sb.AppendLine($"{f.Label}:");
            sb.AppendLine($"  {f.After}");
        }
        else if (f.Changed)
            sb.AppendLine($"{f.Label}:\n  {f.Before} → {f.After}");
        else
            sb.AppendLine($"{f.Label}:\n  {f.Before} (sin cambios)");
        sb.AppendLine();
    }

    private void Publish_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
