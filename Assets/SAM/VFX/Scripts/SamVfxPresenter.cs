using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction
{
    /// <summary>
    /// Runtime prototype: independent seeded emitters, world-space released particles,
    /// an irregular tapered punch ribbon and bounded per-presenter pools.
    /// These effects establish integration and timing; they are not final reference-matched art.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SamVfxPresenter : MonoBehaviour
    {
        [Header("Assign for player builds so shader variants remain referenced")]
        public Shader flameShader;
        public Texture2D erosionTexture;
        public Texture2D dustTexture;
        [Header("Original impact flame texture - per-presenter owned material")]
        public Shader impactTextureShader;
        public Texture2D impactFlameTexture;
        public SamImpactTextureSettings impactTexture = new SamImpactTextureSettings();
        [Tooltip("Character renderers only. Auto-discovered on first SetHeat if empty.")]
        public Renderer[] heatRenderers;
        [Range(0.1f, 3f)] public float visualScale = 1f;
        [Range(1, 48)] public int maximumActiveEffects = 20;
        [Range(1, 12)] public int maximumPooledPerType = 6;
        [Tooltip("Requires the URP asset or camera to request a depth texture.")]
        public bool useDepthFade = true;
        public bool enableWarmLights = true;
        [Header("P04 energy release - independently editable art layers")]
        public SamPunchEnergySettings punchEnergy = new SamPunchEnergySettings();
        public SamCombatHeatSettings combatHeat = new SamCombatHeatSettings();
        [Tooltip("Pure art review gate for PunchImpact and PunchTrail, including their particles and lights. Simulation continues.")]
        public bool showPunchEffects = true;
        [Tooltip("Pure art review gate for sustained CombatHeat, including surface tongues, particles and lights. Simulation continues.")]
        public bool showCombatHeat = true;
        [Tooltip("Review visibility only; hidden layers still receive the same simulation and random sequence.")]
        public SamVfxLayers visibleLayers = SamVfxLayers.All;
        [Tooltip("Diagnostic rendering only. Restore Final before art review and production capture.")]
        public SamVfxDiagnosticMode diagnosticMode = SamVfxDiagnosticMode.Final;
        [Tooltip("The skill clock calls Advance. Particle rendering remains active while simulation is paused.")]
        public bool manualTick;
        public int ActiveEffectCount => active.Count;
        public int PooledEffectCount
        {
            get { int count = 0; foreach (var pair in pools) count += pair.Value.Count; return count; }
        }

        enum Kind { Thruster, PunchTrail, PunchImpact, LandDust, Ignition, CombatHeat }
        sealed class Effect
        {
            internal Kind kind;
            internal GameObject root;
            internal ParticleSystem flame, embers, dust;
            internal ParticleSystemRenderer flameRenderer, emberRenderer, dustRenderer;
            internal MeshFilter ribbonFilter;
            internal MeshRenderer ribbonRenderer;
            internal Mesh ribbon;
            internal Light light;
            internal SamPunchEnergyShape energy;
            internal SamCombatHeatShape surfaceHeat;
            internal Transform socket;
            internal Transform transferSocket;
            internal Vector3 position, direction;
            internal float age, emissionEnd, ttl, emissionRate;
            internal double elapsed;
            internal int emissionIndex, ribbonTick;
            internal Vector3 previousSocketPosition;
            internal bool releasing, burstDone;
            internal System.Random random;
            internal int seed;
            internal readonly List<Sample> samples = new List<Sample>(40);
            internal readonly Vector3[] vertices = new Vector3[80];
            internal readonly Vector2[] uv = new Vector2[80];
            internal readonly Color[] colors = new Color[80];
            internal readonly int[] triangles = new int[234];
            internal readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        }
        struct Sample { internal Vector3 position; internal float time; }

        readonly Dictionary<Kind, Stack<Effect>> pools = new Dictionary<Kind, Stack<Effect>>();
        readonly List<Effect> active = new List<Effect>(24);
        readonly List<Effect> owned = new List<Effect>(48);
        readonly HashSet<string> unknownIds = new HashSet<string>();
        Material flameMaterial, emberMaterial, dustMaterial, ribbonMaterial, impactTextureMaterial;
        MaterialPropertyBlock heatProperties;
        bool ready;
        static readonly int HeatId = Shader.PropertyToID("_Heat");
        static readonly int TimeId = Shader.PropertyToID("_LocalTime");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int ReviewId = Shader.PropertyToID("_ReviewMode");
        static readonly int DepthFadeId = Shader.PropertyToID("_UseDepthFade");

        public void Play(string effectId, Transform socket, Vector3 position, Vector3 direction, int seed)
        {
            Kind kind;
            if (string.IsNullOrEmpty(effectId) || !Enum.TryParse(effectId, true, out kind) || !Enum.IsDefined(typeof(Kind),kind))
            {
                if (unknownIds.Add(effectId ?? "<null>")) Debug.LogWarning("SAM VFX: unknown effect ID " + effectId, this);
                return;
            }
            if (!isActiveAndEnabled || !EnsureResources()) return;
            if (kind == Kind.CombatHeat && (combatHeat == null || !combatHeat.enabled)) return;
            if (!Finite(position)) position = transform.position;
            direction = Finite(direction) && direction.sqrMagnitude > 0.00001f ? direction.normalized : transform.forward;
            while (active.Count >= Mathf.Max(1, maximumActiveEffects))
            {
                int reclaim=0;
                for(int i=0;i<active.Count;i++)if(active[i].kind!=Kind.CombatHeat){reclaim=i;break;}
                ReturnToPool(active[reclaim]);
            }
            if (!pools.TryGetValue(kind, out Stack<Effect> pool)) pools.Add(kind, pool = new Stack<Effect>());
            Effect fx = pool.Count > 0 ? pool.Pop() : CreateEffect(kind);
            // Impact energy is released into world space, even when the caller passes the fist socket.
            fx.socket = kind == Kind.PunchImpact || kind == Kind.LandDust ? null : socket;
            // Geometry-only source: never parents or translates the released effect.
            fx.transferSocket = kind == Kind.PunchImpact ? socket : null;
            fx.position = position;
            fx.direction = direction;
            fx.seed = seed;
            fx.random = new System.Random(seed);
            fx.age = 0;
            fx.elapsed = 0;
            fx.emissionIndex = 0;
            fx.ribbonTick = 1;
            fx.previousSocketPosition = position;
            fx.releasing = fx.burstDone = false;
            fx.samples.Clear();
            fx.root.transform.SetPositionAndRotation(position, SafeRotation(direction));
            fx.root.transform.localScale = Vector3.one;
            fx.root.SetActive(true);
            ConfigureTiming(fx);
            ResetParticles(fx.flame, seed);
            ResetParticles(fx.embers, seed ^ 0x273);
            ResetParticles(fx.dust, seed ^ 0x1567);
            fx.ribbon.Clear();
            fx.ribbonRenderer.enabled = kind == Kind.PunchTrail;
            fx.light.enabled = false;
            if (fx.energy != null) { fx.energy.Reset(seed, visualScale, fx.transferSocket != null && Finite(fx.transferSocket.position) ? fx.transferSocket.position : position); fx.energy.SetTime(0, EffectiveLayers(fx), false, (int)diagnosticMode, useDepthFade); }
            if (fx.surfaceHeat != null) { fx.surfaceHeat.Reset(seed,visualScale); UpdateSurfaceHeat(fx); }
            ApplyProperties(fx, 0);
            active.Add(fx);
            EmitOpening(fx);
            if (kind == Kind.PunchTrail) AddRibbonSample(fx);
            ApplyLayerVisibility(fx);
            UpdateLight(fx);
        }

        /// <summary>Call immediately after deterministic pose sampling. No age, emission or random state changes.</summary>
        public void SampleReleasedTransfers()
        {
            foreach (Effect fx in active)
            {
                if (fx.energy == null) continue;
                if (fx.releasing) fx.energy.FreezeTransfer();
                else if (fx.transferSocket != null) fx.energy.SampleTransfer(fx.transferSocket.position);
            }
        }

        /// <summary>Read-only geometric endpoints for an active release seed on this presenter.</summary>
        public bool TryGetPunchTransferEndpoints(int seed, out Vector3 nearWorld, out Vector3 farWorld, out bool frozen)
        {
            for (int i = active.Count - 1; i >= 0; --i)
                if (active[i].kind == Kind.PunchImpact && active[i].seed == seed && active[i].energy != null)
                    return active[i].energy.TryGetTransferEndpoints(out nearWorld, out farWorld, out frozen);
            nearWorld = farWorld = Vector3.zero; frozen = false;
            return false;
        }

        public void StopAll(bool immediate)
        {
            for (int i = active.Count - 1; i >= 0; --i)
                StopEffect(active[i],immediate);
        }

        /// <summary>Stop punch/thruster/ground bursts while sustained surface combat fire continues.</summary>
        public void StopTransient(bool immediate)
        {
            for(int i=active.Count-1;i>=0;--i)
                if(active[i].kind!=Kind.CombatHeat)StopEffect(active[i],immediate);
        }

        /// <summary>Surface sockets should sit outside armor with local +Z facing out.</summary>
        public void BeginCombatHeat(Transform chestSurface,Transform leftShoulderSurface,Transform rightShoulderSurface,int seed)
        {
            EndCombatHeat(true);
            BeginCombatHeatAt(chestSurface,seed,true);
            BeginCombatHeatAt(leftShoulderSurface,seed^0x1439,false);
            BeginCombatHeatAt(rightShoulderSurface,seed^0x6721,false);
        }

        void BeginCombatHeatAt(Transform socket,int seed,bool chest)
        {
            if(socket==null)return;
            Play("CombatHeat",socket,socket.position,socket.forward,seed);
            for(int i=active.Count-1;i>=0;--i)
            {
                Effect fx=active[i];
                if(fx.kind!=Kind.CombatHeat||fx.socket!=socket||fx.seed!=seed)continue;
                fx.surfaceHeat?.SetProfile(chest);
                UpdateSurfaceHeat(fx);
                break;
            }
        }

        public void EndCombatHeat(bool immediate)
        {
            for(int i=active.Count-1;i>=0;--i)
                if(active[i].kind==Kind.CombatHeat)StopEffect(active[i],immediate);
        }

        void StopEffect(Effect fx,bool immediate)
        {
            if(immediate){ReturnToPool(fx);return;}
            if(fx.releasing)return;
            // Stop generation while released particles and the last ribbon samples expire.
            fx.releasing=true;
            fx.energy?.FreezeTransfer();
            fx.transferSocket=null;
            fx.emissionEnd=Mathf.Min(fx.emissionEnd,fx.age);
            if(fx.kind==Kind.CombatHeat)fx.ttl=fx.age+Mathf.Max(1.1f,combatHeat.particleLifetime*2.1f);
            fx.socket=null;
            fx.light.enabled=false;
            if(fx.energy!=null)fx.energy.SetTime(fx.age,EffectiveLayers(fx),true,(int)diagnosticMode,useDepthFade);
            UpdateSurfaceHeat(fx);
        }

        public void SetHeat(float heat)
        {
            heat = float.IsNaN(heat) || float.IsInfinity(heat) ? 0 : Mathf.Clamp01(heat);
            if (heatProperties == null) heatProperties = new MaterialPropertyBlock();
            if (heatRenderers == null || heatRenderers.Length == 0) heatRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer target in heatRenderers)
            {
                if (target == null) continue;
                // Preserve other systems' property overrides; never instantiate all shared materials.
                target.GetPropertyBlock(heatProperties);
                heatProperties.SetFloat(HeatId, heat);
                target.SetPropertyBlock(heatProperties);
            }
        }

        void Update()
        {
            if (!manualTick) Advance(Time.deltaTime);
        }

        /// <summary>
        /// Advance all effect state and paused particle systems by skill-clock seconds.
        /// Emission uses absolute instance event times; no Unity frame timer is read here.
        /// A caller must not also advance this component automatically: set manualTick=true.
        /// </summary>
        public void Advance(float deltaSeconds)
        {
            if (!isActiveAndEnabled || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0) return;
            for (int i = active.Count - 1; i >= 0; --i)
            {
                Effect fx = active[i];
                if (fx.root == null) { active.RemoveAt(i); owned.Remove(fx); continue; }
                double start = fx.elapsed;
                double requestedEnd = start + deltaSeconds;
                double end = Math.Min(requestedEnd, fx.ttl);
                Vector3 startPosition = fx.previousSocketPosition;
                Vector3 endPosition = fx.socket != null ? fx.socket.position : startPosition;
                if (!Finite(endPosition)) endPosition = startPosition;
                double rate = fx.emissionRate;
                bool continuous = fx.kind != Kind.LandDust;
                // Split at emitter and ribbon events, so a capture delta cannot skip a short burst.
                while (fx.elapsed + 1e-9 < end)
                {
                    double emissionTime = !fx.releasing && continuous ? (fx.emissionIndex + 1) / rate : double.PositiveInfinity;
                    if (emissionTime >= fx.emissionEnd - 1e-8) emissionTime = double.PositiveInfinity;
                    double ribbonTime = !fx.releasing && fx.kind == Kind.PunchTrail ? fx.ribbonTick / 120.0 : double.PositiveInfinity;
                    if (ribbonTime >= fx.emissionEnd - 1e-8) ribbonTime = double.PositiveInfinity;
                    double next = Math.Min(end, Math.Min(emissionTime, ribbonTime));
                    if (fx.elapsed < fx.emissionEnd && fx.emissionEnd < next) next = fx.emissionEnd;
                    float step = (float)Math.Max(0, next - fx.elapsed);
                    SimulateParticles(fx.flame, step);
                    SimulateParticles(fx.embers, step);
                    SimulateParticles(fx.dust, step);
                    fx.elapsed = next;
                    fx.age = (float)next;
                    if (!fx.releasing && next <= fx.emissionEnd && fx.socket != null)
                    {
                        float fraction = (float)((next-start)/Math.Max(requestedEnd-start,1e-9));
                        fx.position = Vector3.LerpUnclamped(startPosition,endPosition,fraction);
                        fx.root.transform.position = fx.position;
                        if(fx.kind==Kind.CombatHeat)
                        {
                            fx.direction=fx.socket.forward;
                            fx.root.transform.rotation=SafeRotation(fx.direction);
                        }
                    }
                    if (emissionTime <= next + 1e-9)
                    {
                        fx.emissionIndex++;
                        EmitContinuousParticle(fx);
                    }
                    if (ribbonTime <= next + 1e-9)
                    {
                        fx.ribbonTick++;
                        AddRibbonSample(fx);
                    }
                }
                fx.previousSocketPosition = endPosition;
                bool emitting = !fx.releasing && fx.age < fx.emissionEnd;
                UpdateRibbon(fx);
                UpdateLight(fx);
                ApplyProperties(fx, fx.age);
                if (fx.energy != null) fx.energy.SetTime(fx.age, EffectiveLayers(fx), fx.releasing, (int)diagnosticMode, useDepthFade);
                UpdateSurfaceHeat(fx);
                ApplyLayerVisibility(fx);
                bool energyAlive = (fx.energy != null && fx.energy.IsAlive(fx.age)) || (fx.surfaceHeat != null && fx.surfaceHeat.IsAlive(fx.age));
                if (fx.age >= fx.ttl || (!emitting && !energyAlive && fx.samples.Count < 2 && !fx.flame.IsAlive(false) && !fx.embers.IsAlive(false) && !fx.dust.IsAlive(false)))
                    ReturnToPool(fx);
            }
        }

        static void SimulateParticles(ParticleSystem system, float seconds)
        {
            if (seconds > 0) system.Simulate(seconds, false, false, false);
            // Simulate finishes paused; Pause explicitly also protects against an external Play call.
            system.Pause(false);
        }

        bool EnsureResources()
        {
            if (ready) return true;
            if (flameShader == null) flameShader = Shader.Find("SamProduction/Flame");
            if (flameShader == null)
            {
                Debug.LogError("SAM VFX requires the SamProduction/Flame shader. Assign it on the presenter for player builds.", this);
                return false;
            }
            flameMaterial = MakeMaterial("SAM runtime flame", new Color(1f, .07f, .003f), new Color(1f, .58f, .15f), 3.2f, 0);
            emberMaterial = MakeMaterial("SAM runtime embers", new Color(1f, .22f, .013f), new Color(1f, .83f, .38f), 4.2f, 0);
            dustMaterial = MakeMaterial("SAM runtime dust", new Color(.045f, .035f, .044f), new Color(.19f, .12f, .085f), 1f, 0);
            if (dustTexture != null) dustMaterial.SetTexture("_MainTex", dustTexture);
            ribbonMaterial = MakeMaterial("SAM runtime punch ribbon", new Color(1f, .045f, .002f), new Color(1f, .69f, .22f), 3.5f, 1);
            if (impactTexture == null) impactTexture = new SamImpactTextureSettings();
            if (impactTextureShader == null) impactTextureShader = Resources.Load<Shader>("SAMVFX/SamImpactTexture");
            if (impactTextureShader == null) impactTextureShader = Shader.Find("SamProduction/OriginalImpactTexture");
            if (impactFlameTexture == null) impactFlameTexture = Resources.Load<Texture2D>("SAMVFX/FlameBurstOriginal14");
            if (impactTextureShader != null && impactFlameTexture != null)
            {
                impactTextureMaterial = new Material(impactTextureShader) { name = "SAM runtime original impact flame" };
                impactTextureMaterial.SetTexture("_MainTex", impactFlameTexture);
            }
            ready = true;
            return true;
        }

        Material MakeMaterial(string label, Color outer, Color core, float power, float shape)
        {
            Material material = new Material(flameShader) { name = label };
            material.SetColor("_BaseColor", outer);
            material.SetColor("_HotColor", core);
            material.SetFloat("_EmissionPower", power);
            material.SetFloat("_ShapeMode", shape);
            material.SetFloat("_UseDepthFade", useDepthFade ? 1 : 0);
            if (erosionTexture != null) material.SetTexture("_MainTex", erosionTexture);
            return material;
        }

        Effect CreateEffect(Kind kind)
        {
            Effect fx = new Effect { kind = kind, root = new GameObject("SAM VFX / " + name + " / " + kind) };
            fx.flame = MakeParticles(fx.root.transform, "Flame fragments", flameMaterial, 192, out fx.flameRenderer);
            fx.embers = MakeParticles(fx.root.transform, "Detached embers", emberMaterial, 144, out fx.emberRenderer);
            fx.dust = MakeParticles(fx.root.transform, "Impact dust", dustMaterial, 64, out fx.dustRenderer);
            if(kind==Kind.CombatHeat)
            {
                // Emission is still manual and systems stay paused; looping only permits a long
                // persistent skill state to simulate past the standard burst duration.
                var flameMain=fx.flame.main;flameMain.loop=true;
                var emberMain=fx.embers.main;emberMain.loop=true;
                fx.surfaceHeat=new SamCombatHeatShape(fx.root.transform,flameMaterial,combatHeat);
            }
            GameObject ribbonObject = new GameObject("Swept punch surface");
            ribbonObject.transform.SetParent(fx.root.transform, false);
            fx.ribbonFilter = ribbonObject.AddComponent<MeshFilter>();
            fx.ribbonRenderer = ribbonObject.AddComponent<MeshRenderer>();
            fx.ribbonRenderer.sharedMaterial = ribbonMaterial;
            fx.ribbonRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fx.ribbonRenderer.receiveShadows = false;
            fx.ribbon = new Mesh { name = "SAM dynamic tapered ribbon" };
            fx.ribbon.MarkDynamic();
            fx.ribbonFilter.sharedMesh = fx.ribbon;
            if (kind == Kind.PunchImpact)
            {
                if (punchEnergy == null) punchEnergy = new SamPunchEnergySettings();
                fx.energy = new SamPunchEnergyShape(fx.root.transform, flameMaterial, punchEnergy, impactTextureMaterial, impactTexture);
                // Detached impact fragments follow their velocity instead of forming upright
                // billboard flames at the fist. Other emitter types keep their own orientation.
                fx.flameRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                fx.flameRenderer.lengthScale = 1.4f;
                fx.flameRenderer.velocityScale = .045f;
            }
            for (int j = 0; j < 39; ++j)
            {
                int q = j * 6, v = j * 2;
                fx.triangles[q] = v; fx.triangles[q+1] = v+1; fx.triangles[q+2] = v+2;
                fx.triangles[q+3] = v+1; fx.triangles[q+4] = v+3; fx.triangles[q+5] = v+2;
            }
            fx.light = fx.root.AddComponent<Light>();
            fx.light.type = LightType.Point;
            fx.light.color = new Color(1f, .26f, .035f);
            fx.light.shadows = LightShadows.None;
            fx.light.renderMode = LightRenderMode.ForcePixel;
            fx.light.enabled = false;
            owned.Add(fx);
            return fx;
        }

        ParticleSystem MakeParticles(Transform parent, string label, Material material, int count, out ParticleSystemRenderer renderer)
        {
            GameObject child = new GameObject(label);
            child.transform.SetParent(parent, false);
            ParticleSystem ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = count;
            main.startLifetime = .35f;
            main.startSpeed = 0;
            main.startSize3D = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var color = ps.colorOverLifetime; color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f,.58f,.24f),1) },
                new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.1f), new GradientAlphaKey(.65f,.5f), new GradientAlphaKey(0,1) });
            color.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0,.55f),new Keyframe(.25f,1f),new Keyframe(1,.15f)));
            renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.minParticleSize = 0;
            renderer.maxParticleSize = 1;
            return ps;
        }

        static void ResetParticles(ParticleSystem ps, int seed)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = unchecked((uint)seed) | 1u;
            ps.Play(false);
            ps.Pause(false);
        }

        void ConfigureTiming(Effect fx)
        {
            // Capture the schedule per play. Live inspector edits must not move already-issued
            // absolute birth times backward; a new Begin/Play adopts the edited emission rate.
            fx.emissionRate=fx.kind==Kind.CombatHeat?Mathf.Clamp(combatHeat.particlesPerSecond,4,40):fx.kind==Kind.PunchTrail?90:65;
            switch (fx.kind)
            {
                case Kind.Thruster: fx.emissionEnd=.42f; fx.ttl=1.3f; break;
                case Kind.PunchTrail: fx.emissionEnd=.32f; fx.ttl=1f; break;
                case Kind.PunchImpact:
                    fx.emissionEnd=Mathf.Max(.02f,punchEnergy.fragmentEmission);
                    fx.ttl=Mathf.Max(1.1f,fx.emissionEnd+.65f,Mathf.Max(punchEnergy.coreLifetime,punchEnergy.petalLifetime,punchEnergy.streakLifetime,impactTexture != null && impactTexture.enabled ? impactTexture.lifetime : 0)+.1f);
                    break;
                case Kind.LandDust: fx.emissionEnd=.08f; fx.ttl=1.9f; break;
                case Kind.CombatHeat: fx.emissionEnd=float.PositiveInfinity; fx.ttl=float.PositiveInfinity; break;
                default: fx.emissionEnd=.58f; fx.ttl=1.65f; break;
            }
        }

        void EmitOpening(Effect fx)
        {
            if (fx.kind == Kind.PunchImpact)
            {
                for (int i=0;i<9;i++)
                {
                    EmitEnergyFragment(fx, true);
                }
                for (int i=0;i<16;i++) Spawn(fx,fx.embers,fx.position,
                    fx.direction*Range(fx,3.5f,8.5f)+Perpendicular(fx)*Range(fx,.5f,2.3f),
                    Range(fx,.18f,.55f),.015f,Range(fx,.035f,.085f),Range(fx,.6f,.95f));
                // A fist-height energy release is not a confirmed ground contact. LandDust owns ground dust.
            }
            else if (fx.kind == Kind.LandDust) EmitDust(fx,23,1.4f);
            else if (fx.kind == Kind.Ignition)
            {
                for(int i=0;i<12;i++) Spawn(fx,fx.flame,fx.position+Sphere(fx)*.12f,Vector3.up*Range(fx,.4f,1.3f)+Sphere(fx)*.45f,Range(fx,.2f,.48f),Range(fx,.09f,.22f),Range(fx,.3f,.65f),1);
            }
            else if(fx.kind==Kind.CombatHeat)
            {
                // Reference combat fire already exists at clip start. Reconstruct a short,
                // seeded steady-emitter history for this instance only; do not advance other effects.
                float history=Mathf.Clamp(combatHeat.prewarmSeconds,0,1);
                int births=Mathf.CeilToInt(history*Mathf.Clamp(combatHeat.particlesPerSecond,4,40));
                if(births>0)for(int i=0;i<births;i++)
                {
                    EmitCombatHeatParticle(fx);
                    SimulateParticles(fx.flame,history/births);
                    SimulateParticles(fx.embers,history/births);
                }
            }
            fx.burstDone = true;
        }

        void EmitContinuousParticle(Effect fx)
        {
            if(fx.kind == Kind.LandDust) return;
            if(fx.kind == Kind.PunchImpact) { EmitEnergyFragment(fx, false); return; }
            if(fx.kind == Kind.CombatHeat) { EmitCombatHeatParticle(fx); return; }
            {
                Vector3 spread = Sphere(fx);
                Vector3 velocity;
                float width, height, life;
                if(fx.kind == Kind.Thruster)
                {
                    velocity = -fx.direction*Range(fx,2.1f,4.8f)+spread*.75f;
                    width=Range(fx,.055f,.16f); height=Range(fx,.2f,.46f); life=Range(fx,.13f,.3f);
                }
                else if(fx.kind == Kind.PunchTrail)
                {
                    velocity=-fx.direction*Range(fx,.6f,2.4f)+spread*.6f;
                    width=Range(fx,.055f,.16f); height=Range(fx,.12f,.32f); life=Range(fx,.12f,.28f);
                }
                else
                {
                    velocity=Vector3.up*Range(fx,.55f,1.8f)+spread*.42f;
                    width=Range(fx,.08f,.22f); height=Range(fx,.25f,.58f); life=Range(fx,.22f,.48f);
                }
                Spawn(fx,fx.flame,fx.position+spread*.045f*visualScale,velocity,life,width,height,Range(fx,.65f,1));
                if(fx.emissionIndex%3==1) Spawn(fx,fx.embers,fx.position,velocity*1.25f+spread*.3f,life*1.6f,.022f,.045f,.85f);
            }
        }

        void EmitCombatHeatParticle(Effect fx)
        {
            Vector3 jitter=Sphere(fx);
            Vector3 position=fx.position+fx.direction*combatHeat.surfaceOffset*visualScale+jitter*.024f*visualScale;
            Vector3 velocity=Vector3.up*combatHeat.riseSpeed*Range(fx,.72f,1.24f)+fx.direction*.12f+jitter*.13f;
            float life=combatHeat.particleLifetime*Range(fx,.72f,1.18f);
            Spawn(fx,fx.flame,position,velocity,life,combatHeat.flameWidth*Range(fx,.68f,1.25f),
                combatHeat.flameHeight*Range(fx,.65f,1.28f),combatHeat.opacity*Range(fx,.68f,1));
            if(fx.emissionIndex%12==1)
                Spawn(fx,fx.embers,position,velocity*.85f,life*1.65f,.011f,.023f,.42f);
        }

        void EmitEnergyFragment(Effect fx, bool opening)
        {
            Vector3 radial=Perpendicular(fx);
            float strength=Mathf.Clamp01(punchEnergy.detachedFragments);
            float growth=Mathf.Clamp01(fx.age/Mathf.Max(.02f,punchEnergy.fragmentEmission));
            Vector3 position=fx.position+(fx.direction*(punchEnergy.impactDistance+Range(fx,-.16f,.24f))+punchEnergy.impactWorldOffset+radial*Range(fx,.04f,.28f))*visualScale;
            Vector3 velocity=fx.direction*Range(fx,2.2f,5.1f)+radial*Range(fx,.3f,1.3f)+Vector3.up*.2f;
            Spawn(fx,fx.flame,position,velocity,Range(fx,.12f,.29f),
                Range(fx,.04f,.095f)*(1-growth*.30f),Range(fx,.18f,.36f),strength*Range(fx,.42f,.78f));
        }

        static Vector3 Perpendicular(Effect fx)
        {
            Vector3 axis=Vector3.Cross(fx.direction,Mathf.Abs(fx.direction.y)<.9f?Vector3.up:Vector3.right).normalized;
            float angle=Range(fx,0,Mathf.PI*2);
            return axis*Mathf.Cos(angle)+Vector3.Cross(fx.direction,axis)*Mathf.Sin(angle);
        }

        void EmitDust(Effect fx, int count, float power)
        {
            Vector3 normal=fx.kind==Kind.LandDust ? fx.direction : Vector3.up;
            Vector3 axis=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right).normalized;
            Vector3 second=Vector3.Cross(normal,axis);
            for(int i=0;i<count;i++)
            {
                float angle=Range(fx,0,Mathf.PI*2);
                Vector3 radial=axis*Mathf.Cos(angle)+second*Mathf.Sin(angle);
                Spawn(fx,fx.dust,fx.position+normal*.035f+radial*Range(fx,.02f,.12f),
                    radial*Range(fx,.45f,1.65f)*power+normal*Range(fx,.15f,.7f),Range(fx,.45f,1.15f),Range(fx,.22f,.56f),Range(fx,.18f,.42f),Range(fx,.2f,.45f));
            }
        }

        void Spawn(Effect fx, ParticleSystem ps, Vector3 position, Vector3 velocity, float life, float width, float height, float opacity)
        {
            ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams
            {
                position=position, velocity=velocity*visualScale,
                startLifetime=life, startSize3D=new Vector3(width,height,width)*visualScale,
                startColor=new Color(1,1,1,opacity), rotation=Range(fx,-40,40),
                randomSeed=unchecked((uint)fx.random.Next())|1u, applyShapeToPosition=false
            };
            ps.Emit(particle,1);
            ps.Pause(false);
        }

        void AddRibbonSample(Effect fx)
        {
            if(fx.kind!=Kind.PunchTrail) return;
            Vector3 p=fx.socket!=null?fx.position:fx.position+fx.direction*fx.age*1.2f;
            if(fx.samples.Count==0) fx.samples.Add(new Sample{position=p-fx.direction*.10f*visualScale,time=fx.age-.025f});
            if(Vector3.Distance(fx.samples[fx.samples.Count-1].position,p)>.014f || fx.age-fx.samples[fx.samples.Count-1].time>.03f)
                fx.samples.Add(new Sample{position=p,time=fx.age});
            while(fx.samples.Count>40)fx.samples.RemoveAt(0);
        }

        void UpdateRibbon(Effect fx)
        {
            if(fx.kind!=Kind.PunchTrail) return;
            while(fx.samples.Count>0 && (fx.age-fx.samples[0].time>.24f || fx.samples.Count>40)) fx.samples.RemoveAt(0);
            int count=fx.samples.Count;
            if(count<2){fx.ribbonRenderer.enabled=false;return;}
            fx.ribbonRenderer.enabled=true;
            Camera camera=Camera.main;
            Vector3 view=camera!=null?camera.transform.forward:Vector3.forward;
            for(int i=0;i<count;i++)
            {
                Sample sample=fx.samples[i];
                Vector3 tangent=i+1<count?fx.samples[i+1].position-sample.position:sample.position-fx.samples[i-1].position;
                if(tangent.sqrMagnitude<.000001f)tangent=fx.direction;
                Vector3 side=Vector3.Cross(view,tangent).normalized;
                if(side.sqrMagnitude<.1f)side=Vector3.up;
                float age=Mathf.Clamp01((fx.age-sample.time)/.24f);
                float taper=Mathf.Sin(Mathf.PI*Mathf.Clamp01((i+.5f)/count));
                float irregular=.74f+.16f*Mathf.Sin(i*2.39f+fx.seed*.01f)+.1f*Mathf.Sin(i*.81f);
                float width=(.065f+.075f*taper)*(1-age)*irregular*visualScale;
                fx.vertices[i*2]=fx.root.transform.InverseTransformPoint(sample.position-side*width);
                fx.vertices[i*2+1]=fx.root.transform.InverseTransformPoint(sample.position+side*width*RangeStable(i,fx.seed));
                fx.uv[i*2]=new Vector2((float)i/(count-1),0);fx.uv[i*2+1]=new Vector2((float)i/(count-1),1);
                Color color=new Color(1,.85f+.15f*(1-age),.7f+.3f*(1-age),(1-age)*.85f);
                fx.colors[i*2]=fx.colors[i*2+1]=color;
            }
            fx.ribbon.Clear(false);
            fx.ribbon.SetVertices(fx.vertices,0,count*2);
            fx.ribbon.SetUVs(0,fx.uv,0,count*2);
            fx.ribbon.SetColors(fx.colors,0,count*2);
            fx.ribbon.SetTriangles(fx.triangles,0,(count-1)*6,0);
            fx.ribbon.RecalculateBounds();
        }

        void UpdateLight(Effect fx)
        {
            if(!ArtVisible(fx) || !enableWarmLights || (visibleLayers&SamVfxLayers.WarmLight)==0 || fx.kind==Kind.LandDust || fx.releasing){fx.light.enabled=false;return;}
            if(fx.kind==Kind.CombatHeat)
            {
                fx.light.enabled=combatHeat.warmLightIntensity>.001f;
                fx.light.range=combatHeat.warmLightRange*visualScale;
                fx.light.intensity=combatHeat.warmLightIntensity*(.80f+.12f*Mathf.Sin(fx.age*8.7f+(fx.seed&255)*.31f));
                return;
            }
            float envelope=fx.kind==Kind.PunchImpact?SamImpactCoverageProfile.LightEnvelope(fx.age):Mathf.Clamp01(1-fx.age/(fx.emissionEnd+.1f));
            fx.light.enabled=envelope>.005f;
            fx.light.range=(fx.kind==Kind.PunchImpact?SamImpactCoverageProfile.LightRange:1.7f)*visualScale;
            fx.light.intensity=envelope*(fx.kind==Kind.PunchImpact?SamImpactCoverageProfile.LightPower:2.2f);
        }

        void ApplyLayerVisibility(Effect fx)
        {
            SamVfxLayers layers=EffectiveLayers(fx);
            fx.flameRenderer.enabled=(layers&SamVfxLayers.FlameFragments)!=0;
            fx.emberRenderer.enabled=(layers&SamVfxLayers.Embers)!=0;
            fx.dustRenderer.enabled=(layers&SamVfxLayers.Dust)!=0;
            fx.ribbonRenderer.enabled=fx.kind==Kind.PunchTrail&&fx.samples.Count>=2&&(layers&SamVfxLayers.PressureStreaks)!=0;
        }

        bool ArtVisible(Effect fx)
        {
            if(fx.kind==Kind.CombatHeat)return showCombatHeat;
            return (fx.kind!=Kind.PunchImpact&&fx.kind!=Kind.PunchTrail)||showPunchEffects;
        }
        SamVfxLayers EffectiveLayers(Effect fx){return ArtVisible(fx)?visibleLayers:SamVfxLayers.None;}
        void UpdateSurfaceHeat(Effect fx)
        {
            if(fx.surfaceHeat!=null)fx.surfaceHeat.SetTime(fx.age,fx.releasing,(EffectiveLayers(fx)&SamVfxLayers.FlamePetals)!=0,(int)diagnosticMode,useDepthFade);
        }

        /// <summary>Independent visibility-only comparison; neither switch starts/stops an emitter or consumes random state.</summary>
        public void SetArtVisibility(bool punch,bool persistentHeat)
        {
            showPunchEffects=punch;showCombatHeat=persistentHeat;RefreshDiagnostics();
        }

        /// <summary>Visibility-only inspection of owned world-space effects; keeps age, seed, particles and light unchanged.</summary>
        public int IsolatePunchRendererForReview(string exactName)
        {
            RefreshDiagnostics();
            int visible=0;
            foreach(Effect fx in active)
            {
                foreach(Renderer renderer in fx.root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled=renderer.enabled&&fx.kind==Kind.PunchImpact&&renderer.gameObject.name==exactName;
                    if(renderer.enabled&&renderer.gameObject.activeInHierarchy)visible++;
                }
            }
            return visible;
        }

        void ApplyProperties(Effect fx, float time)
        {
            fx.properties.SetFloat(TimeId,time);
            fx.properties.SetFloat(SeedId,(fx.seed&65535)*.0137f);
            fx.properties.SetFloat(OpacityId,1);
            fx.properties.SetFloat(DissolveId,0);
            fx.properties.SetFloat(ReviewId,(int)diagnosticMode);
            fx.properties.SetFloat(DepthFadeId,useDepthFade?1:0);
            fx.flameRenderer.SetPropertyBlock(fx.properties);
            fx.emberRenderer.SetPropertyBlock(fx.properties);
            fx.dustRenderer.SetPropertyBlock(fx.properties);
            fx.ribbonRenderer.SetPropertyBlock(fx.properties);
        }

        /// <summary>Refresh review properties at the current age without advancing simulation.</summary>
        public void RefreshDiagnostics()
        {
            foreach(Effect fx in active)
            {
                ApplyProperties(fx,fx.age);
                if(fx.energy!=null)fx.energy.SetTime(fx.age,EffectiveLayers(fx),fx.releasing,(int)diagnosticMode,useDepthFade);
                UpdateSurfaceHeat(fx);
                UpdateRibbon(fx);
                UpdateLight(fx);
                ApplyLayerVisibility(fx);
            }
        }

        /// <summary>Read renderer/mesh/age state for an actual post-sample GPU diagnostic capture.</summary>
        public SamVfxDiagnosticReport CaptureDiagnostics(Camera camera)
        {
            var report=new SamVfxDiagnosticReport
            {
                presenter=name,manualTick=manualTick,diagnosticMode=diagnosticMode.ToString(),
                visibleLayers=(int)visibleLayers,useDepthFade=useDepthFade,
                showPunchEffects=showPunchEffects,showCombatHeat=showCombatHeat,
                cameraPosition=camera!=null?camera.transform.position:Vector3.zero,
                cameraForward=camera!=null?camera.transform.forward:Vector3.zero
            };
            foreach(Effect fx in active)
            {
                var entry=new SamVfxEffectDiagnostic
                {
                    kind=fx.kind.ToString(),seed=fx.seed,age=fx.age,
                    emissionEnd=float.IsInfinity(fx.emissionEnd)?-1:fx.emissionEnd,
                    ttl=float.IsInfinity(fx.ttl)?-1:fx.ttl,
                    emissionRate=fx.emissionRate,
                    persistent=fx.kind==Kind.CombatHeat,
                    releasing=fx.releasing,position=fx.root.transform.position,direction=fx.root.transform.forward,
                    rootActive=fx.root.activeInHierarchy
                };
                foreach(Renderer renderer in fx.root.GetComponentsInChildren<Renderer>(true))
                    entry.renderers.Add(SamVfxDiagnosticReport.ReadRenderer(renderer,camera));
                report.effects.Add(entry);
            }
            return report;
        }

        void ReturnToPool(Effect fx)
        {
            active.Remove(fx);
            fx.transferSocket=null;
            fx.energy?.ClearTransfer();
            if (fx.root == null) { owned.Remove(fx); return; }
            fx.socket=null;
            fx.samples.Clear();
            fx.light.enabled=false;
            fx.flame.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            fx.embers.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            fx.dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            fx.ribbon.Clear();
            if(fx.energy!=null)fx.energy.Hide();
            if(fx.surfaceHeat!=null)fx.surfaceHeat.Hide();
            fx.root.SetActive(false);
            if(!pools.TryGetValue(fx.kind,out Stack<Effect> pool))pools.Add(fx.kind,pool=new Stack<Effect>());
            if(pool.Count<Mathf.Max(1,maximumPooledPerType))pool.Push(fx);
            else
            {
                owned.Remove(fx);
                if(fx.energy!=null)fx.energy.Dispose();
                if(fx.surfaceHeat!=null)fx.surfaceHeat.Dispose();
                Destroy(fx.ribbon);
                Destroy(fx.root);
            }
        }

        void OnDisable(){StopAll(true);SetHeat(0);}
        void OnDestroy()
        {
            foreach(Effect fx in owned){if(fx.energy!=null)fx.energy.Dispose();if(fx.surfaceHeat!=null)fx.surfaceHeat.Dispose();if(fx.ribbon!=null)Destroy(fx.ribbon);if(fx.root!=null)Destroy(fx.root);}
            owned.Clear();active.Clear();pools.Clear();
            if(flameMaterial!=null)Destroy(flameMaterial);if(emberMaterial!=null)Destroy(emberMaterial);
            if(dustMaterial!=null)Destroy(dustMaterial);if(ribbonMaterial!=null)Destroy(ribbonMaterial);
            if(impactTextureMaterial!=null)Destroy(impactTextureMaterial);
        }
        static Quaternion SafeRotation(Vector3 direction){return Quaternion.LookRotation(direction,Mathf.Abs(Vector3.Dot(direction,Vector3.up))>.99f?Vector3.forward:Vector3.up);}
        static bool Finite(Vector3 p){return !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));}
        static float Range(Effect fx,float low,float high){return low+(high-low)*(float)fx.random.NextDouble();}
        static float RangeStable(int index,int seed){return .84f+.2f*Mathf.Sin(index*1.37f+seed*.003f);}
        static Vector3 Sphere(Effect fx){float z=Range(fx,-1,1),a=Range(fx,0,Mathf.PI*2),r=Mathf.Sqrt(Mathf.Max(0,1-z*z));return new Vector3(r*Mathf.Cos(a),z,r*Mathf.Sin(a));}
        static Vector3 Hemisphere(Effect fx){Vector3 v=Sphere(fx);return Vector3.Dot(v,fx.direction)<0?v-2*Vector3.Dot(v,fx.direction)*fx.direction:v;}
    }
}
