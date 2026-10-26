using System;using UnityEngine;
// Own scene adapter: no reward inventory writes, no old-world component replacement.
[DefaultExecutionOrder(900)]
[DisallowMultipleComponent]
public sealed class MainWorldInvestigations:MonoBehaviour{
 public BaselineFlow flow;
 public BaselineWorld world;
 public FlameCollectionGame surveyGame;
 public ConstellationGame relayGame;
 [Tooltip("Only the superseded Objective interaction components for task0/2; never chest/rune/task-giver components.")]
 public BaselineInteraction surveyLegacyObjective,relayLegacyObjective;
 [TextArea]public string surveyRecord="Survey recovered: the boundary is weakening. The surveyors recorded its changes so both civilizations could respond together.";
 [TextArea]public string islandRecord="The island inscription records a shared guardianship. Both civilizations maintained the boundary together; the seven-star relay's guardian never left its post.";
 [TextArea]public string relayRecord="The seven-star relay is aligned. The guardian has always remained here, maintaining the link between both civilizations as the boundary weakened.";
 bool bound,committing;
 public bool SurveyKnown=>flow&&world&&(flow.Progress.worldSurveyComplete||world.completed[0]);
 public bool IslandKnown=>flow&&world&&(flow.Progress.worldInscriptionRead||world.visited[1]||world.completed[1]);
 public bool RelayKnown=>flow&&world&&(flow.Progress.worldRelayComplete||world.completed[2]);
 public bool CanAct=>flow&&world&&flow.Started&&isActiveAndEnabled&&gameObject.activeInHierarchy&&Time.timeScale>0&&!flow.BlockWorldInput&&!flow.MenuOpen&&!flow.MapOpen&&!world.JournalOpen&&world.LastJournalClosedFrame!=Time.frameCount&&!(SideWorldTravel.Instance&&(SideWorldTravel.Instance.IsAway||SideWorldTravel.Instance.Busy));
 void OnEnable(){Bind();}
 void Start(){Bind();Restore();}
 void Bind(){
  if(!flow)flow=GetComponent<BaselineFlow>();if(!world)world=GetComponent<BaselineWorld>();
  if(!flow||!world||flow.gameObject.scene!=gameObject.scene||world.gameObject.scene!=gameObject.scene)return;
  if(!bound){flow.WorldEncountersReset+=Restore;world.StateChanged+=OnWorldChanged;bound=true;}
  if(surveyGame&&surveyGame.gameObject.scene==gameObject.scene&&surveyGame.chest){surveyGame.chest.gameId="world.survey";surveyGame.chest.TryCommitClaim=CommitSurvey;surveyGame.chest.completionMessage=surveyRecord;surveyGame.chest.revealMessage="Survey recovered. Open the record chest to confirm the investigation.";}
  if(relayGame&&relayGame.gameObject.scene==gameObject.scene&&relayGame.chest){relayGame.chest.gameId="world.relay";relayGame.chest.TryCommitClaim=CommitRelay;relayGame.chest.completionMessage=relayRecord;relayGame.chest.revealMessage="The relay answers. Open the record chest to confirm its seven-star alignment.";}
  SuppressLegacyObjective(surveyLegacyObjective,0);SuppressLegacyObjective(relayLegacyObjective,2);
  var player=MinigamePlayer.Find(gameObject.scene);if(player)player.HostGate=()=>CanAct;
 }
 void SuppressLegacyObjective(BaselineInteraction item,int index){if(item&&item.gameObject.scene==gameObject.scene&&item.action==BaselineInteraction.ActionKind.Objective&&item.index==index)item.enabled=false;}
 public void Restore(){
  if(!flow||!world)return;Bind();
  bool oldSurvey=flow.Progress.worldSurveyComplete,oldIsland=flow.Progress.worldInscriptionRead,oldRelay=flow.Progress.worldRelayComplete;
  if(flow.Started){
   flow.Progress.worldSurveyComplete|=world.completed[0];
   flow.Progress.worldInscriptionRead|=world.visited[1]||world.completed[1];
   flow.Progress.worldRelayComplete|=world.completed[2];
   if(oldSurvey!=flow.Progress.worldSurveyComplete||oldIsland!=flow.Progress.worldInscriptionRead||oldRelay!=flow.Progress.worldRelayComplete){
    bool saved=false;try{saved=flow.SaveNow();}catch(Exception error){Debug.LogException(error,this);}
    if(!saved){flow.Progress.worldSurveyComplete=oldSurvey;flow.Progress.worldInscriptionRead=oldIsland;flow.Progress.worldRelayComplete=oldRelay;}
   }
  }
  // Known old completion remains authoritative; no forced repetition and no old flags reset.
  if(surveyGame&&surveyGame.gameObject.scene==gameObject.scene)surveyGame.RestoreRun(SurveyKnown);
  if(relayGame&&relayGame.gameObject.scene==gameObject.scene)relayGame.RestoreRun(RelayKnown);
 }
 bool CommitSurvey(MinigameRewardChest chest)=>TryCommitChapter(0,chest);
 bool CommitRelay(MinigameRewardChest chest)=>TryCommitChapter(2,chest);
 public bool TryCommitChapter(int index,MinigameRewardChest chest){
  if(committing||!CanAct||(index!=0&&index!=2)||!chest||chest.gameObject.scene!=gameObject.scene)return false;
  if(index==0&&(!surveyGame||surveyGame.chest!=chest||!surveyGame.State.Solved))return false;
  if(index==2&&(!relayGame||relayGame.chest!=chest||!relayGame.State.Solved))return false;
  bool oldFlag=index==0?flow.Progress.worldSurveyComplete:flow.Progress.worldRelayComplete;
  bool a=world.accepted[index],v=world.visited[index],c=world.completed[index];
  bool pa=flow.Progress.accepted[index],pv=flow.Progress.visited[index],pc=flow.Progress.completed[index];
  committing=true;
  try{
   if(index==0)flow.Progress.worldSurveyComplete=true;else flow.Progress.worldRelayComplete=true;
   world.accepted[index]=world.visited[index]=world.completed[index]=true;
   bool saved=false;try{saved=flow.SaveNow();}catch(Exception error){Debug.LogException(error,this);}
   if(!saved){
    if(index==0)flow.Progress.worldSurveyComplete=oldFlag;else flow.Progress.worldRelayComplete=oldFlag;
    world.accepted[index]=a;world.visited[index]=v;world.completed[index]=c;
    flow.Progress.accepted[index]=pa;flow.Progress.visited[index]=pv;flow.Progress.completed[index]=pc;
    world.ShowNotice("Investigation not saved. The record chest remains available; retry saving.");return false;
   }
   // A second tracking notification is safe only AFTER the saved chapter transaction.
   if(world.Tracked==index)world.Track(-1);
   world.ShowNotice(index==0?surveyRecord:relayRecord);return true;
  }finally{committing=false;}
 }
 void OnWorldChanged(){
  if(committing||!CanAct||flow.Progress.worldInscriptionRead||(!world.visited[1]&&!world.completed[1]))return;
  committing=true;try{
   flow.Progress.worldInscriptionRead=true;
   bool saved=false;try{saved=flow.SaveNow();}catch(Exception error){Debug.LogException(error,this);}
   if(!saved){flow.Progress.worldInscriptionRead=false;world.ShowNotice("The inscription was read, but its chapter record was not saved. Retry Save progress.");return;}
   world.ShowNotice(islandRecord);
  }finally{committing=false;}
 }
 // If inscription persistence failed during its dialogue, a later explicit save/UI state
 // event can retry; no frame-by-frame autosave loop or additional E handler is introduced.
 public bool RetryInscriptionRecord(){if(!CanAct)return false;OnWorldChanged();return flow.Progress.worldInscriptionRead;}
 void OnDisable(){
  if(bound){if(flow)flow.WorldEncountersReset-=Restore;if(world)world.StateChanged-=OnWorldChanged;bound=false;}
  if(surveyGame&&surveyGame.chest)surveyGame.chest.TryCommitClaim=null;
  if(relayGame&&relayGame.chest)relayGame.chest.TryCommitClaim=null;
  var player=MinigamePlayer.Existing;if(player&&player.gameObject.scene==gameObject.scene)player.HostGate=null;
 }
}
