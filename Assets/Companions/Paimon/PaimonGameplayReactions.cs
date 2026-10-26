using UnityEngine;

// Listens to accepted damage and successful grants; threat entry is edge-triggered with hysteresis.
[DisallowMultipleComponent]
public sealed class PaimonGameplayReactions : MonoBehaviour
{
 PaimonCompanion companion;Health heroHealth;float scanAt,rebindAt,threatLeftAt=-100,lastAlert=-100,lastHurt=-100;bool threatLatched;
 Health[] enemies=System.Array.Empty<Health>();float refreshAt;
 bool pendingAlert,pendingJoy,pendingHurt;float alertUntil,joyUntil,hurtUntil;
 readonly RaycastHit[] sight=new RaycastHit[24];
 public int AlertReactions {get;private set;}public int JoyReactions {get;private set;}public int HurtReactions {get;private set;}
 public bool ThreatLatched => threatLatched;
 void Awake(){companion=GetComponent<PaimonCompanion>();}
 void OnEnable(){rebindAt=0;}
 void OnDisable(){if(heroHealth)heroHealth.Damaged-=Damaged;heroHealth=null;pendingAlert=pendingJoy=pendingHurt=false;threatLatched=false;}
 void Bind(){var next=companion.target?companion.target.GetComponent<Health>():null;if(next==heroHealth)return;if(heroHealth)heroHealth.Damaged-=Damaged;heroHealth=next;if(heroHealth)heroHealth.Damaged+=Damaged;}
 void Damaged(Health target,DamageInfo info){float now=Time.unscaledTime;if(now-lastHurt<8f||!Available)return;lastHurt=now;if(companion.ReactionPlaying&&companion.IdleAction==PaimonMotion.Action.Concern)return;pendingHurt=true;hurtUntil=now+5;}
 bool Available=>companion&&companion.isActiveAndEnabled&&companion.Equipped&&companion.Visible&&companion.target&&companion.target.gameObject.activeInHierarchy;
 public static void ItemObtained(Transform owner=null){foreach(var n in FindObjectsByType<NiaolongCompanion>(FindObjectsSortMode.None))if(n.Equipped&&(!owner||n.owner==owner))n.Play(NiaolongCompanion.Gesture.HappyBounce);foreach(var a in FindObjectsByType<AmberwingCompanion>(FindObjectsSortMode.None))if(a.Equipped&&(!owner||a.owner==owner))a.Play(AmberwingCompanion.Gesture.HappyHop);foreach(var reaction in FindObjectsByType<PaimonGameplayReactions>(FindObjectsSortMode.None))if(!owner||reaction.companion.target==owner)reaction.ReceiveItem();}
 void ReceiveItem(){if(!Available)return;if(companion.ReactionPlaying&&companion.IdleAction==PaimonMotion.Action.Joy)return;pendingJoy=true;joyUntil=Time.unscaledTime+20;}
 void Update()
 {
  float now=Time.unscaledTime;if(now>=rebindAt){rebindAt=now+.25f;Bind();}
  if(!Available){pendingAlert=pendingJoy=pendingHurt=false;threatLatched=false;return;}
  if(now>=scanAt){scanAt=now+.2f;ScanThreats(now);}
  if(pendingHurt&&now>hurtUntil)pendingHurt=false;if(pendingAlert&&now>alertUntil)pendingAlert=false;if(pendingJoy&&now>joyUntil)pendingJoy=false;
  int priority=pendingHurt?3:pendingAlert?2:pendingJoy?1:0;if(priority==0)return;
  int current=companion.ReactionPlaying?(companion.IdleAction==PaimonMotion.Action.Concern?3:companion.IdleAction==PaimonMotion.Action.Surprise?2:1):0;if(current>=priority)return;
  var action=priority==3?PaimonMotion.Action.Concern:priority==2?PaimonMotion.Action.Surprise:PaimonMotion.Action.Joy;
  if(!companion.TryPlayReaction(action))return;
  if(priority==3){pendingHurt=false;pendingAlert=false;HurtReactions++;}else if(priority==2){pendingAlert=false;AlertReactions++;}else{pendingJoy=false;JoyReactions++;}
 }
 public void ScanThreats(float now)
 {
  if(now>=refreshAt){refreshAt=now+1;enemies=FindObjectsByType<Health>(FindObjectsSortMode.None);}
  bool close=false,near=false;
  foreach(var health in enemies){if(!health||!health.isActiveAndEnabled||health.IsDead||health.Team!=HealthTeam.Enemy||health.gameObject.scene!=companion.target.gameObject.scene)continue;
   var knight=health.GetComponent<MountedKnightBrain>();var scout=health.GetComponent<StoneScoutRoadsideAI>();float range;
   if(knight&&knight.isActiveAndEnabled){if(knight.territory&&!knight.territory.Contains(companion.target.position))continue;range=knight.attackRange;}
   else if(scout&&scout.isActiveAndEnabled)range=scout.AttackRange;else continue;
   Vector3 offset=health.transform.position-companion.target.position;if(Mathf.Abs(offset.y)>2.5f)continue;float distance=Vector3.ProjectOnPlane(offset,Vector3.up).magnitude;
   if(distance>range+.7f)continue;if(!CanSee(health))continue;near=true;if(distance<=range)close=true;
  }
  if(near)threatLeftAt=now;else if(now-threatLeftAt>2)threatLatched=false;
  if(close&&!threatLatched){threatLatched=true;if(now-lastAlert>=8){lastAlert=now;pendingAlert=true;alertUntil=now+3;}}
 }
 bool CanSee(Health enemy){Vector3 from=companion.target.position+Vector3.up,to=enemy.transform.position+Vector3.up;Vector3 direction=to-from;int count=Physics.RaycastNonAlloc(from,direction.normalized,sight,direction.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);for(int i=0;i<count;i++){var hit=sight[i].transform;if(hit.IsChildOf(companion.target)||hit.IsChildOf(enemy.transform)||hit.IsChildOf(transform))continue;return false;}return count<sight.Length;}
}
