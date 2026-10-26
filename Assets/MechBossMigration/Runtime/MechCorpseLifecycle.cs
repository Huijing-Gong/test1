using UnityEngine;

namespace KingOfGraphic.MechBossMigration
{
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(Health))]
    public sealed class MechCorpseLifecycle : MonoBehaviour
    {
        public Animator animator;
        public Transform modelRoot;
        [Min(1)] public float disappearDistance = 20;
        [Min(0)] public float minimumVisibleSeconds = 3;
        Health health;
        Collider[] solids;
        MechBodyCollision body;
        bool dead, settled;
        float elapsed, nextDistanceCheck;
        Vector3 corpsePosition;
        public bool Defeated => dead;

        void Awake()
        {
            health=GetComponent<Health>();
            if(!animator)animator=GetComponentInChildren<Animator>();
            if(!modelRoot&&animator)modelRoot=animator.transform;
            solids=GetComponentsInChildren<Collider>(true);
            body=GetComponent<MechBodyCollision>();
        }
        void OnEnable(){if(health)health.Died+=OnDeath;}
        void OnDisable(){if(health)health.Died-=OnDeath;}
        void OnDeath(Health target,DamageInfo damage)
        {
            if(dead)return;
            dead=true;settled=false;elapsed=nextDistanceCheck=0;corpsePosition=transform.position;
            // No standing capsule or live torso separation may survive the death animation.
            if(body)body.enabled=false;
            foreach(var solid in solids)if(solid)solid.enabled=false;
            GetComponent<MechReactiveShield>()?.ResetForLab();
        }
        void LateUpdate()
        {
            if(!dead)return;
            elapsed+=Time.deltaTime;
            if(!settled)
            {
                if(animator)
                {
                    var state=animator.GetCurrentAnimatorStateInfo(0);
                    if(!state.IsName("Death")||animator.IsInTransition(0)||state.normalizedTime<1)return;
                }
                SettleOnGround();settled=true;
                if(animator)animator.speed=0;
            }
            if(!modelRoot||!modelRoot.gameObject.activeSelf||elapsed<minimumVisibleSeconds||Time.time<nextDistanceCheck)return;
            nextDistanceCheck=Time.time+.25f;
            var battle=GetComponent<MechSentinelBattle>();
            var player=battle?battle.player:null;
            if(!player)player=FindFirstObjectByType<PlayerMovement>();
            if(!player)return; // Missing/unspawned player is not evidence that they left.
            if((player.transform.position-corpsePosition).sqrMagnitude>=disappearDistance*disappearDistance)
                modelRoot.gameObject.SetActive(false);
        }
        void SettleOnGround()
        {
            if(!modelRoot)return;
            var skins=modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
            if(skins.Length==0)return;
            var mesh=new Mesh();var bounds=new Bounds();bool found=false;
            try
            {
                foreach(var skin in skins)
                {
                    if(!skin.enabled)continue;
                    skin.BakeMesh(mesh);
                    foreach(var vertex in mesh.vertices)
                    {
                        var world=skin.transform.TransformPoint(vertex);
                        if(!found){bounds=new Bounds(world,Vector3.zero);found=true;}else bounds.Encapsulate(world);
                    }
                }
            }
            finally{Destroy(mesh);}
            if(!found)return;
            float ground=transform.position.y;
            var hits=Physics.RaycastAll(new Vector3(bounds.center.x,transform.position.y+2,bounds.center.z),Vector3.down,12,~0,QueryTriggerInteraction.Ignore);
            float nearest=float.PositiveInfinity;
            foreach(var hit in hits)
            {
                if(hit.transform.IsChildOf(transform)||hit.normal.y<.65f||hit.collider.GetComponentInParent<PlayerMovement>())continue;
                if(hit.distance<nearest){nearest=hit.distance;ground=hit.point.y;}
            }
            float correction=ground+.01f-bounds.min.y;
            modelRoot.position+=Vector3.up*correction;
            corpsePosition=bounds.center+Vector3.up*correction;
        }
    }
}
