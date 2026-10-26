using System.Collections.Generic;
using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    [RequireComponent(typeof(Health),typeof(MistGardenBossFeedback),typeof(NonClimbableSurface))]
    [RequireComponent(typeof(MechBodyCollision))]
    public sealed class MechSentinelBattle:MonoBehaviour
    {
        public PlayerMovement player;
        public GameObject arenaBarrier,impactEffect,flashEffect,chargeEffect,projectileEffect,playerHitEffect;
        public Material telegraphMaterial;
        public float engageRadius=8,walkSpeed=1.65f;
        public float meleeStartRange=3.6f;
        public Transform arenaCenter;public float arenaMoveRadius=13.2f;
        public bool Engaged{get;private set;}
        public float ImpactTime=>skill==0?1:1.2f;
        public bool Committed=>Engaged&&state==State.Attack&&attackElapsed<ImpactTime+.12f;
        public bool Shooting=>state==State.Attack&&skill==1;
        public bool Slamming=>state==State.Attack&&skill==0;
        public bool showDebugHud=false;
        public float AttackElapsed=>attackElapsed;
        public event System.Action<Vector3> GroundImpact;
        public string Status{get;private set;}="Approach within 8m to engage";
        enum State{Idle,Chase,Attack,Ended}
        State state;Health health,playerHealth;MistGardenBossFeedback feedback;
        Vector3 home;Quaternion homeRotation;
        float attackElapsed,reactionRemaining;int turn,skill;bool strikeDone;
        public bool Reacting=>reactionRemaining>0;
        public bool NotifyHitReaction(float duration)
        {
            if(health.IsDead||Committed||GetComponent<MechReactiveShield>()?.Active==true)return false;
            reactionRemaining=Mathf.Clamp(duration,.12f,.45f);
            if(Engaged)state=State.Chase;
            return true;
        }
        GameObject telegraph,charge;Mesh warningMesh;
        readonly List<GameObject> transientEffects=new List<GameObject>();
        MaterialPropertyBlock warningProperties;
        public Vector3 ChestMuzzlePosition
        {
            get
            {
                var neck=feedback.animator.GetBoneTransform(HumanBodyBones.Neck);
                return neck?neck.position-transform.up*.55f+transform.forward*.85f:transform.TransformPoint(new Vector3(0,2.35f,.85f));
            }
        }
        public Vector3 MuzzlePosition=>state==State.Attack&&skill==2?ChestMuzzlePosition:(feedback.animator.GetBoneTransform(HumanBodyBones.RightHand).position+feedback.animator.GetBoneTransform(HumanBodyBones.LeftHand).position)*.5f+transform.forward*.15f;
        void Awake(){health=GetComponent<Health>();feedback=GetComponent<MistGardenBossFeedback>();home=transform.position;homeRotation=transform.rotation;if(!player)player=FindFirstObjectByType<PlayerMovement>();if(player)playerHealth=player.GetComponent<Health>();warningProperties=new MaterialPropertyBlock();ResetForLab();}
        public void BindPlayer(PlayerMovement target){player=target;playerHealth=target?target.GetComponent<Health>():null;}
        void Update()=>AdvanceCombat(Time.deltaTime);
        public void AdvanceCombat(float dt)
        {
            if(dt<=0||!playerHealth)return;
            if(health.IsDead||playerHealth.IsDead){EndBattle(health.IsDead?"Sentinel defeated / F5 restart":"Player down / F5 restart");return;}
            if(state==State.Ended)return;
            if(reactionRemaining>0){reactionRemaining=Mathf.Max(0,reactionRemaining-dt);Status="Hit reaction / stagger";return;}
            float distance=FlatDistance(transform.position,player.transform.position);
            if(!Engaged){var island=GetComponent<MechIslandGroundConstraint>();if(distance>engageRadius||Mathf.Abs(player.transform.position.y-transform.position.y)>2||(island&&!island.CanEngagePlayer(player.transform.position)))return;Engaged=true;state=State.Chase;if(arenaBarrier)arenaBarrier.SetActive(true);}
            if(state==State.Chase)
            {
                FacePlayer(dt);
                int next=turn%2;
                float range=meleeStartRange;
                if(distance>range)
                {
                    Status="Walking into range";
                    if(Vector3.Angle(transform.forward,FlatDirectionToPlayer())<40)
                    {
                        var p=transform.position+transform.forward*Mathf.Min(walkSpeed*dt,distance-range);
                        var origin=arenaCenter?arenaCenter.position:home;
                        var offset=Vector3.ProjectOnPlane(p-origin,Vector3.up);
                        var goal=origin+Vector3.ClampMagnitude(offset,arenaMoveRadius);goal.y=home.y;
                        GetComponent<MechBodyCollision>().Move(goal-transform.position);
                    }
                    PlayIfDifferent("Walk");
                }
                else if(Vector3.Angle(transform.forward,FlatDirectionToPlayer())<12)BeginSkill(next);
                else PlayIfDifferent("Idle");
                return;
            }
            attackElapsed+=dt;
            if(attackElapsed<ImpactTime-.22f)FacePlayer(dt);
            if(charge){charge.transform.position=MuzzlePosition;charge.transform.rotation=transform.rotation;}
            if(telegraph){warningProperties.SetFloat("_Strength",Mathf.Lerp(.08f,.32f,Mathf.Clamp01(attackElapsed)));telegraph.GetComponent<Renderer>().SetPropertyBlock(warningProperties);}
            if(!strikeDone&&attackElapsed>=ImpactTime){strikeDone=true;ClearTelegraph();ClearCharge();Slam();Status="Recovery / attack now";}
            if(attackElapsed>=2.15f){state=State.Chase;PlayIfDifferent("Idle");}
        }
        Vector3 FlatDirectionToPlayer(){var d=player.transform.position-transform.position;d.y=0;return d.normalized;}
        void FacePlayer(float dt){var d=FlatDirectionToPlayer();if(d.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(d),160*dt);}
        void BeginSkill(int next)
        {
            if(next<0||next>1)return;
            skill=next;turn++;attackElapsed=0;strikeDone=false;state=State.Attack;
            feedback.animator.CrossFadeInFixedTime(next==0?"SlamV2":"ShootV2",.14f);
            Status=next==0?"FIST SLAM / watch the raised fist":"OVERHEAD SLAM / dodge sideways";
        }
        void PlayIfDifferent(string name){var a=feedback.animator;if(a.GetCurrentAnimatorStateInfo(0).IsName(name)||(a.IsInTransition(0)&&a.GetNextAnimatorStateInfo(0).IsName(name)))return;a.CrossFadeInFixedTime(name,.15f);}
        void ShowTelegraph()
        {
            ClearTelegraph();if(!telegraphMaterial)return;
            telegraph=new GameObject("Amber soft slam sector");telegraph.transform.SetParent(transform,false);telegraph.transform.localPosition=Vector3.up*.04f;
            var filter=telegraph.AddComponent<MeshFilter>();telegraph.AddComponent<MeshRenderer>().sharedMaterial=telegraphMaterial;
            const int columns=64,rows=8;var vertices=new Vector3[(columns+1)*(rows+1)];var colors=new Color[vertices.Length];var triangles=new List<int>();
            for(int row=0;row<=rows;row++)for(int col=0;col<=columns;col++)
            {
                float r=row/(float)rows,angle=Mathf.Lerp(-58,58,col/(float)columns)*Mathf.Deg2Rad;int index=row*(columns+1)+col;vertices[index]=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*r*3.5f;float edge=Mathf.Min(col/(float)columns,1-col/(float)columns);colors[index]=new Color(1,1,1,Ease(0,.18f,r)*(1-Ease(.83f,1,r))*Ease(0,.08f,edge));
                if(row<rows&&col<columns){int n=index+columns+1;triangles.AddRange(new[]{index,n,index+1,index+1,n,n+1});}
            }
            warningMesh=new Mesh{name="Soft slam sector mesh"};warningMesh.vertices=vertices;warningMesh.colors=colors;warningMesh.triangles=triangles.ToArray();warningMesh.RecalculateBounds();filter.sharedMesh=warningMesh;
        }
        void ClearTelegraph(){if(telegraph)Destroy(telegraph);telegraph=null;if(warningMesh)Destroy(warningMesh);warningMesh=null;}
        void Slam(){var hand=feedback.animator.GetBoneTransform(HumanBodyBones.RightHand).position;var point=new Vector3(hand.x,home.y+.04f,hand.z);GroundImpact?.Invoke(point);SpawnEffect(impactEffect,point,1,3);var d=player.transform.position-transform.position;if(FlatDistance(transform.position,player.transform.position)<=3.8f&&Mathf.Abs(d.y)<1.1f&&Vector3.Dot(FlatDirectionToPlayer(),transform.forward)>=Mathf.Cos(58*Mathf.Deg2Rad))DamagePlayer(18,point);}
        void DamagePlayer(int damage,Vector3 source){Vector3 point=player.transform.position+Vector3.up*.9f;if(playerHealth.TryApplyDamage(new DamageInfo(damage,gameObject,point,point-transform.position)))SpawnEffect(playerHitEffect,point,.8f,2);}
        GameObject SpawnEffect(GameObject template,Vector3 point,float scale,float lifetime){if(!template)return null;var fx=Instantiate(template,point,transform.rotation);fx.name="Sentinel "+template.name;fx.transform.localScale*=scale;fx.SetActive(true);foreach(var ps in fx.GetComponentsInChildren<ParticleSystem>(true))if(ps.gameObject.activeInHierarchy)ps.Play(false);transientEffects.Add(fx);if(lifetime>0)Destroy(fx,lifetime);return fx;}
        void ClearCharge(){if(charge)Destroy(charge);charge=null;}
        void EndBattle(string message){if(state==State.Ended)return;Engaged=false;state=State.Ended;Status=message;ClearCharge();ClearTelegraph();GetComponent<MechReactiveShield>()?.ResetForLab();if(arenaBarrier)arenaBarrier.SetActive(false);if(!health.IsDead)PlayIfDifferent("Idle");}
        public void ResetForLab(){if(GetComponent<MechCorpseLifecycle>()?.Defeated==true)return;Engaged=false;state=State.Idle;attackElapsed=0;turn=0;ClearCharge();ClearTelegraph();foreach(var fx in transientEffects)if(fx){fx.SetActive(false);Destroy(fx);}transientEffects.Clear();transform.SetPositionAndRotation(home,homeRotation);GetComponent<MechReactiveShield>()?.ResetForLab();if(arenaBarrier)arenaBarrier.SetActive(false);Status="Approach within 8m to engage";}
        void OnDestroy(){ClearCharge();ClearTelegraph();}
        static float FlatDistance(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static float Ease(float a,float b,float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
        void OnGUI(){if(!showDebugHud||!playerHealth)return;GUI.Box(new Rect(16,115,610,65),"SENTINEL BATTLE / "+Status);GUI.Label(new Rect(30,143,580,24),"Player HP "+playerHealth.CurrentHealth+" / "+playerHealth.MaxHealth+"  | Ctrl dodge | Q guard | F5 restart");}
    }
}
