using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StoneScoutCombatPrototypeChecks
{
    private const string PrefabPath = "Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab";
    private const string ScenePath = "Assets/_Demo/Scenes/OverworldBaseline.unity";

    [MenuItem("Tools/Art/Check Stone Scout Combat Prototype")]
    public static void Run()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Stone Scout combat prefab is missing.");

        Health health = prefab.GetComponent<Health>();
        CapsuleCollider body = prefab.GetComponent<CapsuleCollider>();
        Rigidbody rigidbody = prefab.GetComponent<Rigidbody>();
        if (health == null || health.Team != HealthTeam.Enemy || health.MaxHealth != 60 ||
            body == null || body.isTrigger || !body.enabled ||
            rigidbody == null || !rigidbody.isKinematic || rigidbody.useGravity ||
            prefab.GetComponent<StoneScoutCombatTarget>() == null ||
            prefab.GetComponent<StoneScoutRoadsideAI>() == null ||
            prefab.GetComponent<StoneScoutLootDrop>() == null ||
            prefab.GetComponent<StoneScoutFeedback>() == null ||
            prefab.GetComponent<CombatFeedback>() == null ||
            prefab.GetComponent<NonClimbableSurface>() == null)
            throw new InvalidOperationException("Stone Scout combat components are incomplete.");
        if (prefab.GetComponentInChildren<StoneScoutVisualMotion>(true) == null)
            throw new InvalidOperationException("The combat prefab needs the animated visual.");
        var ai = new SerializedObject(prefab.GetComponent<StoneScoutRoadsideAI>());
        float noticeRange = ai.FindProperty("noticeRange").floatValue;
        float leashRange = ai.FindProperty("leashRange").floatValue;
        if (noticeRange < 7f || leashRange < noticeRange + 2f)
            throw new InvalidOperationException("Stone Scout detection and pursuit ranges are too short for the roadside encounter.");
        if (ai.FindProperty("attackRange").floatValue > 1.1f ||
            ai.FindProperty("fistHitRadius").floatValue > 0.35f ||
            ai.FindProperty("hitWindowSeconds").floatValue > 0.2f)
            throw new InvalidOperationException("Stone Scout melee settings are wider than the visible fist swing.");
        if (ai.FindProperty("lostSightSeconds").floatValue < 0.5f ||
            ai.FindProperty("lostSightSeconds").floatValue > 2f ||
            ai.FindProperty("turnDegreesPerSecond").floatValue < 180f)
            throw new InvalidOperationException("Stone Scout sight memory or turning is outside its small-enemy tuning range.");
        if (ai.FindProperty("patrolRadius").floatValue < 0.5f ||
            ai.FindProperty("patrolRadius").floatValue > 1.5f ||
            ai.FindProperty("alertSeconds").floatValue < 0.25f ||
            ai.FindProperty("squadLinkRange").floatValue < 10f)
            throw new InvalidOperationException("Stone Scout needs a short patrol and visible alert pause.");
        var loot = new SerializedObject(prefab.GetComponent<StoneScoutLootDrop>());
        if (loot.FindProperty("shardMaterial").objectReferenceValue == null ||
            loot.FindProperty("pickupClip").objectReferenceValue == null ||
            loot.FindProperty("healAmount").intValue != 15)
            throw new InvalidOperationException("Stone Scout needs its transient healing shard.");
        var feel = new SerializedObject(prefab.GetComponent<StoneScoutFeedback>());
        if (feel.FindProperty("glowMaterial").objectReferenceValue == null ||
            feel.FindProperty("windupClip").objectReferenceValue == null ||
            feel.FindProperty("playerHitClip").objectReferenceValue == null ||
            feel.FindProperty("stoneHitClip").objectReferenceValue == null)
            throw new InvalidOperationException("Stone Scout warning, impact, and damage feedback are incomplete.");

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Transform root = null;
            foreach (GameObject gameObject in scene.GetRootGameObjects())
                if (gameObject.name == "_EnemyCombatPreview") root = gameObject.transform;
            if (root == null || root.childCount != 6)
                throw new InvalidOperationException("Expected six roadside Stone Scouts in the overworld.");

            for (int i = 0; i < root.childCount; i++)
            {
                Transform enemy = root.GetChild(i);
                if (enemy.GetComponent<Health>() == null ||
                    enemy.GetComponent<StoneScoutCombatTarget>() == null ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(enemy.gameObject) != PrefabPath)
                    throw new InvalidOperationException("Roadside enemy " + i + " is not the combat prefab.");
                if (!EnemyFoundationPlacementChecks.TryFitSpawn(enemy.GetComponent<Health>(), enemy.position, out Vector3 fitted) ||
                    Mathf.Abs(enemy.position.y - fitted.y) > .05f)
                    throw new InvalidOperationException(enemy.name + " is not on walkable V2 terrain.");
            }

            Debug.Log("Stone Scout combat prototype PASS: enemy Health, kinematic body, " +
                      "patrol/alert/turn-taking AI, reactions, healing drop, six scene instances, and terrain heights.");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}
