using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Disposable native-physics regression fixtures. Does not save or alter an authored map/prefab.
public static class RailBeetleCornerChecks
{
    private const string PrefabPath = "Assets/Art/Enemies/RailBeetle/RailBeetle.prefab";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(15000f, 500f, 15000f);

    [MenuItem("Tools/Art/Check Rail Beetle Corner Navigation")]
    public static void Run()
    {
        RailBeetleChecks.Run();
        RailBeetleGroundChecks.Run();
        RunFocused();
    }

    public static void RunFocused()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before native corner QA.");
        if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            typeof(RailBeetleChecks).GetMethod("PrepareSavedSceneForFixtures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var failures = new List<string>(); int passed = 0;
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach (float slope in new[] { 12f, -12f, 22f, -22f })
                Case("Slope " + slope + " degrees / 180-degree turn", () => CheckSlopeTurn(slope), failures, ref passed);
            Case("Tight right/back corner escape and natural warned contact", CheckCornerEscape, failures, ref passed);
            Case("Flat 24 m pursuit has stable forward travel", CheckStablePursuit, failures, ref passed);
            Case("Patrol keeps its target while making an initial 90-degree turn", CheckPatrolTurning, failures, ref passed);
            Case("Near-player detour releases into a full warned charge", CheckNearPlayerDetour, failures, ref passed);
            Case("Late straight retreat is caught at 6 m/s, but left/right escapes are fair", CheckDirectionalDodge, failures, ref passed);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            Physics.SyncTransforms();
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Rail Beetle corner QA failed " + failures.Count + " / " + (passed + failures.Count) +
                " groups:\n" + string.Join("\n", failures));
        Debug.Log("Rail Beetle corner PASS: " + passed + " bounded native groups. Full Run also retains the original 17 physics and 1920 visual-ground frames.");
    }

    private static void Case(string name, Action check, List<string> failures, ref int passed)
    {
        try { check(); passed++; Debug.Log("Rail Beetle corner PASS: " + name); }
        catch (Exception error) { failures.Add(name + ": " + error.Message); Debug.LogWarning("Rail Beetle corner FAIL: " + name + ": " + error.Message); }
    }

    private static void CheckSlopeTurn(float degrees)
    {
        using (var f = new Fixture())
        {
            f.Slope(degrees); f.PlayerAt(new Vector3(0f, 0f, -6f));
            float maxPenetration = 0f;
            for (int i = 0; i < 160; i++)
            {
                Call(f.AI, "TryFace", Vector3.back, .02f); f.Sync();
                maxPenetration = Mathf.Max(maxPenetration, f.Penetration(f.Floor.GetComponent<Collider>()));
            }
            float angle = Quaternion.Angle(f.Body.rotation, Quaternion.LookRotation(Vector3.back));
            Require(angle < 8f, "Stopped " + angle.ToString("F2") + " degrees short; root=" + f.LocalPosition.ToString("F3") +
                "; floor penetration=" + maxPenetration.ToString("F4"));
            Require(maxPenetration <= .04f, "Turning pushed a physical collider into its support beyond the existing upward-support tolerance.");
            object[] support = { f.Body.position, 0f };
            Require((bool)Call(f.AI, "TryGround", support), "The final full-footprint pose lost real ground support.");
        }
    }

    private static void CheckCornerEscape()
    {
        using (var f = new Fixture())
        {
            Collider right = f.Wall(new Vector3(1.01f, 1.5f, 0f), new Vector3(.2f, 3f, 12f));
            Collider back = f.Wall(new Vector3(0f, 1.5f, -1.02f), new Vector3(12f, 3f, .2f));
            f.PlayerAt(new Vector3(-4f, .015f, 6f));
            f.Seek(); Set(f.AI, "side", 1);
            bool woundUp = false, charged = false;
            float windupTime = 0f; float maxWallPenetration = 0f;
            for (int i = 0; i < 600 && !charged; i++)
            {
                switch (f.AI.CurrentState)
                {
                    case RailBeetleAI.BehaviourState.Seek:
                        Set(f.AI, "cooldown", Mathf.Max(0f, (float)Get(f.AI, "cooldown") - .02f));
                        Call(f.AI, "Seek", f.Player.transform.position, (bool)Call(f.AI, "HasSight"), .02f); break;
                    case RailBeetleAI.BehaviourState.Windup:
                        woundUp = true; windupTime += .02f;
                        Call(f.AI, "TickWindup", .02f); break;
                    case RailBeetleAI.BehaviourState.Charge:
                        charged = true; break;
                }
                f.Sync();
                maxWallPenetration = Mathf.Max(maxWallPenetration, Mathf.Max(f.Penetration(right), f.Penetration(back)));
                Require(maxWallPenetration <= .002f, "Escape moved/rotated a physical capsule or foot box through a solid wall: " + maxWallPenetration);
            }
            Require(woundUp && charged && windupTime >= .85f,
                "After 12 seconds it never escaped/aligned into a full warned charge; state=" + f.AI.CurrentState +
                "; root=" + f.LocalPosition.ToString("F3") + "; yaw=" + f.Body.rotation.eulerAngles.y.ToString("F2"));
            for (int i = 0; i < 100 && f.AI.IsCharging; i++)
            {
                Call(f.AI, "TickCharge", .02f); f.Sync();
                Require(Mathf.Max(f.Penetration(right), f.Penetration(back)) <= .002f, "The subsequent charge cut through the corner walls.");
            }
            Require(f.Player.CurrentHealth == 86 && !f.AI.IsCharging,
                "A successfully escaped/aligned enemy must make one real 14-damage contact and recover, not loop without a hit.");
            Debug.Log("Corner actual result: position=" + f.LocalPosition.ToString("F3") + "; warned seconds=" + windupTime.ToString("F2") +
                "; target HP=" + f.Player.CurrentHealth + "; maximum wall penetration=" + maxWallPenetration.ToString("F4"));
        }
    }

    private static void CheckStablePursuit()
    {
        using (var f = new Fixture())
        {
            f.PlayerAt(new Vector3(0f, .015f, 24f)); f.Seek(); Set(f.AI, "cooldown", 6f);
            float maxSide = 0f, maxHeading = 0f; int reversalCount = 0; float previousSideStep = 0f;
            for (int i = 0; i < 120; i++)
            {
                Vector3 before = f.Body.position;
                Call(f.AI, "Seek", f.Player.transform.position, true, .02f); f.Sync();
                Vector3 delta = f.Body.position - before;
                Require(delta.z > .02f && f.AI.CurrentState == RailBeetleAI.BehaviourState.Seek,
                    "Open-ground pursuit stopped/reversed before reaching attack range.");
                if (Mathf.Abs(delta.x) > .003f && Mathf.Abs(previousSideStep) > .003f && Mathf.Sign(delta.x) != Mathf.Sign(previousSideStep)) reversalCount++;
                if (Mathf.Abs(delta.x) > .003f) previousSideStep = delta.x;
                maxSide = Mathf.Max(maxSide, Mathf.Abs(f.LocalPosition.x));
                maxHeading = Mathf.Max(maxHeading, Vector3.Angle(f.Body.rotation * Vector3.forward, Vector3.forward));
            }
            Require(f.LocalPosition.z > 9f && maxSide < .1f && maxHeading < 2f && reversalCount <= 1,
                "Straight pursuit wobbled: distance=" + f.LocalPosition.z + "; side=" + maxSide + "; yaw=" + maxHeading + "; reversals=" + reversalCount);
        }
    }

    private static void CheckPatrolTurning()
    {
        using (var f = new Fixture())
        {
            f.PlayerAt(new Vector3(0f, .015f, 24f));
            Set(f.AI, "state", RailBeetleAI.BehaviourState.Patrol); Set(f.AI, "timer", 0f);
            Set(f.AI, "side", 1); Set(f.AI, "patrolTravelling", false);
            Call(f.AI, "Patrol", .02f); f.Sync();
            Vector3 target = (Vector3)Get(f.AI, "patrolTarget");
            Require((bool)Get(f.AI, "patrolTravelling") && target.x > Origin.x + 1f,
                "A valid turning-only patrol tick discarded its target before it could move.");
            float furthest = 0f, travel = 0f; int targetChanges = 0;
            for (int i = 1; i < 120; i++)
            {
                Vector3 before = f.Body.position;
                Call(f.AI, "Patrol", .02f); f.Sync();
                Vector3 current = (Vector3)Get(f.AI, "patrolTarget");
                if (Vector3.Distance(current, target) > .01f) { targetChanges++; target = current; }
                if (i < 12) Require((bool)Get(f.AI, "patrolTravelling") && targetChanges == 0,
                    "The patrol canceled/flipped its route repeatedly while still turning.");
                travel += Vector3.Distance(before, f.Body.position); furthest = Mathf.Max(furthest, f.LocalPosition.magnitude);
            }
            Require(furthest > 1f && travel > 1f && targetChanges <= 1,
                "Patrol did not finish a stable first leg: furthest=" + furthest.ToString("F3") +
                "; path=" + travel.ToString("F3") + "; target changes=" + targetChanges);
        }
    }

    private static void CheckNearPlayerDetour()
    {
        using (var f = new Fixture())
        {
            f.PlayerAt(new Vector3(0f, .015f, 1.6f)); f.Seek();
            Set(f.AI, "detouring", true); Set(f.AI, "detourTravel", .7f); Set(f.AI, "detourHeading", Vector3.forward);
            Call(f.AI, "Seek", f.Player.transform.position, true, .02f); f.Sync();
            Require(!(bool)Get(f.AI, "detouring"),
                "A completed, already player-facing detour remained active because a .7 m clearance probe touched the near player.");
            float warned = 0f; bool started = false;
            for (int i = 0; i < 240 && f.Player.CurrentHealth == 100; i++)
            {
                switch (f.AI.CurrentState)
                {
                    case RailBeetleAI.BehaviourState.Seek:
                        Call(f.AI, "Seek", f.Player.transform.position, (bool)Call(f.AI, "HasSight"), .02f); break;
                    case RailBeetleAI.BehaviourState.Windup:
                        started = true; warned += .02f; Call(f.AI, "TickWindup", .02f); break;
                    case RailBeetleAI.BehaviourState.Charge:
                        Call(f.AI, "TickCharge", .02f); break;
                }
                f.Sync();
            }
            Require(started && warned >= .85f && f.Player.CurrentHealth == 86 && !f.AI.IsCharging,
                "A released close detour failed to regain a full warned physical attack: state=" + f.AI.CurrentState +
                "; warning=" + warned.ToString("F2") + "; target HP=" + f.Player.CurrentHealth + "; root=" + f.LocalPosition.ToString("F3"));
        }
    }

    private static void CheckDirectionalDodge()
    {
        foreach (Vector3 escape in new[] { Vector3.forward, Vector3.left, Vector3.right })
        using (var f = new Fixture(true))
        {
            f.PlayerAt(new Vector3(0f, .015f, 8f)); f.Seek();
            Call(f.AI, "Seek", f.Player.transform.position, true, .02f);
            bool moving = false; float warned = 0f;
            for (int i = 0; i < 180 && (f.AI.CurrentState == RailBeetleAI.BehaviourState.Windup ||
                     f.AI.CurrentState == RailBeetleAI.BehaviourState.Charge); i++)
            {
                moving |= (bool)Get(f.AI, "aimLocked");
                // Edit-mode fixtures advance the target transform; Play QA separately uses real CC.Move.
                if (moving) { f.Player.transform.position += escape * 6f * .02f; Physics.SyncTransforms(); }
                if (f.AI.CurrentState == RailBeetleAI.BehaviourState.Windup)
                { warned += .02f; Call(f.AI, "TickWindup", .02f); }
                else Call(f.AI, "TickCharge", .02f);
                f.Sync();
            }
            bool retreat = escape == Vector3.forward;
            Require(warned >= .85f && f.Player.CurrentHealth == (retreat ? 86 : 100),
                "Enlarged beetle directional dodge mismatch: escape=" + escape + "; HP=" + f.Player.CurrentHealth +
                "; player=" + (f.Player.transform.position - Origin) + "; heading=" + Get(f.AI, "chargeHeading"));
            Require(retreat || f.LocalPosition.z <= 10f,
                "A successful side dodge must not leave the beetle running the full 16 m away from its counterattack opportunity.");
            Require(f.AI.CurrentState == RailBeetleAI.BehaviourState.Recover &&
                    Mathf.Abs((float)Get(f.AI, "timer") - 2.2f) < .001f,
                "Hit and missed charges must both expose the full 2.2-second counterattack window.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root, Floor;
        public readonly RailBeetleAI AI;
        public readonly Rigidbody Body;
        public readonly Health Player;
        private readonly GameObject playerRoot;
        private readonly CapsuleCollider capsule;
        private readonly BoxCollider box;
        private readonly List<GameObject> walls = new List<GameObject>();
        public Vector3 LocalPosition => Body.position - Origin;
        public Fixture(bool enlarged = false)
        {
            Floor = new GameObject("QA corner ground"); Floor.transform.position = Origin + Vector3.down * .5f;
            Floor.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            Root = new GameObject("QA corner Rail Beetle"); Root.transform.position = Origin + Vector3.up * .015f;
            if (enlarged) Root.transform.localScale = Vector3.one * 1.2f;
            Health health = Root.AddComponent<Health>(); Configure(health, 80, HealthTeam.Enemy);
            CapsuleCollider authoredCapsule = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<CapsuleCollider>();
            BoxCollider authoredBox = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BoxCollider>();
            capsule = Root.AddComponent<CapsuleCollider>(); capsule.direction = authoredCapsule.direction;
            capsule.center = authoredCapsule.center; capsule.radius = authoredCapsule.radius; capsule.height = authoredCapsule.height;
            box = Root.AddComponent<BoxCollider>(); box.center = authoredBox.center; box.size = authoredBox.size;
            Body = Root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
            AI = Root.AddComponent<RailBeetleAI>(); Call(AI, "Awake");
            playerRoot = new GameObject("QA corner player"); Player = playerRoot.AddComponent<Health>(); Configure(Player, 100, HealthTeam.Player);
            CharacterController controller = playerRoot.AddComponent<CharacterController>(); controller.radius = .3f;
            controller.height = 1.8f; controller.center = Vector3.up * .9f;
            Set(AI, "playerHealth", Player); Set(AI, "playerBody", controller); Sync();
        }
        public void Sync() { Root.transform.SetPositionAndRotation(Body.position, Body.rotation); Physics.SyncTransforms(); }
        public void PlayerAt(Vector3 value) { playerRoot.transform.position = Origin + value; Physics.SyncTransforms(); }
        public void Seek()
        {
            Set(AI, "state", RailBeetleAI.BehaviourState.Seek); Set(AI, "cooldown", 0f);
            Call(AI, "AcquireTarget", playerRoot.transform.position); Set(AI, "lastMovePosition", Body.position);
        }
        public void Slope(float degrees)
        {
            Quaternion rotation = Quaternion.Euler(degrees, 0f, 0f);
            Floor.transform.SetPositionAndRotation(Origin + rotation * (Vector3.down * .5f), rotation);
            Body.position = Origin + Vector3.up * .35f; Sync();
            object[] ground = { Body.position, 0f };
            Require((bool)Call(AI, "TryGround", ground), "Initial slope stance has no support.");
            Body.position = new Vector3(Origin.x, (float)ground[1], Origin.z); Sync();
        }
        public Collider Wall(Vector3 position, Vector3 size)
        {
            GameObject wall = new GameObject("QA corner solid wall"); wall.transform.position = Origin + position;
            BoxCollider collider = wall.AddComponent<BoxCollider>(); collider.size = size; walls.Add(wall); Physics.SyncTransforms(); return collider;
        }
        public float Penetration(Collider obstacle)
        {
            float maximum = 0f;
            foreach (Collider shape in new Collider[] { capsule, box })
                if (Physics.ComputePenetration(shape, Body.position, Body.rotation, obstacle, obstacle.transform.position,
                    obstacle.transform.rotation, out _, out float depth)) maximum = Mathf.Max(maximum, depth);
            return maximum;
        }
        public void Dispose()
        {
            foreach (GameObject wall in walls) Object.DestroyImmediate(wall);
            Object.DestroyImmediate(Root); Object.DestroyImmediate(playerRoot); Object.DestroyImmediate(Floor); Physics.SyncTransforms();
        }
    }

    private static void Configure(Health health, int maximum, HealthTeam team)
    {
        var data = new SerializedObject(health); data.FindProperty("maxHealth").intValue = maximum;
        data.FindProperty("team").enumValueIndex = (int)team; data.ApplyModifiedPropertiesWithoutUndo(); health.RestoreFullHealth();
    }
    private static object Call(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method, Private);
        if (info == null) throw new InvalidOperationException("Missing native QA method: " + method);
        try { return info.Invoke(target, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Require(bool passed, string reason) { if (!passed) throw new InvalidOperationException(reason); }
}
