using RufusMapEditor.LegacyCompatibility.Content;

namespace RufusMapEditor.LegacyCompatibility.Tests.Content;

public sealed class MissionPanelStateTests
{
  private static bool ShowCompleteMissingDialogRoles(MissionDraft mission) =>
      mission.PregIncompletaPreguntaId is null or <= 0
      || mission.PregCompletadaPreguntaId is null or <= 0;

  [Fact]
  public void Test_A_panel_uses_linked_mission_when_selected_null()
  {
    var ws = new ContentDraftWorkspace();
    var npc = ws.Npcs.CreateNew();
    var qDar = ws.Dialogs.CreateQuestion(npc.Id);
    var qInc = ws.Dialogs.CreateQuestion(npc.Id);
    var qComp = ws.Dialogs.CreateQuestion(npc.Id);

    var missionA = ws.Missions.CreateMission();
    missionA.StartNpcId = npc.Id;
    missionA.PregDarPreguntaId = qDar.Id;
    missionA.PregIncompletaPreguntaId = qInc.Id;
    missionA.PregCompletadaPreguntaId = qComp.Id;

    var action = new DialogActionDraft
    {
      Accion = DialogActionCodes.StartQuest,
      TargetMissionDraftId = missionA.DraftId,
    };

    var linked = StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(action, ws.Missions);
    var panel = MissionDialogRolesUi.ResolvePanelMissionDraft(true, linked, null);

    Assert.Same(missionA, linked);
    Assert.Same(missionA, panel);
    Assert.Equal(qDar.Id, panel!.PregDarPreguntaId);
    Assert.Equal(qInc.Id, panel.PregIncompletaPreguntaId);
    Assert.Equal(qComp.Id, panel.PregCompletadaPreguntaId);
  }

  [Fact]
  public void Test_B_panel_ignores_legacy_selected_when_link_valid()
  {
    var ws = new ContentDraftWorkspace();
    var npc = ws.Npcs.CreateNew();
    var qDar = ws.Dialogs.CreateQuestion(npc.Id);

    var missionA = ws.Missions.CreateMission();
    missionA.StartNpcId = npc.Id;
    missionA.PregDarPreguntaId = qDar.Id;
    missionA.PregIncompletaPreguntaId = 20026;
    missionA.PregCompletadaPreguntaId = 20027;

    var legacyB = ws.Missions.CreateMission();
    legacyB.Nombre = "legacy";

    var action = new DialogActionDraft
    {
      Accion = DialogActionCodes.StartQuest,
      TargetMissionDraftId = missionA.DraftId,
    };

    var linked = StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(action, ws.Missions);
    var panel = MissionDialogRolesUi.ResolvePanelMissionDraft(true, linked, legacyB);

    Assert.Same(missionA, panel);
    Assert.NotSame(legacyB, panel);
  }

  [Fact]
  public void Test_C_changing_target_mission_draft_id_switches_panel_mission()
  {
    var ws = new ContentDraftWorkspace();
    var npc = ws.Npcs.CreateNew();

    var missionA = ws.Missions.CreateMission();
    missionA.StartNpcId = npc.Id;
    missionA.PregDarPreguntaId = 20025;

    var missionC = ws.Missions.CreateMission();
    missionC.StartNpcId = npc.Id;
    missionC.PregDarPreguntaId = 20030;

    var action = new DialogActionDraft
    {
      Accion = DialogActionCodes.StartQuest,
      TargetMissionDraftId = missionA.DraftId,
    };

    var panelA = MissionDialogRolesUi.ResolvePanelMissionDraft(
        true,
        StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(action, ws.Missions),
        null);
    Assert.Same(missionA, panelA);

    action.TargetMissionDraftId = missionC.DraftId;
    var panelC = MissionDialogRolesUi.ResolvePanelMissionDraft(
        true,
        StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(action, ws.Missions),
        missionA);
    Assert.Same(missionC, panelC);
    Assert.Equal(20030, panelC!.PregDarPreguntaId);
  }

  [Fact]
  public void Test_D_complete_missing_hidden_when_all_roles_present()
  {
    var ws = new ContentDraftWorkspace();
    var npc = ws.Npcs.CreateNew();
    var mission = ws.Missions.CreateMission();
    mission.StartNpcId = npc.Id;
    mission.PregDarPreguntaId = 20025;
    mission.PregIncompletaPreguntaId = 20026;
    mission.PregCompletadaPreguntaId = 20027;

    Assert.False(ShowCompleteMissingDialogRoles(mission));
  }

  [Fact]
  public void Resolve_linked_by_target_draft_id_does_not_require_start_quest_action_flag()
  {
    var ws = new ContentDraftWorkspace();
    var m = ws.Missions.CreateMission();
    var action = new DialogActionDraft { TargetMissionDraftId = m.DraftId };

    Assert.Same(m, StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(action, ws.Missions));
    Assert.Null(StartQuestLinkUi.ResolveLinkedMission(action, ws.Missions));
  }

  [Fact]
  public void Panel_and_canvas_source_of_truth_share_draft_id()
  {
    var ws = new ContentDraftWorkspace();
    var npc = ws.Npcs.CreateNew();
    var mission = ws.Missions.CreateMission();
    mission.StartNpcId = npc.Id;
    mission.PregDarPreguntaId = 20025;

    var action = new DialogActionDraft
    {
      Accion = DialogActionCodes.StartQuest,
      TargetMissionDraftId = mission.DraftId,
    };

    var panelLinked = StartQuestLinkUi.ResolveLinkedMissionByTargetDraftId(action, ws.Missions);
    var canvasLinked = StartQuestLinkUi.ResolveMissionSourceOfTruth(action, ws.Missions, npc.Id);

    Assert.Same(mission, panelLinked);
    Assert.Same(mission, canvasLinked);
    Assert.Equal(mission.DraftId, panelLinked!.DraftId);
    Assert.Equal(mission.DraftId, canvasLinked!.DraftId);
  }

  [Fact]
  public void Inline_mission_nombre_reads_linked_draft_not_legacy_selected()
  {
    var ws = new ContentDraftWorkspace();
    var npc = ws.Npcs.CreateNew();
    var missionLinked = ws.Missions.CreateMission();
    missionLinked.StartNpcId = npc.Id;
    missionLinked.Nombre = "Misión vinculada";
    missionLinked.PregDarPreguntaId = 20025;
    missionLinked.PregIncompletaPreguntaId = 20026;
    missionLinked.PregCompletadaPreguntaId = 20027;

    var legacy = ws.Missions.CreateMission();
    legacy.Nombre = "Legacy";

    var inline = MissionDialogRolesUi.ResolvePanelMissionDraft(true, missionLinked, legacy);
    Assert.Same(missionLinked, inline);
    Assert.Equal("Misión vinculada", inline!.Nombre);
    Assert.Equal(20026, inline.PregIncompletaPreguntaId);
    Assert.Equal(20027, inline.PregCompletadaPreguntaId);
  }
}
