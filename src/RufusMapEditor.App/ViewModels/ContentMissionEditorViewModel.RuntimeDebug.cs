using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.App.ViewModels;

/// <summary>MISSIONS.PANEL.RUNTIME.DEBUG.1C — runtime panel diagnostics (single text block).</summary>
public sealed record MissionPanelRuntimeDebugContext(
    int? SelectedActionAccion,
    Guid? TargetMissionDraftId,
    bool HasLinkedMission);

public sealed partial class ContentMissionEditorViewModel
{
    private const string DebugActiveHeader = "=== DEBUG ACTIVO ===";

    private Func<MissionPanelRuntimeDebugContext>? _runtimeDebugContext;

    public int ViewModelInstanceId => RuntimeHelpers.GetHashCode(this);

    public string DebugBindingProbe => "PROBE_OK";

    public string DebugDataContextType => GetType().FullName ?? "NULL";

    public void ConfigureRuntimeDebugContext(Func<MissionPanelRuntimeDebugContext>? provider)
    {
        _runtimeDebugContext = provider;
        RefreshDebugMissionState();
    }

    public void RefreshDebugMissionState() =>
        OnPropertyChanged(nameof(DebugMissionStateText));

    /// <summary>Backward-compatible alias for existing refresh call sites.</summary>
    public void RefreshRuntimeDebugBindings() => RefreshDebugMissionState();

    public string DebugMissionStateText
    {
        get
        {
            try
            {
                var body = BuildDebugMissionStateBody();
                if (string.IsNullOrWhiteSpace(body))
                    body = "(sin datos adicionales)";
                return DebugActiveHeader + Environment.NewLine + body;
            }
            catch (Exception ex)
            {
                return DebugActiveHeader + Environment.NewLine + "BUILD_ERROR: " + ex.Message;
            }
        }
    }

    private MissionPanelRuntimeDebugContext? ReadDebugContext() => _runtimeDebugContext?.Invoke();

    private string BuildDebugMissionStateBody()
    {
        var ctx = ReadDebugContext();
        var roleMission = ResolveDialogRoleMission();
        var sb = new StringBuilder();

        AppendLine(sb, "ViewModelInstanceId", ViewModelInstanceId.ToString(CultureInfo.InvariantCulture));
        AppendLine(sb, "DebugDataContextType", DebugDataContextType);
        AppendLine(sb, "accion", FmtNullableInt(ctx?.SelectedActionAccion));
        AppendLine(sb, "TargetMissionDraftId", FmtNullableGuid(ctx?.TargetMissionDraftId));
        AppendLine(sb, "InlineLinkedMissionDraftId", FmtNullableGuid(_inlineLinkedMissionDraftId));
        AppendLine(sb, "CurrentLinkedMission", FmtNullableGuid(CurrentLinkedMission?.DraftId));
        AppendLine(sb, "Missions.Selected", FmtNullableGuid(Selected?.Model.DraftId));
        AppendLine(sb, "ResolveDialogRoleMission", FmtNullableGuid(roleMission?.DraftId));
        sb.AppendLine();
        AppendLine(sb, "OwnerNpc", FmtNullableInt(OwnerNpcId));
        AppendLine(sb, "StartNpcId", FmtNullableInt(StartNpcIdValue));
        sb.AppendLine();
        AppendLine(sb, "PregDar", FmtNullableInt(roleMission?.PregDarPreguntaId));
        AppendLine(sb, "PregInc", FmtNullableInt(roleMission?.PregIncompletaPreguntaId));
        AppendLine(sb, "PregComp", FmtNullableInt(roleMission?.PregCompletadaPreguntaId));
        sb.AppendLine();
        AppendLine(sb, "PregDarQuestionIdValue", FmtNullableInt(PregDarQuestionIdValue));
        AppendLine(sb, "PregIncQuestionIdValue", FmtNullableInt(PregIncQuestionIdValue));
        AppendLine(sb, "PregCompQuestionIdValue", FmtNullableInt(PregCompQuestionIdValue));
        sb.AppendLine();
        AppendLine(sb, "PregDarRoleDisplay", FmtDisplay(PregDarRoleDisplay));
        AppendLine(sb, "PregIncRoleDisplay", FmtDisplay(PregIncRoleDisplay));
        AppendLine(sb, "PregCompRoleDisplay", FmtDisplay(PregCompRoleDisplay));
        sb.AppendLine();
        AppendLine(sb, "PregDarPreview", FmtDisplay(PregDarPreview));
        AppendLine(sb, "PregIncPreview", FmtDisplay(PregIncPreview));
        AppendLine(sb, "PregCompPreview", FmtDisplay(PregCompPreview));
        sb.AppendLine();
        AppendLine(sb, "QuestionChoices.Count",
            QuestionChoices.Count.ToString(CultureInfo.InvariantCulture));
        AppendLine(sb, "QuestionChoices IDs",
            QuestionChoices.Count == 0
                ? "NULL"
                : string.Join(", ", QuestionChoices.Select(q => q.Id.ToString(CultureInfo.InvariantCulture))));
        sb.AppendLine();
        AppendLine(sb, "ShowCompleteMissingDialogRoles", FmtBool(ShowCompleteMissingDialogRoles));
        AppendLine(sb, "ActionInlineMode", FmtBool(ActionInlineMode));
        AppendLine(sb, "HasLinkedMission", FmtBool(ctx?.HasLinkedMission ?? CurrentLinkedMission is not null));

        return sb.ToString().TrimEnd();
    }

    private static void AppendLine(StringBuilder sb, string label, string value) =>
        sb.Append(label).Append(": ").AppendLine(string.IsNullOrWhiteSpace(value) ? "NULL" : value);

    private static string FmtNullableGuid(Guid? value) =>
        value is Guid g ? g.ToString() : "NULL";

    private static string FmtNullableInt(int? value) =>
        value is int i ? i.ToString(CultureInfo.InvariantCulture) : "NULL";

    private static string FmtBool(bool value) => value ? "True" : "False";

    private static string FmtDisplay(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "NULL" : value;
}
