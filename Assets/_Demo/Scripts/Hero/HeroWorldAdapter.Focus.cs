using UnityEngine;

namespace KingOfGraphic.Hero
{
    public sealed partial class HeroWorldAdapter
    {
        readonly HeroFocusSequence focus = new HeroFocusSequence();
        Health focusEnemy;Vector3 focusLook;float focusStuck;
        public bool FocusSequenceActive => focus.Active;
        public bool FocusOwnsMovement => focus.Travelling;
        public HeroFocusPhase FocusPhase => focus.Phase;
        public bool FocusPromptReady => focus.Prompt;
        public int FocusStrikeCount => focus.Strikes;
        public Vector3 FocusOrigin => focus.Origin;
        public Health FocusEnemy => focusEnemy&&!focusEnemy.IsDead?focusEnemy:null;
        public string FocusHint => focus.Phase==HeroFocusPhase.Approach?"CLOSING IN":focus.Phase==HeroFocusPhase.Return?"RETURNING TO START":
            "HOLD LMB TO ATTACK  "+focus.Strikes+"/"+HeroFocusSequence.MaximumStrikes;
        public bool BeginFocusSequence(Health enemy)
        {
            if(!enemy||enemy.IsDead||enemy.Team!=HealthTeam.Enemy||dead||!capsule||FocusSequenceActive)return false;
            focus.Begin(transform.position);focusEnemy=enemy;focusLook=enemy.transform.position;focusStuck=0;
            var fx=GetComponent<HeroFocusEffects>();if(!fx)fx=gameObject.AddComponent<HeroFocusEffects>();fx.Bind(pose,()=>focus.Phase,()=>focus.Strikes,()=>focusEnemy?focusEnemy.transform:null,()=>focus.Origin);
            combat.Cancel();dodgeTime=-1;Guarding=false;counterRemaining=0;counterId=null;
            quickAttackBuffer=quickDodgeBuffer=0;ClearTransient();ClearHitSamples();SetOwnedLock(true);
            equipment.DrawForAttack();return true;
        }
        public void CancelFocusSequence(bool endSlow=true)
        {
            if(!focus.Active)return;focus.Cancel();focusEnemy=null;focusStuck=0;
            combat.Cancel();ClearHitSamples();SetOwnedLock(false);
            if(endSlow)GetComponent<PlayerBattleActions>()?.FinishAuthoredFocusSequence();
        }
        public void ReturnFromFocus()
        {
            if(!focus.Active||focus.Phase==HeroFocusPhase.Return)return;
            focus.Return(transform.position);focusStuck=0;combat.Cancel();ClearHitSamples();SetOwnedLock(true);
        }
        float FocusStopDistance()
        {
            float radius=.55f;var body=focusEnemy?focusEnemy.GetComponent<CapsuleCollider>():null;
            if(body)radius=body.radius*Mathf.Max(Mathf.Abs(body.transform.lossyScale.x),Mathf.Abs(body.transform.lossyScale.z));
            var cc=focusEnemy?focusEnemy.GetComponent<CharacterController>():null;if(cc)radius=cc.radius;
            return capsule.radius+radius+.04f;
        }
        bool TickFocusSequence(float delta,ref bool pressed,ref bool held,ref bool released)
        {
            focus.Advance(delta,transform.position);focus.ObserveStrike(combat.Active,combat.Sequence);
            if(focusEnemy)focusLook=focusEnemy.transform.position;
            if(focus.ShouldReturn(focusEnemy&&!focusEnemy.IsDead,combat.Active))ReturnFromFocus();
            if(!focus.Travelling){
                if(!combat.Active||combat.Time<combat.Open)HeroCombatTravel.Face(transform,focusLook,delta,!combat.Active);
                if(focusEnemy&&(!combat.Active||combat.Time<combat.Open)){
                    Vector3 to=Vector3.ProjectOnPlane(focusLook-transform.position,Vector3.up);float gap=to.magnitude-FocusStopDistance();
                    Vector3 followStep=to.normalized*Mathf.Min(Mathf.Max(0,gap),8*delta);
                    if(gap>.04f&&gap<2&&HeroCombatTravel.ClearSight(transform,focusEnemy.transform,transform.position+Vector3.up,focusLook+Vector3.up)&&HeroCombatTravel.SupportedStep(capsule,followStep,focusEnemy.transform))capsule.Move(followStep+Vector3.down*2*delta);
                }
                pressed=focus.AttackInput(held,pressed);held=released=false;SetOwnedLock(true);return false;
            }
            Vector3 step=Vector3.zero;
            if(focus.Phase==HeroFocusPhase.Approach&&focusEnemy){
                HeroCombatTravel.Face(transform,focusLook,delta,true);
                Vector3 to=Vector3.ProjectOnPlane(focusLook-transform.position,Vector3.up);float gap=to.magnitude-FocusStopDistance();
                if(gap<=.06f){focus.Arrive();return true;}
                if(Mathf.Abs(focusLook.y-transform.position.y)>1.6f||!HeroCombatTravel.ClearSight(transform,focusEnemy.transform,transform.position+Vector3.up,focusLook+Vector3.up))ReturnFromFocus();
                else step=to.normalized*Mathf.Min(gap,20*delta);
            }
            if(focus.Phase==HeroFocusPhase.Return)step=focus.ReturnStep(transform.position,delta);
            Vector3 before=transform.position;
            if(step.sqrMagnitude>.000001f&&HeroCombatTravel.SupportedStep(capsule,step,focusEnemy?focusEnemy.transform:null))capsule.Move(step+Vector3.down*2*delta);
            float moved=Vector3.ProjectOnPlane(transform.position-before,Vector3.up).magnitude;
            focusStuck=step.magnitude>.001f&&moved<step.magnitude*.1f?focusStuck+delta:0;
            if(focus.Phase==HeroFocusPhase.Approach&&focusStuck>.14f)ReturnFromFocus();
            if(focus.Phase==HeroFocusPhase.Return&&(focus.Returned(transform.position)||focus.PhaseAge>1.5f||focusStuck>.22f)){
                CancelFocusSequence();ResetLocomotionAfterRelocation();
            }
            return true;
        }
    }
}
