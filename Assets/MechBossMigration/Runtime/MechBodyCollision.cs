using UnityEngine;
namespace KingOfGraphic.MechBossMigration
{
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class MechBodyCollision:MonoBehaviour
    {
        public SkinnedMeshRenderer torsoSkin;
        public BoxCollider torsoSolid;
        public float skinWidth=.025f;
        CapsuleCollider body;MechSentinelBattle battle;Mesh baked;
        readonly RaycastHit[] hits=new RaycastHit[32];
        void Awake(){body=GetComponent<CapsuleCollider>();battle=GetComponent<MechSentinelBattle>();}
        public void Move(Vector3 displacement)
        {
            var island = GetComponent<MechIslandGroundConstraint>();
            if(island && island.isActiveAndEnabled)
                displacement = island.ConstrainDisplacement(transform.position, displacement);
            if(displacement.sqrMagnitude<1e-8f)return;
            if(!body)body=GetComponent<CapsuleCollider>();
            Physics.SyncTransforms();float length=displacement.magnitude;var direction=displacement/length;float allowed=length;
            var scale=transform.lossyScale;float radius=body.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));float half=Mathf.Max(radius,body.height*Mathf.Abs(scale.y)*.5f);var center=transform.TransformPoint(body.center);var offset=transform.up*(half-radius);
            int count=Physics.CapsuleCastNonAlloc(center+offset,center-offset,radius,direction,hits,length+skinWidth,~0,QueryTriggerInteraction.Ignore);
            Limit(count,ref allowed);
            if(torsoSolid&&torsoSolid.enabled)
            {
                var s=torsoSolid.transform.lossyScale;var extents=Vector3.Scale(torsoSolid.size*.5f,new Vector3(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z)));
                count=Physics.BoxCastNonAlloc(torsoSolid.transform.TransformPoint(torsoSolid.center),extents,direction,hits,torsoSolid.transform.rotation,length+skinWidth,~0,QueryTriggerInteraction.Ignore);Limit(count,ref allowed);
            }
            transform.position+=direction*Mathf.Max(0,allowed);
        }
        void Limit(int count,ref float allowed)
        {
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];var c=hit.collider;if(!c||c.transform.IsChildOf(transform)||c.isTrigger||hit.normal.y>.65f)continue;
                if(Physics.GetIgnoreLayerCollision(gameObject.layer,c.gameObject.layer)||Physics.GetIgnoreCollision(body,c))continue;
                allowed=Mathf.Min(allowed,Mathf.Max(0,hit.distance-skinWidth));
            }
        }
        void LateUpdate()
        {
            if(Time.deltaTime<=0)return;
            if(torsoSkin&&torsoSolid)
            {
                if(!baked)baked=new Mesh{name="Temporary animated torso collider fit"};
                torsoSkin.BakeMesh(baked);var bounds=baked.bounds;var scale=torsoSolid.transform.lossyScale;
                // Occupy the chest's entire horizontal footprint down to the floor,
                // so the player cannot stand beneath a forward-leaning torso.
                var minimum=bounds.min;var maximum=bounds.max;
                minimum.y=Mathf.Min(minimum.y,torsoSolid.transform.InverseTransformPoint(transform.position).y);
                bounds.SetMinMax(minimum,maximum);
                torsoSolid.center=bounds.center;torsoSolid.size=bounds.size+new Vector3(.04f/Mathf.Abs(scale.x),.02f/Mathf.Abs(scale.y),.04f/Mathf.Abs(scale.z));
            }
            if(!battle)battle=GetComponent<MechSentinelBattle>();if(!battle||!battle.player)return;
            var controller=battle.player.GetComponent<CharacterController>();if(!controller||!controller.enabled)return;
            Physics.SyncTransforms();
            for(int pass=0;pass<3;pass++){bool adjusted=Separate(controller,body);if(torsoSolid)adjusted=Separate(controller,torsoSolid)||adjusted;if(!adjusted)break;Physics.SyncTransforms();}
        }
        bool HorizontalOverlap(CharacterController player,Collider solid,out Vector3 direction,out float distance)
        {
            direction=Vector3.zero;distance=0;
            if(!solid||!solid.enabled)return false;
            var playerScale=player.transform.lossyScale;var center=player.transform.TransformPoint(player.center);
            float radius=player.radius*Mathf.Max(Mathf.Abs(playerScale.x),Mathf.Abs(playerScale.z));
            float half=player.height*Mathf.Abs(playerScale.y)*.5f;
            if(center.y+half<=solid.bounds.min.y||center.y-half>=solid.bounds.max.y)return false;
            var box=solid as BoxCollider;
            if(box)
            {
                var scale=box.transform.lossyScale;float sx=Mathf.Abs(scale.x),sz=Mathf.Abs(scale.z);
                var point=box.transform.InverseTransformPoint(center);var minimum=box.center-box.size*.5f;var maximum=box.center+box.size*.5f;
                float x=Mathf.Clamp(point.x,minimum.x,maximum.x),z=Mathf.Clamp(point.z,minimum.z,maximum.z);
                var delta=new Vector3((point.x-x)*sx,0,(point.z-z)*sz);float length=delta.magnitude;
                if(length>=radius+skinWidth)return false;
                if(length>.0001f){direction=box.transform.TransformDirection(delta/length);direction.y=0;direction.Normalize();distance=radius+skinWidth-length;}
                else
                {
                    float left=(point.x-minimum.x)*sx,right=(maximum.x-point.x)*sx,back=(point.z-minimum.z)*sz,front=(maximum.z-point.z)*sz;
                    float nearest=Mathf.Min(left,right,back,front);var axis=nearest==left?Vector3.left:nearest==right?Vector3.right:nearest==back?Vector3.back:Vector3.forward;
                    direction=box.transform.TransformDirection(axis);direction.y=0;direction.Normalize();distance=nearest+radius+skinWidth;
                }
                return distance>.001f;
            }
            var capsule=solid as CapsuleCollider;if(!capsule)return false;
            var bodyCenter=capsule.transform.TransformPoint(capsule.center);var bodyScale=capsule.transform.lossyScale;float bodyRadius=capsule.radius*Mathf.Max(Mathf.Abs(bodyScale.x),Mathf.Abs(bodyScale.z));
            var radial=Vector3.ProjectOnPlane(center-bodyCenter,Vector3.up);float separation=radial.magnitude;float required=bodyRadius+radius+skinWidth;
            if(separation>=required)return false;direction=separation>.001f?radial/separation:transform.forward;distance=required-separation;return true;
        }
        bool Separate(CharacterController player,Collider solid)
        {
            var position=player.transform.position;if(!HorizontalOverlap(player,solid,out var direction,out float required))return false;
            // Resolve on the ground plane using capsule/box footprints rather than
            // relying on CharacterController support in ComputePenetration.
            player.Move(direction*required);float moved=Vector3.ProjectOnPlane(player.transform.position-position,Vector3.up).magnitude;
            // If a wall prevents the player moving aside, let the boss yield instead.
            if(moved+skinWidth<required)Move(-direction*(required-moved));
            return true;
        }
        void OnDisable(){if(baked)Destroy(baked);baked=null;}
    }
}
