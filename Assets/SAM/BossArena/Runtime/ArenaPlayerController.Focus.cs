using UnityEngine;
using KingOfGraphic.Hero;

namespace SamProduction.BossArena
{
    public sealed partial class ArenaPlayerController
    {
        readonly HeroFocusSequence focus = new HeroFocusSequence();
        ArenaFighter focusEnemy; float focusStuck;
        public bool FocusActive => focus.Active;
        public HeroFocusPhase FocusPhase => focus.Phase;
        public float FocusEnemyScale => FocusActive ? HeroFocusSequence.EnemyScale : 1;
        public int FocusStrikeCount => focus.Strikes;
        public Vector3 FocusOrigin => focus.Origin;
        public float FocusRemaining => Mathf.Max(0,HeroFocusSequence.MaximumSeconds-focus.Age);
        public string FocusHint => focus.Phase==HeroFocusPhase.Approach?"LINK TIME  |  CLOSING IN":
            focus.Phase==HeroFocusPhase.Return?"LINK TIME  |  RETURNING":
            "LINK TIME  |  HOLD LMB TO ATTACK  "+focus.Strikes+"/"+HeroFocusSequence.MaximumStrikes;
        bool Responsive => Hero && Hero.library.responsiveSwordCombat;
        void BeginFocus(ArenaFighter enemy)
        {
            if(!Responsive||FocusActive||!enemy||enemy.Dead||enemy.team==fighter.team)return;
            focus.Begin(transform.position);focusEnemy=enemy;focusStuck=0;
            var fx=GetComponent<HeroFocusEffects>();if(!fx)fx=gameObject.AddComponent<HeroFocusEffects>();fx.Bind(Hero,()=>focus.Phase,()=>focus.Strikes,()=>focusEnemy?focusEnemy.transform:null,()=>focus.Origin);
            heroCombat.Cancel();heroBlade.Reset();attackTime=dodgeTime=-1;dodgeState.Cancel();
            perfectCounterTime=0;ClearCounterRequest();guard?.SuspendGuard();velocity=Vector3.zero;
            SwordDrawn=true;heroStowTime=-1;swordIdleRemaining=SwordIdleSeconds;fighter.ClearKnockback();fighter.Invulnerable=true;
        }
        void CancelFocus(){focus.Cancel();focusEnemy=null;focusStuck=0;if(fighter)fighter.Invulnerable=false;}
        float FocusStopDistance()
        {
            var enemy=focusEnemy?focusEnemy.GetComponent<CharacterController>():null;
            return capsule.radius+(enemy?enemy.radius:.65f)+.04f;
        }
        void BeginFocusReturn()
        {
            focus.Return(transform.position);focusStuck=0;heroCombat.Cancel();heroBlade.Reset();attackTime=-1;velocity=Vector3.zero;
        }
        // Returns true when this sequence exclusively owns displacement and pose.
        bool TickFocus(ref ArenaCommand command,float delta,ArenaSession arena)
        {
            if(!FocusActive)return false;
            focus.Advance(delta,transform.position);focus.ObserveStrike(heroCombat.Active,heroCombat.Sequence);
            if(focus.ShouldReturn(focusEnemy&&!focusEnemy.Dead,heroCombat.Active))BeginFocusReturn();
            command.move=Vector2.zero;command.dodge=command.guardHeld=command.guardPressed=false;command.author=default;command.run=false;
            if(!focus.Travelling){
                if(focusEnemy&&(!heroCombat.Active||heroCombat.Time<heroCombat.Open))HeroCombatTravel.Face(transform,focusEnemy.transform.position,delta,heroCombat.Active&&heroCombat.Time<heroCombat.Open);
                if(focusEnemy&&(!heroCombat.Active||heroCombat.Time<heroCombat.Open)){
                    Vector3 to=Vector3.ProjectOnPlane(focusEnemy.transform.position-transform.position,Vector3.up);float gap=to.magnitude-FocusStopDistance();
                    Vector3 followStep=to.normalized*Mathf.Min(Mathf.Max(0,gap),8*delta);
                    if(gap>.04f&&gap<2&&HeroCombatTravel.ClearSight(transform,focusEnemy.transform,transform.position+Vector3.up,focusEnemy.transform.position+Vector3.up)&&HeroCombatTravel.SupportedStep(capsule,followStep,focusEnemy.transform))capsule.Move(followStep+Vector3.down*2*delta);
                }
                command.attack=focus.AttackInput(command.attackHeld,command.attack);command.attackHeld=command.attackReleased=false;
                return false;
            }
            Vector3 step=Vector3.zero;
            if(focus.Phase==HeroFocusPhase.Approach&&focusEnemy){
                Vector3 to=Vector3.ProjectOnPlane(focusEnemy.transform.position-transform.position,Vector3.up);
                HeroCombatTravel.Face(transform,focusEnemy.transform.position,delta,true);
                float gap=to.magnitude-FocusStopDistance();
                if(gap<=.06f){focus.Arrive();focusStuck=0;return PoseFocusTravel(Vector3.zero,delta);}
                if(Mathf.Abs(focusEnemy.transform.position.y-transform.position.y)>1.6f||
                    !HeroCombatTravel.ClearSight(transform,focusEnemy.transform,transform.position+Vector3.up,focusEnemy.transform.position+Vector3.up))BeginFocusReturn();
                else step=to.normalized*Mathf.Min(gap,20*delta);
            }
            if(focus.Phase==HeroFocusPhase.Return)step=focus.ReturnStep(transform.position,delta);
            Vector3 before=transform.position;
            if(step.sqrMagnitude>.000001f&&HeroCombatTravel.SupportedStep(capsule,step,focusEnemy?focusEnemy.transform:null))capsule.Move(step+Vector3.down*2*delta);
            float moved=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
            focusStuck=step.magnitude>.001f&&moved<step.magnitude*.1f?focusStuck+delta:0;
            if(focus.Phase==HeroFocusPhase.Approach&&focusStuck>.14f)BeginFocusReturn();
            if(focus.Phase==HeroFocusPhase.Return&&(focus.Returned(transform.position)||focus.PhaseAge>1.5f||focusStuck>.22f)){
                arena.Record("focus_returned","link_time",fighter,0,transform.position);CancelFocus();
                velocity=PlanarVelocity=Vector3.zero;Speed=0;GetComponent<ArenaCurrentCharacter>()?.ResetHeroPresentation();return true;
            }
            return PoseFocusTravel(transform.position-before,delta);
        }
        bool PoseFocusTravel(Vector3 displacement,float delta)
        {
            if(view)view.TickLook(Vector2.zero,false);
            fighter.Invulnerable=true;PlanarVelocity=Vector3.ProjectOnPlane(displacement,Vector3.up)/Mathf.Max(.0001f,delta);Speed=PlanarVelocity.magnitude;
            if(Hero)GetComponent<ArenaCurrentCharacter>().PoseHero(delta,transform.InverseTransformDirection(displacement/Mathf.Max(.0001f,delta)),false,false,capsule.isGrounded,false,false);
            TickSwordPresentation(delta);return true;
        }
        void AimArenaAttack(ArenaSession arena,bool immediate)
        {
            var enemy=FocusActive?focusEnemy:arena&&arena.boss?arena.boss.fighter:null;
            if(!enemy||enemy.Dead)return;
            Vector3 to=enemy.transform.position-transform.position;
            if(Vector3.ProjectOnPlane(to,Vector3.up).sqrMagnitude>25||Mathf.Abs(to.y)>2.5f||
                !HeroCombatTravel.ClearSight(transform,enemy.transform,transform.position+Vector3.up,enemy.transform.position+Vector3.up))return;
            HeroCombatTravel.Face(transform,enemy.transform.position,0,immediate);
            attackStartRotation=transform.rotation;Vector3 flat=Vector3.ProjectOnPlane(to,Vector3.up);
            if(flat.sqrMagnitude>.001f)attackTargetRotation=Quaternion.LookRotation(flat);
        }
    }
}
