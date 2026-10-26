using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// The room is created only during play and never replaces the authored main world.
[DisallowMultipleComponent]
public sealed class BaselineEndingRoom : MonoBehaviour
{
    public Transform player;
    public Material barrierMaterial;
    public Material floorMaterial;
    public Material targetMaterial;
    public Material backdropAccentMaterial;

    public bool IsInside { get; private set; }
    public bool Completed { get; private set; }
    public bool IntroDialogueActive => introDialogue && introDialogue.IsShowing;
    public string LastMessage { get; private set; }
    public event Action Won;

    private static readonly Vector3 RoomPosition = new Vector3(320f, 0f, 0f);
    private const float Radius = 16f;
    private const float FloorTop = 1f;
    private const float WallHeight = 8f;
    private const int WallSegments = 64;

    private GameObject room;
    private GameObject target;
    private Health targetHealth;
    private GuardianIntroDialogue introDialogue;
    private BaselineFlow flow;
    private GUIStyle bossNameStyle;
    private GUIStyle bossValueStyle;
    private GUIStyle bossNameShadowStyle;
    private GUIStyle bossValueShadowStyle;
    private Texture2D bossArtwork;
    private Material bossHealthMaterial;
    private CharacterController controller;
    private Vector3 towerPosition;
    private Quaternion towerRotation;
    private bool hasTowerPosition;

    public bool Enter()
    {
        if (!isActiveAndEnabled || IsInside) return false;
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            LastMessage = "The player is not ready to enter the room.";
            return false;
        }

        controller = player.GetComponent<CharacterController>();
        if (controller == null || !controller.enabled)
        {
            LastMessage = "The player is not ready to enter the room.";
            return false;
        }

        BuildRoom();
        towerPosition = player.position;
        towerRotation = player.rotation;
        hasTowerPosition = true;
        room.SetActive(true);
        IsInside = true;

        float bottomOffset, topOffset, capsuleRadius;
        GetCapsuleDimensions(out bottomOffset, out topOffset, out capsuleRadius);
        Vector3 arrival = RoomPosition + new Vector3(0f, FloorTop - bottomOffset + .05f, -8f);
        MovePlayer(arrival, Quaternion.identity, true);
        if (!Completed)
        {
            if (!introDialogue) introDialogue = GetComponent<GuardianIntroDialogue>() ?? gameObject.AddComponent<GuardianIntroDialogue>();
            introDialogue.Show(player);
        }
        LastMessage = Completed ? "This room is already complete." : "Face the Guardian to finish the trial.";
        return true;
    }

    public void ReturnToTower()
    {
        if (!IsInside) return;
        if (introDialogue) introDialogue.DismissImmediately();
        // Stop enforcing room bounds before relocating the player into the main world.
        IsInside = false;
        if (player != null && hasTowerPosition)
            MovePlayer(towerPosition, towerRotation, true);
        if (room != null) room.SetActive(false);
        hasTowerPosition = false;
    }

    // Loading a save or starting a new game never fires a new victory event.
    public void SetCompleted(bool completed)
    {
        Completed = completed;
        if (room == null) return;
        if (completed)
        {
            if (target != null) target.SetActive(false);
        }
        else BuildTarget();
    }

    private void BuildRoom()
    {
        if (room != null) return;
        room = new GameObject("Ending Room (runtime)");
        room.SetActive(false);
        room.hideFlags = HideFlags.DontSave;
        room.transform.position = RoomPosition;
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            SceneManager.MoveGameObjectToScene(room, gameObject.scene);

        // PlayerMovement recognizes this marker on collider ancestors. Layer 2 alone
        // would not prevent climbing because its climb queries explicitly include all layers.
        room.AddComponent<NonClimbableSurface>();
        GameObject floor = MakeShape("Circular platform", PrimitiveType.Cylinder,
            room.transform, new Vector3(0f, .5f, 0f), new Vector3(Radius * 2f, .5f, Radius * 2f), floorMaterial);
        // Unity's cylinder primitive has a capsule collider. Use its flat mesh for a flat floor.
        Collider primitiveCollider = floor.GetComponent<Collider>();
        if (primitiveCollider != null) { primitiveCollider.enabled = false; Destroy(primitiveCollider); }
        floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;

        GameObject barrier = new GameObject("Pixel shield");
        barrier.transform.SetParent(room.transform, false);
        barrier.transform.localPosition = Vector3.up * (FloorTop + WallHeight * .5f);
        PixelShieldController shield = barrier.AddComponent<PixelShieldController>();
        shield.ConfigureGeometry(Radius, WallHeight, 128);
        shield.ConfigureMaterial(barrierMaterial);

        const float thickness = .4f;
        float wallRadius = Radius + thickness * .5f;
        float width = 2f * wallRadius * Mathf.Tan(Mathf.PI / WallSegments) + .05f;
        for (int i = 0; i < WallSegments; i++)
        {
            float angle = i * 2f * Mathf.PI / WallSegments;
            Vector3 radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            GameObject wall = new GameObject("Barrier collision " + i);
            wall.transform.SetParent(room.transform, false);
            wall.transform.localPosition = radial * wallRadius + Vector3.up * (FloorTop + WallHeight * .5f);
            wall.transform.localRotation = Quaternion.LookRotation(radial);
            wall.AddComponent<BoxCollider>().size = new Vector3(width, WallHeight, thickness);
        }

        // Physical overhead cap, plus the local bounds check, prevents escaping above the shield.
        GameObject cap = new GameObject("Barrier overhead collision");
        cap.transform.SetParent(room.transform, false);
        cap.transform.localPosition = Vector3.up * (FloorTop + WallHeight + .2f);
        cap.AddComponent<BoxCollider>().size = new Vector3(Radius * 2f, .4f, Radius * 2f);
        BuildBackdrop();
        BuildTarget();
    }

    private void BuildBackdrop()
    {
        // Provisional spatial context only; final background imagery remains an art decision.
        // Everything lives outside the playable ring, has no collider and shares existing materials.
        GameObject backdrop = new GameObject("Provisional virtual fragments");
        backdrop.transform.SetParent(room.transform, false);
        for (int i = 0; i < 16; i++)
        {
            float angle = i * Mathf.PI * 2f / 16f + Mathf.Sin(i * 1.9f) * .08f;
            float distance = 31f + (i % 4) * 4.5f;
            float height = 3f + (i % 5) * 1.5f;
            Vector3 position = new Vector3(Mathf.Sin(angle) * distance,
                (i % 3 - 1) * 3.5f + height * .15f, Mathf.Cos(angle) * distance);
            Vector3 scale = new Vector3(1.8f + (i % 3) * .8f, height, 1.7f + (i % 4) * .6f);
            Color tint = Color.Lerp(new Color(.17f, .23f, .28f),
                new Color(.28f, .38f, .44f), (i % 4) / 3f);
            GameObject fragment = MakeBackdropShape("Virtual fragment " + i,
                backdrop.transform, position, scale, floorMaterial, tint);
            fragment.transform.localRotation = Quaternion.Euler(
                Mathf.Sin(i * 1.4f) * 10f, angle * Mathf.Rad2Deg + 22f, Mathf.Cos(i * 1.7f) * 12f);

            if (i % 2 != 0) continue;
            // A narrow edge strip adds a readable silhouette through the transparent shield.
            MakeBackdropShape("Cyan edge", fragment.transform,
                new Vector3(.51f, 0f, 0f), new Vector3(.035f, .92f, 1.02f),
                backdropAccentMaterial != null ? backdropAccentMaterial : floorMaterial,
                new Color(.12f, .85f, .94f));
        }
    }

    private GameObject MakeBackdropShape(string name, Transform parent, Vector3 position,
        Vector3 scale, Material material, Color tint)
    {
        GameObject shape = MakeShape(name, PrimitiveType.Cube, parent, position, scale, material);
        Collider collider = shape.GetComponent<Collider>();
        if (collider != null) { collider.enabled = false; Destroy(collider); }
        Renderer renderer = shape.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", tint);
        properties.SetColor("_Color", tint);
        renderer.SetPropertyBlock(properties);
        return shape;
    }

    private void BuildTarget()
    {
        if (targetHealth != null) targetHealth.Died -= OnTargetDied;
        if (target != null) { target.SetActive(false); Destroy(target); }

        target = new GameObject("Passive final figure");
        target.transform.SetParent(room.transform, false);
        target.transform.localPosition = new Vector3(0f, FloorTop, 3.5f);
        // One collider and default neutral Health allow the existing PlayerCombat hit window
        // to apply damage. No AI, attacks, animation controller or extra combat rules are added.
        CapsuleCollider hitbox = target.AddComponent<CapsuleCollider>();
        hitbox.center = new Vector3(0f, 1.25f, 0f);
        hitbox.height = 2.5f;
        hitbox.radius = .6f;
        targetHealth = target.AddComponent<Health>();
        targetHealth.SetMaximumHealth(200);
        targetHealth.Died += OnTargetDied;

        MakeTargetPart("Torso", PrimitiveType.Cube, new Vector3(0f, 1.4f, 0f), new Vector3(.7f, .9f, .4f));
        MakeTargetPart("Head", PrimitiveType.Sphere, new Vector3(0f, 2.12f, 0f), Vector3.one * .5f);
        MakeTargetPart("Left arm", PrimitiveType.Cube, new Vector3(-.52f, 1.36f, 0f), new Vector3(.24f, .95f, .26f));
        MakeTargetPart("Right arm", PrimitiveType.Cube, new Vector3(.52f, 1.36f, 0f), new Vector3(.24f, .95f, .26f));
        MakeTargetPart("Left leg", PrimitiveType.Cube, new Vector3(-.21f, .45f, 0f), new Vector3(.27f, .9f, .3f));
        MakeTargetPart("Right leg", PrimitiveType.Cube, new Vector3(.21f, .45f, 0f), new Vector3(.27f, .9f, .3f));
        target.SetActive(!Completed);
    }

    private void MakeTargetPart(string name, PrimitiveType primitive, Vector3 position, Vector3 scale)
    {
        GameObject part = MakeShape(name, primitive, target.transform, position, scale, targetMaterial);
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) { collider.enabled = false; Destroy(collider); }
    }

    private static GameObject MakeShape(string name, PrimitiveType primitive, Transform parent,
        Vector3 position, Vector3 scale, Material material)
    {
        GameObject shape = GameObject.CreatePrimitive(primitive);
        shape.name = name;
        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = position;
        shape.transform.localScale = scale;
        if (material != null) shape.GetComponent<Renderer>().sharedMaterial = material;
        return shape;
    }

    private void OnTargetDied(Health health, DamageInfo damage)
    {
        if (!IsInside || Completed || health != targetHealth) return;
        if (introDialogue) introDialogue.DismissImmediately();
        Completed = true;
        target.SetActive(false);
        LastMessage = "Trial complete.";
        // The room and barrier remain active until the flow explicitly returns to the tower.
        Won?.Invoke();
    }

    private void LateUpdate()
    {
        if (!IsInside || player == null || room == null || !room.activeInHierarchy) return;
        float bottomOffset, topOffset, capsuleRadius;
        GetCapsuleDimensions(out bottomOffset, out topOffset, out capsuleRadius);
        Vector3 position = player.position;
        Vector2 planar = new Vector2(position.x - RoomPosition.x, position.z - RoomPosition.z);
        float limit = Mathf.Max(1f, Radius - capsuleRadius - .15f);
        if (planar.sqrMagnitude > limit * limit)
        {
            planar = planar.normalized * limit;
            position.x = RoomPosition.x + planar.x;
            position.z = RoomPosition.z + planar.y;
        }
        float minimumY = RoomPosition.y + FloorTop - bottomOffset + .02f;
        float maximumY = Mathf.Max(minimumY, RoomPosition.y + FloorTop + WallHeight - topOffset - .05f);
        position.y = Mathf.Clamp(position.y, minimumY, maximumY);
        if ((position - player.position).sqrMagnitude > .000001f)
            MovePlayer(position, player.rotation, false);
    }

    private void GetCapsuleDimensions(out float bottomOffset, out float topOffset, out float radius)
    {
        if (controller == null)
        {
            bottomOffset = 0f; topOffset = 2f; radius = .4f;
            return;
        }
        Vector3 scale = player.lossyScale;
        radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfHeight = Mathf.Max(controller.height * Mathf.Abs(scale.y) * .5f, radius);
        float centerOffset = player.TransformPoint(controller.center).y - player.position.y;
        bottomOffset = centerOffset - halfHeight;
        topOffset = centerOffset + halfHeight;
    }

    private void MovePlayer(Vector3 position, Quaternion rotation, bool moveCamera)
    {
        Vector3 displacement = position - player.position;
        if (moveCamera)
        {
            var combat=player.GetComponent<PlayerCombat>(); if(combat)combat.CancelForTransition();
            var movement=player.GetComponent<PlayerMovement>();
            if(movement){movement.Relocate(position,rotation);Physics.SyncTransforms();var main=Camera.main;if(main && !main.transform.IsChildOf(player))main.transform.position+=displacement;return;}
        }
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        player.SetPositionAndRotation(position, rotation);
        if (wasEnabled) controller.enabled = true;
        Physics.SyncTransforms();
        Camera camera = Camera.main;
        if (moveCamera && camera != null && !camera.transform.IsChildOf(player))
            camera.transform.position += displacement;
    }

    private void OnGUI()
    {
        if (!IsInside || Completed || IntroDialogueActive || targetHealth == null) return;
        if (flow == null) flow = GetComponent<BaselineFlow>();
        if (flow != null && (flow.MenuOpen || flow.MapOpen || flow.VictoryOpen || flow.DeathOpen)) return;
        if (bossArtwork == null) bossArtwork = Resources.Load<Texture2D>("BossHealthUI/FinalFigureBar");
        if (bossHealthMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("BossHealthUI/BossHealthFill");
            if (shader != null && shader.isSupported)
                bossHealthMaterial = new Material(shader) { name = "Live final figure health", hideFlags = HideFlags.DontSave };
        }
        if (bossNameStyle == null)
        {
            bossNameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 20
            };
            bossNameStyle.normal.textColor = WayfarerUITheme.Ink;
            WayfarerUITheme.KeepLabelColor(bossNameStyle);
            bossValueStyle = new GUIStyle(bossNameStyle) { fontSize = 14 };
            bossNameShadowStyle = new GUIStyle(bossNameStyle);
            bossNameShadowStyle.normal.textColor = WayfarerUITheme.Gold;
            WayfarerUITheme.KeepLabelColor(bossNameShadowStyle);
            bossValueShadowStyle = new GUIStyle(bossNameShadowStyle) { fontSize = 14 };
        }
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.identity;
        float width = Mathf.Min(Screen.width - 32f, Mathf.Clamp(Screen.width * .68f, 520f, 860f));
        float x = (Screen.width - width) * .5f;
        float imageHeight = width * 724f / 2172f * .7f;
        float imageY = 38f - imageHeight * 320f / 724f;
        if (bossArtwork != null && bossHealthMaterial != null && Event.current.type == EventType.Repaint)
        {
            bossHealthMaterial.SetFloat("_Health", targetHealth.NormalizedHealth);
            GUI.color = Color.white;
            Graphics.DrawTexture(new Rect(x, imageY, width, imageHeight), bossArtwork, bossHealthMaterial);
        }
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 1f, 5f, width, 25f), "GUARDIAN", bossNameShadowStyle);
        GUI.Label(new Rect(x, 4f, width, 25f), "GUARDIAN", bossNameStyle);
        float valueY = imageY + imageHeight * 420f / 724f;
        GUI.Label(new Rect(x + 1f, valueY + 1f, width, 20f),
            targetHealth.CurrentHealth + " / " + targetHealth.MaxHealth, bossValueShadowStyle);
        GUI.Label(new Rect(x, valueY, width, 20f),
            targetHealth.CurrentHealth + " / " + targetHealth.MaxHealth, bossValueStyle);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void OnDisable()
    {
        // Suspending the host for a side-world visit while outside has no player side effects.
        if (IsInside) ReturnToTower();
        else if (room != null) room.SetActive(false);
    }

    private void OnDestroy()
    {
        if (bossHealthMaterial != null) Destroy(bossHealthMaterial);
        if (targetHealth != null) targetHealth.Died -= OnTargetDied;
        if (room != null) Destroy(room);
    }
}
