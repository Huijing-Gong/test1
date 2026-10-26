using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    [RequireComponent(typeof(Health))]
    public sealed class MechReactiveShield:MonoBehaviour
    {
        [Range(0,1)] public float triggerProbability=.25f;
        public float duration=1,cooldown=6;
        [Range(0,1)] public float damageReduction=.7f;
        public GameObject shieldVisual;public AudioClip blockSound;
        Health health;AudioSource audioSource;float remaining,cooldownRemaining;
        public bool Active=>remaining>0;
        void Awake(){health=GetComponent<Health>();audioSource=GetComponent<AudioSource>();}
        void OnEnable(){health.ResolvingDamage+=Resolve;health.Damaged+=AfterHit;}
        void OnDisable(){health.ResolvingDamage-=Resolve;health.Damaged-=AfterHit;ResetForLab();}
        void Update()=>AdvanceDefense(Time.deltaTime);
        public void AdvanceDefense(float dt){if(health.IsDead){ResetForLab();return;}remaining=Mathf.Max(0,remaining-dt);cooldownRemaining=Mathf.Max(0,cooldownRemaining-dt);if(shieldVisual&&shieldVisual.activeSelf!=Active)shieldVisual.SetActive(Active);}
        bool PlayerHit(DamageInfo damage)=>damage.Source&&damage.Source.GetComponentInParent<PlayerMovement>();
        void Resolve(ref DamageInfo damage){if(!Active||!PlayerHit(damage))return;damage=new DamageInfo(Mathf.Max(1,Mathf.RoundToInt(damage.Amount*(1-damageReduction))),damage.Source,damage.HitPoint,damage.HitDirection);if(audioSource&&blockSound)audioSource.PlayOneShot(blockSound,.55f);}
        void AfterHit(Health target,DamageInfo damage){if(target.IsDead||Active||cooldownRemaining>0||!PlayerHit(damage)||Random.value>=triggerProbability)return;remaining=duration;cooldownRemaining=cooldown;if(shieldVisual){shieldVisual.SetActive(true);foreach(var ps in shieldVisual.GetComponentsInChildren<ParticleSystem>(true))ps.Play(false);}if(audioSource&&blockSound)audioSource.PlayOneShot(blockSound,.7f);}
        public void ResetForLab(){remaining=cooldownRemaining=0;if(shieldVisual)shieldVisual.SetActive(false);}
    }
}
