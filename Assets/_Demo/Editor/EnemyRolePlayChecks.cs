using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Runtime-only role fixtures, run by the save-isolated real Play harness. No authored scene writes.
public static class EnemyRolePlayChecks
{
    const string Scout = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";
    const string Domain = "Assets/Art/Enemies/DomainPrototypes/Generated/";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(19000, 100, 19000);

    [MenuItem("Tools/Enemies/Check Five Enemy Combat Roles (Play)")]
    public static void Run() => EnemyFoundationPlayChecks.Run();

    public static IEnumerator Tests(BaselineFlow flow, Transform player, Action<string> log)
    {
        foreach (var ai in Object.FindObjectsByType<StoneScoutRoadsideAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<DomainPrototypeEnemy>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RailBeetleAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var ai in Object.FindObjectsByType<RepairWispAI>(FindObjectsSortMode.None)) ai.enabled = false;
        foreach (var nav in Object.FindObjectsByType<EnemyNavigationWorld>(FindObjectsSortMode.None)) nav.enabled = false;
        var movement = player.GetComponent<PlayerMovement>(); movement.enabled = false; movement.ClearImpact();
        var playerHealth = player.GetComponent<Health>(); var vitals = player.GetComponent<PlayerVitals>();
        Collider floor = Box("QA flat role arena", Origin + Vector3.down * .15f, new Vector3(48, .3f, 48));
        var data = new SerializedObject(flow.GetComponent<OverworldV2Layout>());
        var floors = data.FindProperty("navigationSurfaces"); int n = floors.arraySize; floors.arraySize++;
        floors.GetArrayElementAtIndex(n).objectReferenceValue = floor; data.ApplyModifiedPropertiesWithoutUndo();
        // Bake only the temporary flat fixture; never alter authored navigation assets.
        var navRoot = new GameObject("QA flat role routes");
        var navigation = navRoot.AddComponent<EnemyNavigationWorld>(); navigation.enabled = false;
        var meshData = new List<NavMeshData>();
        var navData = new SerializedObject(navigation); var meshes = navData.FindProperty("bakedData"); meshes.arraySize = 4;
        var ids = navData.FindProperty("agentTypes"); ids.arraySize = 4;
        var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(48, .3f, 48), transform = Matrix4x4.TRS(Origin + Vector3.down * .15f, Quaternion.identity, Vector3.one), area = 0 } };
        for (int i = 0; i < 4; i++)
        {
            var settings = EnemyNavigationBuilder.Settings(i);
            var baked = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Origin, new Vector3(50, 8, 50)), Vector3.zero, Quaternion.identity);
            Need(baked, "Temporary role navigation failed to bake."); meshData.Add(baked);
            meshes.GetArrayElementAtIndex(i).objectReferenceValue = baked;
            ids.GetArrayElementAtIndex(i).intValue = settings.agentTypeID;
        }
        navData.ApplyModifiedPropertiesWithoutUndo(); navigation.enabled = true;
        MovePlayer(player, Origin + Vector3.forward * 5f);

        var scout = Spawn(Scout, Origin).GetComponent<StoneScoutRoadsideAI>();
        yield return null;
        SetState(scout, "Recover"); Set(scout, "timer", .8f);
        Vector3 rest = scout.transform.position;
        yield return new WaitForSeconds(.45f);
        Need(Vector3.Distance(scout.transform.position, rest) < .015f && State(scout) == "Recover", "Scout slides during counterattack window.");
        var visual = scout.GetComponentInChildren<StoneScoutVisualMotion>();
        visual.PlayHit(Vector3.right); yield return new WaitForSeconds(.1f);
        Need(Quaternion.Angle(visual.transform.localRotation, Quaternion.identity) > 1f, "Directional hit tilt missing.");
        scout.GetComponent<Health>().TryApplyDamage(new DamageInfo(1000, player.gameObject, rest, Vector3.forward));
        yield return new WaitForSeconds(.7f);
        var renderer = visual.GetComponentInChildren<MeshRenderer>(); var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
        Need(block.GetFloat("_CorePower") < .05f, "Dead Scout core remains powered.");
        int chips = 0;
        foreach (ParticleSystem particles in scout.GetComponentsInChildren<ParticleSystem>()) chips += particles.particleCount;
        Need(chips > 0, "Delayed landing chips missing.");
        scout.GetComponent<StoneScoutCombatTarget>().ResetForJourney(); yield return null;
        renderer.GetPropertyBlock(block); Need(block.GetFloat("_CorePower") > .95f, "Scout core failed to reset.");
        Object.Destroy(scout.gameObject); yield return null;
        log("PASS role Scout: stationary recover, directional tilt, delayed small landing chips, extinguished/reset core.");

        var wisp = Spawn(Domain + "LeafWisp.prefab", Origin).GetComponent<DomainPrototypeEnemy>();
        wisp.enabled = false; Bind(wisp, vitals, playerHealth);
        MovePlayer(player, Origin + Vector3.forward * 1.4f);
        Collider wall = Box("QA rear wall", Origin + new Vector3(0, 1.5f, -1.2f), new Vector3(5, 3, .25f));
        object[] args = { Vector3.forward * 1.4f, true, Vector3.zero };
        Need((bool)Call(wisp, "ChooseWispShotPosition", args), "Wisp found no lateral point with rear wall.");
        Vector3 chosen = (Vector3)args[2];
        Need(Mathf.Abs(chosen.x - Origin.x) > .8f && chosen.z > Origin.z - .8f, "Rear-wall fallback selected blocked retreat.");
        Set(wisp, "hasWispShotPosition", true); Set(wisp, "wispShotPosition", chosen); Set(wisp, "wispDecisionTime", 1.2f);
        SetState(wisp, "Waiting");
        for (int i = 0; i < 12; i++) { Call(wisp, "RepositionWisp"); yield return null; }
        Need((Vector3)Field(wisp, "wispShotPosition") == chosen, "Wisp changes shooting point every frame.");
        SetState(wisp, "Recover"); Set(wisp, "timer", 1.85f); rest = wisp.transform.position;
        for (int i = 0; i < 8; i++) { Call(wisp, "RepositionWisp"); yield return null; }
        Need(Vector3.Distance(rest, wisp.transform.position) < .005f, "Wisp retreats during initial shot recovery.");
        Set(wisp, "timer", .1f); SetState(wisp, "Warning"); Call(wisp, "LockAim");
        Vector3 locked = (Vector3)Field(wisp, "lockedAimPosition"); MovePlayer(player, Origin + new Vector3(3, 0, 2));
        Call(wisp, "UpdateWarning"); Need((Vector3)Field(wisp, "lockedAimPosition") == locked, "Locked Wisp aim homes after commitment.");
        Need(Vector3.Distance(wisp.transform.position, Origin) <= 4.5f, "Wisp exceeded local encounter boundary.");
        Object.Destroy(wall.gameObject); yield return null;
        wisp.ResetForJourney(); wisp.enabled = true;
        MovePlayer(player, Origin + Vector3.forward * 5f); playerHealth.RestoreFullHealth();
        wall = Box("QA narrow shot cover", Origin + new Vector3(0, 1.5f, 2.5f), new Vector3(.6f, 3, .4f));
        Need(!(bool)Call(wisp, "HasClearShot"), "Shot cover fixture does not block sight.");
        args = new object[] { Vector3.forward * 5f, false, Vector3.zero };
        Need((bool)Call(wisp, "ChooseWispShotPosition", args), "No nearby shooting angle around narrow cover.");
        chosen = (Vector3)args[2];
        var muzzle = (Transform)Field(wisp, "muzzle");
        Need((bool)Call(wisp, "ClearLineToPlayer", chosen + muzzle.position - wisp.transform.position),
            "Chosen shooting angle is still occluded.");
        Object.Destroy(wall.gameObject); yield return null;
        yield return new WaitForSeconds(3.5f);
        Need(playerHealth.CurrentHealth < playerHealth.MaxHealth && Vector3.Distance(wisp.transform.position, Origin) <= 4.5f,
            "Autonomous ranged loop does not threaten player or exceeds its encounter boundary.");
        Object.Destroy(wisp.gameObject); yield return null;
        log("PASS role ranged Wisp: stable bounded shot point, rear-wall lateral fallback, stationary post-shot window and locked aim.");

        var prism = Spawn(Domain + "RuinPrism.prefab", Origin).GetComponent<DomainPrototypeEnemy>();
        Bind(prism, vitals, playerHealth); MovePlayer(player, Origin + Vector3.forward * 4f);
        var warning = (LineRenderer)Field(prism, "warningLine");
        foreach (DomainPrototypeEnemy.PrismPalette palette in Enum.GetValues(typeof(DomainPrototypeEnemy.PrismPalette)))
        {
            Set(prism, "prismPalette", palette); Call(prism, "BeginWarning"); Color before = warning.startColor;
            Call(prism, "Fire"); yield return null;
            Color after = warning.startColor;
            Need(new Vector3(before.r - after.r, before.g - after.g, before.b - after.b).magnitude < .025f,
                "Prism warning and released wave palette differ: " + palette);
        }
        rest = prism.transform.position; yield return new WaitForSeconds(.5f);
        Need(State(prism) == "Recover" && Vector3.Distance(rest, prism.transform.position) < .01f, "Prism moves during recovery.");
        MeshRenderer core = ((Transform)Field(prism, "prismCore")).GetComponent<MeshRenderer>(); core.GetPropertyBlock(block);
        Need(block.GetColor("_EmissionColor").maxColorComponent < .5f, "Recovering Prism core still looks attack-ready.");
        var baseCollider = prism.GetComponentInChildren<BoxCollider>(); Need(baseCollider && baseCollider.enabled && !baseCollider.isTrigger, "Live Prism base not solid.");
        prism.GetComponent<Health>().TryApplyDamage(new DamageInfo(1000, player.gameObject, rest, Vector3.forward));
        Need(baseCollider.enabled && !prism.GetComponent<CapsuleCollider>().enabled, "Prism corpse base lost solidity or dead core remains hittable.");
        prism.ResetForJourney(); Need(baseCollider.enabled && prism.GetComponent<CapsuleCollider>().enabled, "Prism collider reset failed.");
        Object.Destroy(prism.gameObject); yield return null;
        log("PASS role Prism: four matching warning/release palettes, stationary recovery, dim core, living/dead/reset solid base.");

        MovePlayer(player, Origin + Vector3.left * 10f);
        var beetle = Spawn("Assets/Art/Enemies/RailBeetle/RailBeetle.prefab", Origin).GetComponent<RailBeetleAI>();
        beetle.enabled = false;
        Set(beetle, "chargeHeading", Vector3.forward); Set(beetle, "activeChargeDistance", 16f);
        wall = Box("QA charge blocker", Origin + new Vector3(0, 1.5f, 4), new Vector3(6, 3, .3f));
        Call(beetle, "BuildWarningPath");
        int count = (int)Field(beetle, "warningPathCount"); var points = (Vector3[])Field(beetle, "warningPath");
        Need(count >= 2 && count < 33 && points[count - 1].z < Origin.z + 4f, "Charge warning passes through wall.");
        Object.Destroy(wall.gameObject); yield return null; Call(beetle, "BuildWarningPath");
        Need((int)Field(beetle, "warningPathCount") == 33, "Open-ground charge warning lost actual full reach.");
        Need((float)Field(beetle, "wallStunSeconds") > (float)Field(beetle, "recoverSeconds"), "Wall stun not longer than normal recover.");
        Object.Destroy(beetle.gameObject); yield return null;
        log("PASS role charger: supported warning path ends before solid obstacle, full reach in open ground, stronger wall stun.");

        // Two ordinary prefab guards. Only this temporary test arena gets a combination.
        var first = Spawn(Scout, Origin + Vector3.left * 1.6f).GetComponent<Health>();
        var second = Spawn(Scout, Origin + Vector3.right * 1.6f).GetComponent<Health>();
        first.GetComponent<StoneScoutRoadsideAI>().enabled = false; second.GetComponent<StoneScoutRoadsideAI>().enabled = false;
        var healer = Spawn(RepairWispBuilder.PrefabPath, Origin + Vector3.back * 2f).GetComponent<RepairWispAI>();
        var group = new GameObject("QA two ordinary guards and one repair").AddComponent<RepairWispGroup>();
        group.Configure(new[] { first, second }); healer.ConfigureGroup(group);
        MovePlayer(player, Origin + Vector3.forward * 3f); yield return null;
        Hit(first, player, 20); Hit(second, player, 20);
        int ticks = 0;
        while (!healer.BeamVisible && ticks++ < 110) yield return new WaitForFixedUpdate();
        Need(healer.BeamVisible && healer.RemainingRepairs == 3, "First heal late or charge spent before success.");
        rest = healer.transform.position;
        wall = Box("QA repair interruption", Vector3.Lerp(healer.transform.position, healer.RepairTarget.transform.position, .5f) + Vector3.up,
            new Vector3(.3f, 2, .3f));
        yield return new WaitForFixedUpdate(); yield return null;
        Need(!healer.BeamVisible && healer.RemainingRepairs == 3 && (float)Field(healer, "timer") < 1f,
            "Failed channel consumes a repair or waits full successful cooldown.");
        Object.Destroy(wall.gameObject); yield return null;
        ticks = 0;
        while (healer.SuccessfulRepairs == 0 && ticks++ < 180) yield return new WaitForFixedUpdate();
        Need(healer.SuccessfulRepairs == 1 && healer.RemainingRepairs == 2 && first.CurrentHealth + second.CurrentHealth == 98,
            "First successful heal not timely/effective on ordinary guards.");
        while (!healer.BeamVisible && ticks++ < 440) yield return new WaitForFixedUpdate();
        Need(healer.BeamVisible, "Second repair failed to begin.");
        rest = healer.transform.position; yield return new WaitForSeconds(.25f);
        Need(Vector3.Distance(healer.transform.position, rest) < .005f, "Healer moves while channeling.");
        healer.GetComponent<Health>().TryApplyDamage(new DamageInfo(1000, player.gameObject, rest, Vector3.forward));
        yield return null; Need(!healer.BeamVisible && healer.CurrentPhase == RepairWispAI.Phase.Dead, "Killing healer does not cancel support.");

        int slowHits = 0, earlyHits = 0, burstHits = 0, slowRepairs = 0;
        for (int strategy = 0; strategy < 3; strategy++)
        {
            first.GetComponent<StoneScoutCombatTarget>().ResetForJourney(); second.GetComponent<StoneScoutCombatTarget>().ResetForJourney();
            healer.ResetForJourney(); group.Alert();
            // Deterministic damage pacing isolates target-priority cost; not a claim of human input playthrough.
            int used = 0;
            if (strategy == 1)
                while (!healer.GetComponent<Health>().IsDead)
                { Hit(healer.GetComponent<Health>(), player, 20); used++; yield return new WaitForSeconds(1.8f); }
            foreach (Health guard in new[] { first, second })
            {
                int cap = 0;
                while (!guard.IsDead && cap++ < 8)
                { Hit(guard, player, 20); used++; yield return new WaitForSeconds(strategy == 2 ? .35f : 1.8f); }
                Need(guard.IsDead, "Finite-support fight did not terminate.");
            }
            if (strategy == 0)
            {
                slowRepairs = healer.SuccessfulRepairs;
                while (!healer.GetComponent<Health>().IsDead)
                { Hit(healer.GetComponent<Health>(), player, 20); used++; yield return new WaitForSeconds(1.8f); }
                slowHits = used;
            }
            else if (strategy == 1) earlyHits = used; else burstHits = used;
        }
        Need(slowRepairs >= 2 && slowHits > earlyHits && burstHits == 6,
            "Target priority has no measurable tradeoff: slow=" + slowHits + " early=" + earlyHits + " burst=" + burstHits + " heals=" + slowRepairs);
        log("PASS role support strategy (controlled 20-damage pacing, two ordinary 60-HP guards): guard-first slow=" + slowHits +
            " hits; support-first=" + earlyHits + " hits; burst guards=" + burstHits + " hits; slow heals=" + slowRepairs + ". Not a human WASD playthrough.");
        Need(healer.RemainingRepairs >= 0 && healer.RemainingRepairs <= 3 && !healer.BeamVisible,
            "Support exceeds finite budget or heals a dead guard.");
        first.GetComponent<StoneScoutCombatTarget>().ResetForJourney(); second.GetComponent<StoneScoutCombatTarget>().ResetForJourney();
        healer.ResetForJourney(); first.GetComponent<StoneScoutRoadsideAI>().enabled = true;
        second.GetComponent<StoneScoutRoadsideAI>().enabled = true;
        MovePlayer(player, Origin + Vector3.forward * 3f); playerHealth.RestoreFullHealth();
        Hit(first, player, 20); Hit(second, player, 20);
        for (int i = 0; i < 4; i++)
        {
            yield return new WaitForSeconds(2f);
            log("Live combination t=" + (i + 1) * 2 + " player=" + player.position + " HP=" + playerHealth.CurrentHealth +
                " first=" + first.transform.position + " state=" + State(first.GetComponent<StoneScoutRoadsideAI>()) +
                " settle=" + Field(first.GetComponent<StoneScoutRoadsideAI>(), "attackSettleTime") +
                " contact=" + Call(first.GetComponent<StoneScoutRoadsideAI>(), "HorizontalContactDistance") +
                " second=" + second.transform.position + " state=" + State(second.GetComponent<StoneScoutRoadsideAI>()));
        }
        Need(healer.SuccessfulRepairs > 0 && playerHealth.CurrentHealth < playerHealth.MaxHealth && !playerHealth.IsDead,
            "Live two-guard encounter does not combine healing and real punch contact: heals=" + healer.SuccessfulRepairs +
            " player=" + playerHealth.CurrentHealth + " guard states=" + State(first.GetComponent<StoneScoutRoadsideAI>()) + "/" + State(second.GetComponent<StoneScoutRoadsideAI>()));
        log("PASS autonomous flat combination: two live guards approach/punch for real damage while the repair wisp completes finite healing.");
        Object.Destroy(first.gameObject); Object.Destroy(second.gameObject); Object.Destroy(healer.gameObject); Object.Destroy(group.gameObject);
        Object.Destroy(navRoot); foreach (var baked in meshData) Object.Destroy(baked);
        yield return null;
    }

    static GameObject Spawn(string path, Vector3 point) => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), point + Vector3.up * .015f, Quaternion.identity);
    static Collider Box(string name, Vector3 point, Vector3 size)
    { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = point; go.transform.localScale = size; Physics.SyncTransforms(); return go.GetComponent<Collider>(); }
    static void MovePlayer(Transform player, Vector3 point)
    { var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.position = point + Vector3.up * .015f; cc.enabled = true; Physics.SyncTransforms(); }
    static void Hit(Health actor, Transform player, int damage) => actor.TryApplyDamage(new DamageInfo(damage, player.gameObject, actor.transform.position, Vector3.forward));
    static void Bind(DomainPrototypeEnemy enemy, PlayerVitals player, Health health) { Set(enemy, "player", player); Set(enemy, "playerHealth", health); }
    static object Field(object actor, string name) => actor.GetType().GetField(name, Private).GetValue(actor);
    static void Set(object actor, string name, object value) => actor.GetType().GetField(name, Private).SetValue(actor, value);
    static object Call(object actor, string name, params object[] args) => actor.GetType().GetMethod(name, Private).Invoke(actor, args);
    static string State(object actor) => Field(actor, "state").ToString();
    static void SetState(object actor, string value)
    { var field = actor.GetType().GetField("state", Private); field.SetValue(actor, Enum.Parse(field.FieldType, value)); }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
