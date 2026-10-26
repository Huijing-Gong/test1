using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Isolated visual explanations, NOT gameplay footage or a second combat simulation.
/// Only meshes, bones and an event-free Animator are copied. No live input,
/// physics, damage, audio, camera shake, progression or global clocks are driven.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleQuickGuidePreview : MonoBehaviour
{
    [Flags] public enum DemoKeys { None = 0, W = 1, A = 2, S = 4, D = 8, Space = 16, J = 32, K = 64 }
    public RenderTexture Texture { get; private set; }
    public DemoKeys Keys { get; private set; }
    public string ActionLabel { get; private set; } = "VISUAL DEMONSTRATION";
    public float Progress { get; private set; }
    public bool Ready { get; private set; }
    public string Error { get; private set; }

    const int PreviewLayer = 31;
    static readonly Color Cream = new Color32(246, 234, 198, 255);
    static readonly Color Mint = new Color32(125, 222, 199, 255);
    static readonly Color Gold = new Color32(241, 190, 79, 255);
    static readonly Color Ink = new Color32(16, 54, 48, 255);
    GameObject stage;
    Camera previewCamera;
    Actor blue, red;
    Material inkMaterial, ghostMaterial;
    BattleJPresentation.InkMesh floor, ink;
    readonly List<Material> materials = new List<Material>();
    readonly List<Ghost> ghosts = new List<Ghost>();
    float shownAt, nextFrame, previousTime, nextGhostAt;
    int page, sourceId;
    bool visible;

    sealed class Actor
    {
        public GameObject root;
        public Transform model;
        public Animator animator;
        public BattleHumanoidRig rig;
        public PlayableGraph graph;
        public AnimationPlayableOutput output;
        public AnimationClipPlayable playable;
        public AnimationClip current, idle, run, jump, fall, jab, cross, hook, hit;
        public AnimationClip[] clips;
        public SkinnedMeshRenderer[] skins;
        public float height;
        public Vector3 Body => root.transform.position + Vector3.up * (height * .52f);
    }

    sealed class Ghost
    {
        public Mesh[] meshes;
        public MeshRenderer[] renderers;
        public Transform[] nodes;
        public float until;
        public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    }

    public void Show(Animator source, int requestedPage)
    {
        Error = null;
        if (!source || !source.avatar || !source.runtimeAnimatorController)
        {
            Ready = false;
            Error = "The real fighter visual is not available yet.";
            Hide();
            return;
        }
        try
        {
            if (!stage || sourceId != source.GetInstanceID())
            {
                Release();
                Build(source);
                sourceId = source.GetInstanceID();
            }
            page = Mathf.Clamp(requestedPage, 0, 4);
            // The second jump must remain fully inside the demonstration,
            // including the hood, rather than cropping the top of the leap.
            previewCamera.orthographicSize = page == 0 ? 2.4f : 2.24f;
            previewCamera.transform.localPosition = new Vector3(0f, page == 0 ? 1.8f : 1.43f, -12f);
            shownAt = Time.unscaledTime;
            nextFrame = 0f; previousTime = -1f; nextGhostAt = 0f;
            Keys = DemoKeys.None; Progress = 0f;
            ClearGhosts();
            stage.SetActive(true);
            blue.graph.Play(); red.graph.Play();
            visible = Ready = true;
            RenderFrame(0f);
        }
        catch (Exception exception)
        {
            Error = "Visual preview unavailable: " + exception.Message;
            Ready = false;
            Hide();
        }
    }

    public void Hide()
    {
        visible = false;
        Keys = DemoKeys.None;
        if (blue != null && blue.graph.IsValid()) blue.graph.Stop();
        if (red != null && red.graph.IsValid()) red.graph.Stop();
        if (stage) stage.SetActive(false);
    }

    void Build(Animator source)
    {
        stage = new GameObject("Side A quick-guide visual stage") { hideFlags = HideFlags.DontSave };
        stage.SetActive(false);
        stage.transform.SetParent(transform, false);
        stage.transform.position = new Vector3(2600f, 80f, 2600f);
        stage.layer = PreviewLayer;
        Shader shader = Resources.Load<Shader>("VFX/BattleJInk");
        if (!shader) throw new InvalidOperationException("Side A ink shader is missing.");
        inkMaterial = Own(new Material(shader) { name = "Quick guide ink (private)", hideFlags = HideFlags.DontSave });
        floor = new BattleJPresentation.InkMesh("Visual platform (no physics)", stage.transform, inkMaterial);
        ink = new BattleJPresentation.InkMesh("Visual-only move cues", stage.transform, inkMaterial);
        floor.renderer.gameObject.layer = ink.renderer.gameObject.layer = PreviewLayer;
        floor.Begin(stage.transform.position);
        Rect(floor, -12f, -.24f, 24f, .24f, Ink, .35f);
        Rect(floor, -12f, -.05f, 24f, .07f, Cream, .30f);
        Rect(floor, -12f, -.12f, 24f, .025f, Gold, .25f);
        for (int i = -6; i <= 6; i++) floor.Line(new Vector2(i * 1.4f, -.05f), new Vector2(i * 1.4f + .08f, .02f), .013f, Ink, .20f);
        floor.End();
        blue = BuildActor(source, false);
        red = BuildActor(source, true);
        var cameraObject = new GameObject("Quick guide camera (layer 31 only)", typeof(Camera));
        cameraObject.transform.SetParent(stage.transform, false);
        cameraObject.layer = PreviewLayer;
        cameraObject.transform.localPosition = new Vector3(0f, 1.43f, -12f);
        cameraObject.transform.localRotation = Quaternion.identity;
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.orthographic = true; previewCamera.orthographicSize = 2.24f;
        previewCamera.nearClipPlane = .1f; previewCamera.farClipPlane = 22f;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(.045f, .10f, .088f, 1f);
        previewCamera.allowHDR = previewCamera.allowMSAA = false;
        var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = data.renderShadows = false;
        data.requiresColorTexture = data.requiresDepthTexture = false; data.volumeLayerMask = 0;
        Texture = new RenderTexture(640, 360, 16, RenderTextureFormat.ARGB32)
        { name = "Side A quick guide 640x360", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Texture.Create(); previewCamera.targetTexture = Texture;
        Shader ghostShader = Resources.Load<Shader>("VFX/BattleGhost");
        if (ghostShader)
        {
            ghostMaterial = Own(new Material(ghostShader) { name = "Quick guide afterimages", hideFlags = HideFlags.DontSave });
            for (int slot = 0; slot < 3; slot++) BuildGhost(slot);
        }
    }

    Actor BuildActor(Animator source, bool rival)
    {
        var actor = new Actor();
        actor.root = new GameObject(rival ? "Red visual-only dummy" : "Blue visual-only actor") { layer = PreviewLayer };
        actor.root.transform.SetParent(stage.transform, false);
        var map = new Dictionary<Transform, Transform>();
        actor.model = CopyTransforms(source.transform, actor.root.transform, map);
        actor.model.localPosition = Vector3.zero;
        actor.model.localRotation = Quaternion.Euler(0f, 90f, 0f);
        actor.model.localScale = source.transform.lossyScale;
        foreach (var original in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!original.sharedMesh || !map.TryGetValue(original.transform, out Transform node)) continue;
            var renderer = node.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = original.sharedMesh;
            var bones = original.bones; var copiedBones = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++) if (bones[i]) map.TryGetValue(bones[i], out copiedBones[i]);
            renderer.bones = copiedBones;
            renderer.rootBone = original.rootBone && map.TryGetValue(original.rootBone, out Transform rootBone) ? rootBone : actor.model;
            renderer.localBounds = original.localBounds;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterials = CopyMaterials(original.sharedMaterials, rival);
            for (int i = 0; i < original.sharedMesh.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, original.GetBlendShapeWeight(i));
            ConfigureRenderer(renderer);
        }
        foreach (var original in source.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = original.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh || !map.TryGetValue(original.transform, out Transform node)) continue;
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = CopyMaterials(original.sharedMaterials, rival); ConfigureRenderer(renderer);
        }
        actor.animator = actor.model.gameObject.AddComponent<Animator>();
        actor.animator.avatar = source.avatar;
        actor.animator.applyRootMotion = false; actor.animator.fireEvents = false;
        actor.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        actor.animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        // Do not attach the RuntimeAnimatorController: its StateMachineBehaviours
        // must not execute. Only its existing animation clips are read.
        actor.clips = source.runtimeAnimatorController.animationClips;
        actor.idle = Find(actor, "Idle_Loop", "Idle", "Idle01");
        actor.run = Find(actor, "Run_Loop", "Running", "Run", "Walk_Loop");
        actor.jump = Find(actor, "Jump_Start", "Jump", "Jump_Loop");
        actor.fall = Find(actor, "Jump_Loop", "Fall", "Falling", "Jump");
        actor.jab = Find(actor, "Punch_Jab", "Punch", "Attack");
        actor.cross = Find(actor, "Punch_Cross", "Punch", "Attack");
        actor.hook = Find(actor, "Melee_Hook", "Punch_Cross", "Attack");
        actor.hit = Find(actor, "Hit_Chest", "Hit", "HitReact", "Idle_Loop");
        if (!actor.idle) throw new InvalidOperationException("The fighter controller has no reusable clips.");
        actor.graph = PlayableGraph.Create(rival ? "Quick guide dummy pose" : "Quick guide blue pose");
        actor.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        actor.output = AnimationPlayableOutput.Create(actor.graph, "Visual pose only", actor.animator);
        actor.graph.Play();
        actor.skins = actor.root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        // Activate only our newly constructed safe hierarchy to initialise the
        // Animator. No component from the real fighter was instantiated.
        actor.root.SetActive(true);
        stage.SetActive(true);
        Sample(actor, actor.idle, 0f, 1f);
        Bounds bounds = BoundsOf(actor);
        actor.model.position += Vector3.up * (stage.transform.position.y - bounds.min.y);
        actor.height = Mathf.Clamp(bounds.size.y, 1f, 3f);
        actor.rig = BattleHumanoidRig.Create(actor.animator);
        return actor;
    }

    static Transform CopyTransforms(Transform source, Transform parent, Dictionary<Transform, Transform> map)
    {
        var node = new GameObject(source.name) { layer = PreviewLayer };
        node.transform.SetParent(parent, false);
        node.transform.localPosition = source.localPosition;
        node.transform.localRotation = source.localRotation;
        node.transform.localScale = source.localScale;
        map.Add(source, node.transform);
        foreach (Transform child in source) CopyTransforms(child, node.transform, map);
        return node.transform;
    }

    Material[] CopyMaterials(Material[] originals, bool rival)
    {
        var result = new Material[originals.Length];
        for (int i = 0; i < originals.Length; i++)
        {
            Material original = originals[i];
            Shader shader = original ? original.shader : Shader.Find("Universal Render Pipeline/Lit");
            bool reveal = shader && shader.name == "SideA/Intro Reveal";
            bool team = original && (original.HasProperty("_RecolorTeam") ? original.GetFloat("_RecolorTeam") > .5f : original.HasProperty("_TeamColor"));
            if (reveal)
            {
                Shader clean = Shader.Find(team ? "SideA/HumanoidTeamLit" : "Universal Render Pipeline/Lit");
                if (clean) shader = clean;
            }
            var material = Own(new Material(shader) { name = "Quick guide clean " + (rival ? "red" : "blue"), hideFlags = HideFlags.DontSave });
            if (original) material.CopyPropertiesFromMaterial(original);
            if (material.HasProperty("_RevealHeight")) material.SetFloat("_RevealHeight", 10000f);
            if (material.HasProperty("_Glow")) material.SetFloat("_Glow", 0f);
            if (team && material.HasProperty("_TeamColor")) material.SetColor("_TeamColor", rival ? new Color(.86f, .085f, .055f) : new Color(.055f, .42f, .92f));
            result[i] = material;
        }
        return result;
    }

    static void ConfigureRenderer(Renderer renderer)
    {
        renderer.enabled = true; renderer.forceRenderingOff = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.SetPropertyBlock(null);
    }

    static AnimationClip Find(Actor actor, params string[] names)
    {
        foreach (string name in names)
            foreach (var clip in actor.clips)
                if (clip && (string.Equals(clip.name, name, StringComparison.OrdinalIgnoreCase) ||
                    clip.name.EndsWith("|" + name, StringComparison.OrdinalIgnoreCase))) return clip;
        foreach (string name in names)
            foreach (var clip in actor.clips)
                if (clip && clip.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return clip;
        return actor.idle ? actor.idle : actor.clips.Length > 0 ? actor.clips[0] : null;
    }

    static Bounds BoundsOf(Actor actor)
    {
        bool found = false; Bounds result = new Bounds(actor.root.transform.position, Vector3.one * 2f);
        foreach (var renderer in actor.root.GetComponentsInChildren<Renderer>())
        { if (!found) { result = renderer.bounds; found = true; } else result.Encapsulate(renderer.bounds); }
        return result;
    }

    static void Sample(Actor actor, AnimationClip clip, float clipTime, float facing)
    {
        if (actor == null || !actor.graph.IsValid()) return;
        if (actor.rig != null) actor.rig.RestorePose();
        actor.model.localRotation = Quaternion.Euler(0f, facing >= 0 ? 90f : -90f, 0f);
        if (!clip) clip = actor.idle;
        if (actor.current != clip)
        {
            if (actor.playable.IsValid()) actor.graph.DestroyPlayable(actor.playable);
            actor.playable = AnimationClipPlayable.Create(actor.graph, clip);
            actor.playable.SetApplyFootIK(false); actor.playable.SetApplyPlayableIK(false);
            actor.playable.SetSpeed(0f); actor.output.SetSourcePlayable(actor.playable); actor.current = clip;
        }
        actor.playable.SetTime(clip.length > .001f ? Mathf.Repeat(Mathf.Max(0f, clipTime), clip.length) : 0f);
        actor.graph.Evaluate(0f);
        if (actor.rig != null) actor.rig.CapturePose();
    }

    void LateUpdate()
    {
        if (!visible || !Ready || !stage || Time.unscaledTime < nextFrame) return;
        nextFrame = Time.unscaledTime + 1f / 24f;
        try { RenderFrame(Time.unscaledTime - shownAt); }
        catch (Exception exception) { Error = "Visual preview paused: " + exception.Message; Ready = false; Hide(); }
    }

    void RenderFrame(float elapsed)
    {
        float duration = page == 0 ? 5.8f : page == 1 ? ComboDuration() + 1.55f : page == 2 ? DirectionalJDuration() : page == 3 ? 4.8f : 7.2f;
        float time = Mathf.Repeat(elapsed, duration);
        if (time < previousTime) { ClearGhosts(); nextGhostAt = 0f; }
        previousTime = time; Progress = time / duration; Keys = DemoKeys.None;
        previewCamera.transform.localPosition = new Vector3(0f, page == 0 || page == 4 ? 1.75f : 1.15f, -12f);
        previewCamera.orthographicSize = page == 0 || page == 4 ? 2.5f : 1.92f;
        ink.Begin(stage.transform.position);
        blue.root.SetActive(true); red.root.SetActive(page != 0);
        blue.root.transform.localPosition = new Vector3(-.8f, 0f, 0f);
        red.root.transform.localPosition = new Vector3(.8f, 0f, 0f);
        Sample(blue, blue.idle, time, 1f); Sample(red, red.idle, time, -1f);
        if (page == 0) MoveJump(time);
        else if (page == 1) Combo(time);
        else if (page == 2) DirectionalJ(time);
        else if (page == 3) Energy(time);
        else DirectionalK(time);
        ink.End(); UpdateGhosts(elapsed);
        previewCamera.Render();
    }

    void MoveJump(float time)
    {
        ActionLabel = "A / D MOVE   •   SPACE + SPACE IN AIR";
        if (time < 1.2f)
        { Keys = DemoKeys.D; blue.root.transform.localPosition = new Vector3(Mathf.Lerp(-2.2f, .7f, time / 1.2f), 0f, 0f); Sample(blue, blue.run, time, 1f); }
        else if (time < 2.4f)
        { Keys = DemoKeys.A; blue.root.transform.localPosition = new Vector3(Mathf.Lerp(.7f, -1.6f, (time - 1.2f) / 1.2f), 0f, 0f); Sample(blue, blue.run, time, -1f); }
        else
        {
            float t = time - 2.65f;
            blue.root.transform.localPosition = new Vector3(-.9f, 0f, 0f);
            if (t >= 0f && t < 2.0f)
            {
                float height = t < .55f ? 1.1f * Mathf.Sin(t / 1.1f * Mathf.PI) :
                    t < 1.12f ? Mathf.Lerp(1.1f, 2.1f, Mathf.Sin((t - .55f) / .57f * Mathf.PI * .5f)) :
                    Mathf.Lerp(2.1f, 0f, Mathf.Pow((t - 1.12f) / .88f, 2f));
                blue.root.transform.localPosition += Vector3.up * Mathf.Max(0f, height);
                Sample(blue, t < 1.12f ? blue.jump : blue.fall, t, 1f);
                if (t < .12f || t >= .55f && t < .67f) Keys = DemoKeys.Space;
                if (t >= .55f && t < .75f)
                    Arc(new Vector2(-.9f, height + .14f), .55f, 185f, 345f, .055f, Mint);
            }
        }
    }

    float ComboDuration()
    {
        float total = 0f;
        for (int i = 0; i < BattleJMoveSet.HitCount; i++) { var d = BattleJMoveSet.Get(BattleJMove.Neutral, i); total += d.Startup + d.Active + d.Recovery; }
        return total;
    }

    void Combo(float time)
    {
        ActionLabel = "NEUTRAL J   •   EACH HIT CONFIRMS THE NEXT";
        float t = time - .45f;
        for (int hit = 0; hit < BattleJMoveSet.HitCount; hit++)
        {
            var data = BattleJMoveSet.Get(BattleJMove.Neutral, hit);
            float duration = data.Startup + data.Active + data.Recovery;
            if (t >= 0f && t < duration)
            {
                if (t < .085f) Keys = DemoKeys.J;
                ActionLabel = hit == 3 ? "HIT 4   •   HEAVY FINISHER" : "CONFIRMED HIT " + (hit + 1) + " / 4";
                Punch(BattleJMove.Neutral, hit, t, data, 1f);
                return;
            }
            t -= duration;
        }
    }

    void DirectionalJ(float time)
    {
        float comboTime = ComboDuration(), comboSlot = comboTime + .55f;
        if (time < comboSlot * 2f)
        {
            int side = time < comboSlot ? 0 : 1;
            float facing = side == 0 ? 1f : -1f;
            float t = time - side * comboSlot - .25f;
            DemoKeys direction = facing > 0f ? DemoKeys.D : DemoKeys.A;
            string directionName = facing > 0f ? "D" : "A";
            ActionLabel = directionName + " + J   •   SAME FOUR-HIT COMBO";
            blue.root.transform.localPosition = new Vector3(-.8f * facing, 0f, 0f);
            red.root.transform.localPosition = new Vector3(.8f * facing, 0f, 0f);
            Sample(blue, blue.idle, time, facing); Sample(red, red.idle, time, -facing);
            if (t >= -.15f && t < comboTime) Keys = direction;
            // A/D chooses facing; it does not replace the neutral four-hit
            // moveset. Keep every real startup/active/recovery and hit order.
            for (int hit = 0; hit < BattleJMoveSet.HitCount; hit++)
            {
                var timing = BattleJMoveSet.Get(BattleJMove.Neutral, hit);
                float duration = timing.Startup + timing.Active + timing.Recovery;
                if (t >= 0f && t < duration)
                {
                    if (t < .085f) Keys |= DemoKeys.J;
                    ActionLabel = directionName + " + J   •   " + (hit == 3 ? "HIT 4 / HEAVY FINISHER" : "CONFIRMED HIT " + (hit + 1) + " / 4");
                    Punch(BattleJMove.Neutral, hit, t, timing, facing);
                    return;
                }
                t -= duration;
            }
            return;
        }

        float verticalTime = time - comboSlot * 2f;
        bool upper = verticalTime < 1.4f;
        float attackTime = (upper ? verticalTime : verticalTime - 1.4f) - .3f;
        BattleJMove move = upper ? BattleJMove.Upper : BattleJMove.Low;
        DemoKeys vertical = upper ? DemoKeys.W : DemoKeys.S;
        ActionLabel = upper ? "W + J   •   UPPERCUT" : "S + J   •   LOW SWEEP";
        var data = BattleJMoveSet.Get(move);
        if (attackTime >= -.15f && attackTime < data.Startup + data.Active + data.Recovery)
        {
            Keys = vertical | (attackTime >= 0f && attackTime < .085f ? DemoKeys.J : DemoKeys.None);
            if (attackTime >= 0f) Punch(move, -1, attackTime, data, 1f);
        }
    }

    float DirectionalJDuration() => (ComboDuration() + .55f) * 2f + 2.8f;

    void Punch(BattleJMove move, int hit, float t, BattleJMoveData data, float facing)
    {
        float duration = data.Startup + data.Active + data.Recovery;
        AnimationClip clip = move == BattleJMove.Low ? blue.idle : hit == 0 ? blue.jab : hit == 2 ? blue.hook : blue.cross;
        Sample(blue, clip, clip.length * Mathf.Clamp01(t / duration), facing);
        float drive = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(data.Startup * .68f, data.Startup + .018f, t));
        float release = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(data.Startup + data.Active, duration, t));
        float weight = Mathf.Clamp01(t / Mathf.Max(.01f, data.Startup) * 3f) * release;
        PosePunch(blue, move, hit, drive, weight, facing);
        float after = t - data.Startup;
        if (after >= 0f && after < .18f)
        {
            Vector2 point = new Vector2(blue.root.transform.localPosition.x + facing * (move == BattleJMove.Low ? 1.1f : 1.25f),
                move == BattleJMove.Low ? .4f : move == BattleJMove.Upper ? 1.55f : 1.2f);
            Burst(point, hit == 3 ? .42f : .23f, 1f - after / .18f);
            Sample(red, red.hit, Mathf.Min(after, red.hit.length * .8f), -facing);
            if (red.rig != null) red.rig.LeanTorso(-facing, -12f, 1f - after / .18f);
            red.root.transform.localPosition += Vector3.right * facing * (hit == 3 ? .45f : .08f) * Mathf.Sin(after / .18f * Mathf.PI);
        }
        if (after > -.035f && after < data.Active + .05f)
        {
            Vector2 from = new Vector2(blue.root.transform.localPosition.x + .3f * facing, move == BattleJMove.Low ? .28f : 1.14f);
            Vector2 to = from + new Vector2(.85f * facing, move == BattleJMove.Upper ? .9f : 0f);
            ink.Line(from, to, move == BattleJMove.Low ? .09f : .045f, Mint, -.7f);
        }
    }

    static void PosePunch(Actor actor, BattleJMove move, int hit, float drive, float weight, float facing)
    {
        if (actor.rig == null) return;
        Vector3 root = actor.Body, knee = root + new Vector3(.32f * facing, -.35f, 0f);
        Vector3 rightPole = root + new Vector3(-.12f * facing, .12f, -.32f * facing);
        Vector3 leftPole = root + new Vector3(-.12f * facing, .12f, .32f * facing);
        actor.rig.PlantFeet(knee, weight, move != BattleJMove.Low);
        if (move == BattleJMove.Low)
        {
            actor.rig.ShiftHips(Vector3.down * .21f * weight); actor.rig.LeanTorso(facing, 8f, weight);
            actor.rig.AimLeg(true, root + Vector3.Lerp(new Vector3(-.12f * facing, -.62f, -.12f * facing),
                new Vector3(.95f * facing, -.58f, -.04f * facing), drive), knee, weight);
        }
        else
        {
            bool right = hit == 1 || hit == 3 || move == BattleJMove.Side;
            Vector3 target = move == BattleJMove.Upper ? new Vector3(.38f * facing, 1.22f, .04f * facing) :
                new Vector3((hit == 3 ? 1.16f : .98f) * facing, .26f, (right ? -.04f : .04f) * facing);
            actor.rig.AimArm(move == BattleJMove.Upper ? false : right,
                root + Vector3.Lerp(new Vector3(.1f * facing, .3f, (right ? -.22f : .22f) * facing), target, drive),
                right ? rightPole : leftPole, weight);
            actor.rig.LeanTorso(facing, hit == 3 ? 20f : move == BattleJMove.Side ? 16f : 6f, drive * weight);
            actor.rig.TwistTorso(Mathf.Lerp(hit == 3 ? -30 : -12, hit == 3 ? 38 : 12, drive) * facing, weight);
        }
    }

    void Energy(float time)
    {
        blue.root.transform.localPosition = new Vector3(-2.2f, 0f, 0f);
        red.root.transform.localPosition = new Vector3(2.05f, 0f, 0f);
        // Capture planted feet AFTER moving to the beam-demonstration marks.
        // Capturing at the default combo marks would pull both feet sideways
        // towards stale world-space targets, making the charging actor float.
        Sample(blue, blue.idle, time, 1f);
        Sample(red, red.idle, time, -1f);
        bool full = time >= 1.7f;
        float t = time - (full ? 1.7f : .35f);
        float startup = .12f, charging = full ? 1f : 0f;
        float releaseAt = startup + charging;
        float charge = full ? Mathf.Clamp01((t - startup) / 1f) : 0f;
        bool firing = t >= releaseAt && t < releaseAt + .22f;
        bool active = t >= 0f && t < releaseAt + .22f + (full ? .45f : .35f);
        ActionLabel = full ? firing ? "FULL CHARGE   •   AUTO FIRE" : "HOLD K   •   BUILD TO FULL CHARGE" : "TAP K   •   QUICK ENERGY";
        if (!active) return;
        if (t < releaseAt || !full && t < .075f) Keys = DemoKeys.K;
        PoseSpecial(blue, BattleSpecialKind.Charge, firing, 1f, charge);
        Vector2 muzzle = new Vector2(blue.root.transform.localPosition.x + (firing ? .85f : .55f), blue.height * .52f + .20f);
        if (t < releaseAt || firing)
        {
            Disc(muzzle, Mathf.Lerp(.11f, .29f, charge), new Color(.33f, .94f, .79f, .8f));
            Disc(muzzle, Mathf.Lerp(.055f, .16f, charge), Cream);
            for (int i = 0; i < 5; i++)
            {
                float angle = time * 4f + i * Mathf.PI * 2f / 5f;
                Vector2 p = muzzle + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (.27f + charge * .25f);
                ink.Diamond(p, Vector2.one * .035f, Mint, -.74f);
            }
        }
        if (firing)
        {
            float length = Mathf.Lerp(4f, 14f, charge), radius = Mathf.Lerp(.12f, .42f, charge);
            ink.Line(muzzle, muzzle + Vector2.right * length, radius * 2f, new Color(.24f, .83f, .76f, .7f), -.70f);
            ink.Line(muzzle, muzzle + Vector2.right * length, radius * .72f, Cream, -.73f);
            Burst(new Vector2(red.root.transform.localPosition.x - .35f, muzzle.y), full ? .4f : .22f, .9f);
            Sample(red, red.hit, t - releaseAt, -1f);
        }
    }

    void DirectionalK(float time)
    {
        int part = Mathf.Min(3, Mathf.FloorToInt(time / 1.8f));
        float t = time - part * 1.8f - .3f;
        if (part < 2)
        {
            float facing = part == 0 ? 1f : -1f;
            float travel = Mathf.Clamp01((t - .15f) / .30f);
            blue.root.transform.localPosition = new Vector3(Mathf.Lerp(-2.1f, 2.1f, travel) * facing, 0f, 0f);
            red.root.transform.localPosition = new Vector3(2.65f * facing, 0f, 0f);
            Sample(blue, blue.idle, time, facing); Sample(red, red.idle, time, -facing);
            ActionLabel = "A / D + K   •   DASH KICK + AFTERIMAGES";
            if (t >= -.1f && t < .85f)
            {
                Keys = (facing > 0 ? DemoKeys.D : DemoKeys.A) | (t >= 0f && t < .08f ? DemoKeys.K : DemoKeys.None);
                PoseSpecial(blue, BattleSpecialKind.Dash, t >= .15f, facing, 0f);
                if (t >= .15f && t < .45f)
                {
                    TrailSnapshot(time);
                    ink.Line(new Vector2(blue.root.transform.localPosition.x - facing, .16f),
                        new Vector2(blue.root.transform.localPosition.x + .25f * facing, .2f), .09f, Mint, -.7f);
                    if (travel > .78f) Burst(new Vector2(2.3f * facing, 1.1f), .32f, 1f - (travel - .78f) / .3f);
                }
            }
        }
        else if (part == 2)
        {
            red.root.SetActive(false);
            float rise = Mathf.Clamp01((t - .1f) / .95f);
            blue.root.transform.localPosition = new Vector3(-.5f, t >= .1f && t < 1.05f ? Mathf.Sin(rise * Mathf.PI) * 1.65f : 0f, 0f);
            ActionLabel = "W + K   •   ONE RISING RECOVERY PER AIRTIME";
            if (t >= 0f && t < 1.1f)
            {
                if (t < .1f) Keys = DemoKeys.W | DemoKeys.K;
                PoseSpecial(blue, BattleSpecialKind.Rise, t >= .1f, 1f, 0f);
                if (t > .1f && t < .4f)
                { TrailSnapshot(time); Arc(new Vector2(-.5f, .1f), .6f, 190f, 350f, .06f, Mint); }
            }
        }
        else
        {
            ActionLabel = "S + K   •   BRIEF FRONT COUNTER / GROUND ONLY";
            blue.root.transform.localPosition = new Vector3(-.8f, 0f, 0f);
            red.root.transform.localPosition = new Vector3(.8f, 0f, 0f);
            if (t >= 0f && t < .65f)
            {
                if (t < .08f) Keys = DemoKeys.S | DemoKeys.K;
                PoseSpecial(blue, BattleSpecialKind.Counter, t >= .20f && t < .32f, 1f, 0f);
                if (t >= .08f && t < .30f) Arc(new Vector2(-.28f, 1.13f), .59f, -70f, 70f, .055f, Mint);
                if (t >= .05f && t < .22f) Sample(red, red.jab, (t - .05f) / .17f * red.jab.length, -1f);
                if (t >= .20f && t < .39f) { Burst(new Vector2(.2f, 1.2f), .27f, 1f - (t - .2f) / .19f); Sample(red, red.hit, t - .2f, -1f); }
            }
        }
    }

    static void PoseSpecial(Actor actor, BattleSpecialKind kind, bool active, float facing, float charge)
    {
        if (actor.rig == null) return;
        Vector3 root = actor.Body, direction = Vector3.right * facing;
        Vector3 knee = root + direction * .35f + Vector3.down * .35f;
        Vector3 rightPole = root + new Vector3(-.1f * facing, .1f, -.32f * facing);
        Vector3 leftPole = root + new Vector3(-.1f * facing, .1f, .32f * facing);
        actor.rig.LeanTorso(facing, kind == BattleSpecialKind.Rise ? -5f : kind == BattleSpecialKind.Dash ? 15f : 7f + charge * 3f, 1f);
        if (kind == BattleSpecialKind.Charge)
        {
            float reach = active ? .85f : .55f;
            actor.rig.ShiftHips(Vector3.down * .045f);
            actor.rig.PlantFeet(knee, 1f);
            actor.rig.AimArm(true, root + new Vector3(reach * facing, .22f, -.04f * facing), rightPole, 1f);
            actor.rig.AimArm(false, root + new Vector3(reach * facing, .22f, .04f * facing), leftPole, 1f);
            actor.rig.AimPalm(true, direction, 1f); actor.rig.AimPalm(false, direction, 1f);
        }
        else if (kind == BattleSpecialKind.Dash)
        {
            actor.rig.ShiftHips(Vector3.down * .07f);
            if (active)
            {
                actor.rig.AimLeg(true, root + new Vector3(.9f * facing, -.12f, -.04f * facing), knee, 1f);
                actor.rig.AimLeg(false, root + new Vector3(-.3f * facing, -.65f, .1f * facing), knee, 1f);
            }
            actor.rig.AimArm(true, root + new Vector3(-.15f * facing, .14f, -.08f), rightPole, .85f);
            actor.rig.AimArm(false, root + new Vector3(.2f * facing, .4f, .08f), leftPole, .85f);
        }
        else if (kind == BattleSpecialKind.Rise)
        {
            actor.rig.AimArm(true, root + new Vector3(.23f * facing, active ? 1.04f : .16f, -.04f), rightPole, 1f);
            actor.rig.AimArm(false, root + new Vector3(.12f * facing, .34f, .08f), leftPole, .8f);
            actor.rig.AimLeg(true, root + new Vector3(.08f * facing, -.75f, -.12f), knee, 1f);
            actor.rig.AimLeg(false, root + new Vector3(-.1f * facing, -.62f, .12f), knee, 1f);
        }
        else
        {
            actor.rig.ShiftHips(Vector3.down * .09f); actor.rig.PlantFeet(knee, 1f);
            actor.rig.AimArm(true, root + new Vector3((active ? 1.10f : .45f) * facing, .24f, -.06f), rightPole, 1f);
            actor.rig.AimArm(false, root + new Vector3(.36f * facing, .26f, .06f), leftPole, 1f);
        }
    }

    void BuildGhost(int slot)
    {
        int count = blue.skins.Length;
        var ghost = new Ghost { meshes = new Mesh[count], renderers = new MeshRenderer[count], nodes = new Transform[count] };
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Visual-only afterimage " + slot + " / " + i) { layer = PreviewLayer };
            go.transform.SetParent(stage.transform, false); ghost.nodes[i] = go.transform;
            ghost.meshes[i] = new Mesh { name = "Quick guide baked ghost", hideFlags = HideFlags.DontSave };
            go.AddComponent<MeshFilter>().sharedMesh = ghost.meshes[i];
            ghost.renderers[i] = go.AddComponent<MeshRenderer>();
            ghost.renderers[i].sharedMaterial = ghostMaterial; ConfigureRenderer(ghost.renderers[i]); ghost.renderers[i].enabled = false;
        }
        ghosts.Add(ghost);
    }

    void TrailSnapshot(float time)
    {
        if (ghosts.Count == 0 || time < nextGhostAt) return;
        nextGhostAt = time + .075f;
        Ghost ghost = ghosts[0];
        foreach (var candidate in ghosts) if (candidate.until < ghost.until) ghost = candidate;
        ghost.until = Time.unscaledTime - shownAt + .32f;
        for (int i = 0; i < blue.skins.Length; i++)
        {
            var source = blue.skins[i];
            source.BakeMesh(ghost.meshes[i]);
            ghost.nodes[i].SetPositionAndRotation(source.transform.position, source.transform.rotation);
            ghost.nodes[i].localScale = source.transform.lossyScale;
        }
    }

    void UpdateGhosts(float elapsed)
    {
        foreach (var ghost in ghosts)
        {
            float remaining = ghost.until - elapsed;
            ghost.properties.SetColor("_Tint", new Color(.40f, .82f, .74f, .43f * Mathf.Clamp01(remaining / .32f)));
            foreach (var renderer in ghost.renderers)
            { renderer.enabled = remaining > 0f; if (remaining > 0f) renderer.SetPropertyBlock(ghost.properties); }
        }
    }

    void ClearGhosts()
    {
        foreach (var ghost in ghosts)
        { ghost.until = 0f; foreach (var renderer in ghost.renderers) if (renderer) renderer.enabled = false; }
    }

    void Arc(Vector2 center, float radius, float start, float end, float width, Color color)
    {
        for (int i = 0; i < 20; i++)
        {
            float a = Mathf.Lerp(start, end, i / 20f) * Mathf.Deg2Rad, b = Mathf.Lerp(start, end, (i + 1) / 20f) * Mathf.Deg2Rad;
            ink.Line(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width, color, -.72f);
        }
    }
    void Disc(Vector2 center, float radius, Color color)
    {
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f, b = (i + 1) * Mathf.PI * 2f / 24f;
            ink.Triangle(center, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, color, -.74f);
        }
    }
    void Burst(Vector2 point, float radius, float fade)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * .25f, b = (i + .5f) * Mathf.PI * .25f;
            Color color = i % 2 == 0 ? Gold : Cream; color.a = Mathf.Clamp01(fade);
            ink.Triangle(point, point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                point + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius * .26f, color, -.78f);
        }
    }
    static void Rect(BattleJPresentation.InkMesh mesh, float x, float y, float width, float height, Color color, float z)
    {
        mesh.Triangle(new Vector2(x, y), new Vector2(x, y + height), new Vector2(x + width, y + height), color, z);
        mesh.Triangle(new Vector2(x, y), new Vector2(x + width, y + height), new Vector2(x + width, y), color, z);
    }
    Material Own(Material material) { materials.Add(material); return material; }

    void Release()
    {
        visible = Ready = false;
        if (blue != null && blue.graph.IsValid()) blue.graph.Destroy();
        if (red != null && red.graph.IsValid()) red.graph.Destroy();
        foreach (var ghost in ghosts) foreach (var mesh in ghost.meshes) if (mesh) Destroy(mesh);
        ghosts.Clear();
        if (floor != null) floor.Dispose(); if (ink != null) ink.Dispose();
        floor = ink = null;
        foreach (var material in materials) if (material) Destroy(material);
        materials.Clear();
        if (previewCamera) previewCamera.targetTexture = null;
        if (Texture) { Texture.Release(); Destroy(Texture); Texture = null; }
        if (stage) { stage.SetActive(false); Destroy(stage); stage = null; }
        blue = red = null; previewCamera = null; sourceId = 0;
    }
    void OnDisable() { Hide(); }
    void OnDestroy() { Release(); }
}
