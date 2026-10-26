using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class WorldCombatMotionRepairChecks
{
    public static string HandleThickness()
    {
        var e=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>().GetComponent<PlayerEquipment>();
        var mesh=e.PracticeSword.GetComponentInChildren<MeshFilter>(true).sharedMesh;
        var samples=mesh.vertices.Where(v=>v.y>-.15f&&v.y<-.05f).ToArray();
        float radius=samples.Max(v=>Mathf.Max(Mathf.Abs(v.x),Mathf.Abs(v.z)));
        if(radius>.015f)throw new Exception("FAIL: existing grip is too thick for closed modular hand, radius="+radius);
        return "PASS: handle radius="+radius+"m";
    }
    public static string GripGeometry()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();var clone=UnityEngine.Object.Instantiate(player.gameObject);
        try {
            foreach(var script in clone.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            var a=clone.GetComponentInChildren<Animator>();a.Rebind();a.Play("WorldGuardLoop",0,.5f);a.Update(0);
            clone.GetComponent<PlayerEquipment>().PracticeSword.SetActive(true);
            var grip=clone.GetComponent<WorldSwordGrip>();if(grip)grip.ApplyGrip();
            var hand=a.GetBoneTransform(HumanBodyBones.RightHand);
            var index=a.GetBoneTransform(HumanBodyBones.RightIndexProximal);var little=a.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            var palm=(index.position+little.position)*.5f+hand.TransformVector(new Vector3(.022f,-.008f,0));
            var sword=clone.GetComponent<PlayerEquipment>().PracticeSword.transform;
            float error=Vector3.Distance(sword.TransformPoint(new Vector3(0,-.12f,0)),palm);
            if(error>.025f)throw new Exception("FAIL: sword handle centre misses actual palm by "+error+"m");
            return "PASS: sword handle/palm separation="+error+"m";
        } finally{UnityEngine.Object.DestroyImmediate(clone);}
    }
    public static string GroundedEvasions()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();var clone=UnityEngine.Object.Instantiate(player.gameObject);
        try {
            foreach(var script in clone.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            var a=clone.GetComponentInChildren<Animator>();a.Rebind();
            foreach(string name in new[]{"DodgeBack","DodgeForward","BattleRush"})for(int i=0;i<21;i++) {
                a.Play(name,0,i/21f);a.Update(0);
                var hips=a.GetBoneTransform(HumanBodyBones.Hips);var head=a.GetBoneTransform(HumanBodyBones.Head);
                float tilt=Vector3.Angle(head.position-hips.position,Vector3.up);
                if(tilt>50)throw new Exception("FAIL: "+name+" pitches the whole body into an aerial pose: "+tilt);
                float low=Mathf.Min(a.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,a.GetBoneTransform(HumanBodyBones.RightFoot).position.y)-a.transform.position.y;
                if(low>.28f)throw new Exception("FAIL: "+name+" lifts both feet away from floor: "+low);
            }
            var action=clone.GetComponent<PlayerBattleActions>();var type=typeof(PlayerBattleActions);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var late=type.GetMethod("LateUpdate",flags);
            if(late!=null) {
                void Set(string n,object v){type.GetField(n,flags)?.SetValue(action,v);}
                type.GetProperty("IsDodging").GetSetMethod(true).Invoke(action,new object[]{true});
                Set("animator",a);Set("backRoll",true);Set("dodgeDuration",.78f);Set("dodgeStart",CombatClock.UnscaledTime-.39f);Set("rollVisual",a.transform);Set("rollHips",a.GetBoneTransform(HumanBodyBones.Hips));Set("rollPosition",a.transform.localPosition);Set("rollRotation",a.transform.localRotation);
                late.Invoke(action,null);
                if(Vector3.Dot(a.transform.up,clone.transform.up)<.9f)throw new Exception("FAIL: script adds a full body somersault during retreat");
            }
            return "PASS: evasion and counter approach body tilt stays below 50 degrees; no added visual-root inversion.";
        } finally{UnityEngine.Object.DestroyImmediate(clone);}
    }
    public static string BackwardDuringFocus()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();var clone=UnityEngine.Object.Instantiate(source.gameObject);
        var enemy=new GameObject("Temporary lock target");var camera=new GameObject("Temporary lock camera");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        void Set(object obj,string name,object value){obj.GetType().GetField(name,flags).SetValue(obj,value);}
        try {
            foreach(var script in clone.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            var animator=clone.GetComponentInChildren<Animator>();animator.Rebind();animator.Play("Locomotion",0,0);animator.Update(0);
            var movement=clone.GetComponent<PlayerMovement>();Set(movement,"stableGrounded",true);
            var battle=clone.GetComponent<PlayerBattleActions>();Set(battle,"ownsFocus",true);
            var health=enemy.AddComponent<Health>();health.SetMaximumHealth(100);
            var rig=camera.AddComponent<ThirdPersonCamera>();Set(rig,"lockedEnemy",health);
            // Express a 2m/s step in the clock actually used by this frame. A
            // fixed 5cm step becomes "not moving" on a slow editor frame.
            var world=clone.GetComponent<WorldHeroActions>();Set(world,"animator",animator);Set(world,"movement",movement);Set(world,"combat",clone.GetComponent<PlayerCombat>());Set(world,"battle",battle);Set(world,"health",clone.GetComponent<Health>());Set(world,"cameraRig",rig);Set(world,"previousPosition",clone.transform.position+clone.transform.forward*2f*Mathf.Max(.001f,battle.PlayerDeltaTime));Set(world,"wasGrounded",true);
            typeof(WorldHeroActions).GetMethod("Update",flags).Invoke(world,null);animator.Update(.15f);
            if(!animator.GetCurrentAnimatorStateInfo(0).IsName("WalkBack")&&!(animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName("WalkBack")))throw new Exception("FAIL: locked retreat in focus still plays forward locomotion");
            return "PASS: locked retreat selects backward gait during focus.";
        } finally{UnityEngine.Object.DestroyImmediate(clone);UnityEngine.Object.DestroyImmediate(enemy);UnityEngine.Object.DestroyImmediate(camera);}
    }
}
