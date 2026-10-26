using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfGraphic.MechBossMigration
{
    [RequireComponent(typeof(Health))]
    public sealed class MistGardenBossFeedback : MonoBehaviour
    {
        public Animator animator;
        public GameObject hitEffect, deathEffect, shieldEffect, chestHitEffect;
        public AudioSource audioSource;
        public AudioClip hitSound, coreHitSound, deathSound;
        public Transform coreMarker;
        public SamProduction.BossArena.ArenaCombatFx contactFeedback;
        public float hitEffectScale=1,hitEffectLifetime=3;
        public bool externalHitVisuals;
        public string displayName="Grenadier";
        public string[] previewStates={"Punch","Throw","CloseRange"};
        public float[] previewDurations={2.9f,2.9f,1.5f};
        public bool enableDebugPreview=false,showDebugHud=false;
        public int FeedbackCount { get; private set; }
        public GameObject LastFeedbackEffect { get; private set; }
        public bool LastHitWasCore { get; private set; }
        Health health;
        Renderer[] renderers;
        MaterialPropertyBlock flash;
        float flashUntil, returnAt;
        int hitVariant;

        void Awake()
        {
            health=GetComponent<Health>();renderers=GetComponentsInChildren<Renderer>(true);flash=new MaterialPropertyBlock();
            if(!audioSource)audioSource=gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake=false;audioSource.spatialBlend=.8f;audioSource.minDistance=3;audioSource.maxDistance=35;
            if(animator){animator.applyRootMotion=false;animator.fireEvents=false;}
        }
        void OnEnable(){if(health)health.Damaged+=OnHit;}
        void OnDisable(){if(health)health.Damaged-=OnHit;ClearFlash();}

        void OnHit(Health target,DamageInfo damage)
        {
            if(GetComponent<MechCorpseLifecycle>()?.Defeated==true)return;
            FeedbackCount++;
            LastHitWasCore=coreMarker&&Vector3.Distance(damage.HitPoint,coreMarker.position)<.8f;
            if(animator)
            {
                if(target.IsDead){animator.CrossFadeInFixedTime("Death",.06f);returnAt=0;}
                else if((!GetComponent<MechSentinelBattle>()||GetComponent<MechSentinelBattle>().NotifyHitReaction(damage.Amount>=30?.4f:.28f))&&(!GetComponent<MechReactiveShield>()||!GetComponent<MechReactiveShield>().Active)){animator.CrossFadeInFixedTime("Hit"+(hitVariant++%4+1),.035f);returnAt=Time.time+.5f;}
            }
            if(audioSource){var clip=target.IsDead?deathSound:LastHitWasCore&&coreHitSound?coreHitSound:hitSound;if(clip)audioSource.PlayOneShot(clip,.85f);}
            if(externalHitVisuals&&!target.IsDead)return;
            var source=target.IsDead&&deathEffect?deathEffect:chestHitEffect?chestHitEffect:hitEffect;
            if(source)
            {
                var battle=GetComponent<MechSentinelBattle>();
                var point=chestHitEffect&&!target.IsDead&&battle?battle.ChestMuzzlePosition:damage.HitPoint;
                LastFeedbackEffect=Instantiate(source,point,transform.rotation);
                LastFeedbackEffect.name=displayName+" authored hit feedback";LastFeedbackEffect.SetActive(true);
                if(!target.IsDead)LastFeedbackEffect.transform.localScale*=hitEffectScale;
                foreach(var ps in LastFeedbackEffect.GetComponentsInChildren<ParticleSystem>(true)){ps.gameObject.SetActive(true);ps.Play(false);}
                Destroy(LastFeedbackEffect,target.IsDead?3:hitEffectLifetime);
            }
            if(!target.IsDead&&contactFeedback)contactFeedback.Impact(damage.HitPoint,true,SamProduction.BossArena.ArenaTeam.Player,damage.HitDirection);
            flashUntil=Time.time+.11f;
            flash.SetColor("_Color01",new Color(1.5f,1.25f,.65f));
            flash.SetColor("_Color02",new Color(1.6f,1.35f,.8f));
            flash.SetColor("_BaseColor",new Color(1.6f,1.35f,.8f));
            foreach(var r in renderers)if(r is SkinnedMeshRenderer)r.SetPropertyBlock(flash);
        }

        void Update()
        {
            if(contactFeedback)contactFeedback.Tick(Time.deltaTime);
            if(flashUntil>0&&Time.time>=flashUntil){ClearFlash();flashUntil=0;}
            if(returnAt>0&&Time.time>=returnAt){returnAt=0;var battle=GetComponent<MechSentinelBattle>();if(!battle||!battle.Engaged){if(animator&&!health.IsDead)animator.CrossFadeInFixedTime("Idle",.1f);if(shieldEffect)shieldEffect.SetActive(false);}}
            if(!enableDebugPreview||health.IsDead||Keyboard.current==null)return;
            if(Keyboard.current.digit1Key.wasPressedThisFrame)PreviewSkill(1);
            if(Keyboard.current.digit2Key.wasPressedThisFrame)PreviewSkill(2);
            if(Keyboard.current.digit3Key.wasPressedThisFrame)PreviewSkill(3);
        }
        public void PreviewSkill(int index)
        {
            var battle=GetComponent<MechSentinelBattle>();if(battle&&battle.Engaged)return;
            if(!animator||health.IsDead||index<1||index>previewStates.Length)return;
            animator.CrossFadeInFixedTime(previewStates[index-1],.08f);
            returnAt=Time.time+previewDurations[index-1];
            if(shieldEffect){shieldEffect.SetActive(index==3);if(index==3)foreach(var ps in shieldEffect.GetComponentsInChildren<ParticleSystem>(true))ps.Play(true);}
        }
        void ClearFlash(){if(renderers!=null)foreach(var r in renderers)if(r is SkinnedMeshRenderer)r.SetPropertyBlock(null);}
        public void ResetForLab()
        {
            if(health.IsDead||GetComponent<MechCorpseLifecycle>()?.Defeated==true)return;
            GetComponent<MechReactiveShield>()?.ResetForLab();
            health.RestoreFullHealth();FeedbackCount=0;hitVariant=0;returnAt=flashUntil=0;ClearFlash();
            if(animator){animator.Rebind();animator.Play("Idle",0,0);animator.Update(0);}
            if(shieldEffect)shieldEffect.SetActive(false);
            if(LastFeedbackEffect)Destroy(LastFeedbackEffect);
        }
        void OnGUI()
        {
            if(!showDebugHud)return;
            GUI.Box(new Rect(Screen.width*.5f-210,20,420,52),displayName.ToUpperInvariant());
            GUI.Label(new Rect(Screen.width*.5f-192,46,384,22),"HP "+health.CurrentHealth+" / "+health.MaxHealth+"     Confirmed hits: "+FeedbackCount);
        }
    }
}
