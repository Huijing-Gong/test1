using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamProduction.BossArena
{
    // Uses the authored SAM fire renderer for moving fists. The ground wave has its
    // own geometry and follows the same radius function as gameplay damage.
    public sealed class ArenaFlameFx : MonoBehaviour
    {
        public SamVfxPresenter fire;
        public Material groundFlame, groundGlow;
        [Range(.2f, 1.6f)] public float crownHeight = .82f;
        private readonly List<Wave> active = new List<Wave>();
        private readonly Stack<Wave> pool = new Stack<Wave>();
        private readonly List<Wave> owned = new List<Wave>();
        private int serial;
        public int ActiveCount => active.Count + (fire != null ? fire.ActiveEffectCount : 0);
        public float LatestWaveRadius { get; private set; }
        public Vector3 LatestWaveCentre { get; private set; }

        private const int Tongues = 24, Rows = 5, Columns = 5, RingSegments = 96;
        private sealed class Wave
        {
            public GameObject root;
            public Mesh crown, ring;
            public MeshRenderer crownRenderer, ringRenderer;
            public Light light;
            public Vector3 centre;
            public float age, maximum, seed;
            public readonly Vector3[] crownVertices = new Vector3[Tongues * Rows * Columns];
            public readonly Vector3[] ringVertices = new Vector3[(RingSegments + 1) * 2];
            public readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        }

        public void Charge(Transform socket)
        {
            if (fire == null || socket == null) return;
            fire.Play("Ignition", socket, socket.position, Vector3.up, ++serial);
        }

        public void Swing(Transform socket, Vector3 direction)
        {
            if (fire == null || socket == null) return;
            fire.Play("PunchTrail", socket, socket.position, direction, ++serial);
        }

        public void Hit(Vector3 point, Vector3 direction)
        {
            if (fire != null) fire.Play("PunchImpact", null, point, direction, ++serial);
        }

        public void GroundPulse(Vector3 point, float maximum)
        {
            if (groundFlame == null || groundGlow == null) return;
            if (active.Count >= 4) Release(active[0]);
            Wave wave = pool.Count > 0 ? pool.Pop() : CreateWave();
            wave.centre = point + Vector3.up * .018f;
            wave.maximum = maximum; wave.age = 0; wave.seed = ++serial * .731f;
            wave.root.SetActive(true); active.Add(wave); UpdateWave(wave);
            if (fire != null) fire.Play("LandDust", null, point, Vector3.up, ++serial);
        }

        public void Tick(float delta)
        {
            if (fire != null) { fire.SampleReleasedTransfers(); fire.Advance(delta); }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Wave wave = active[i]; wave.age += delta;
                if (wave.age >= .92f) { Release(wave); continue; }
                UpdateWave(wave);
            }
        }

        private Wave CreateWave()
        {
            var wave = new Wave { root = new GameObject("Pooled furnace wave") };
            wave.root.transform.SetParent(transform, false);
            wave.crown = CreateMesh(wave.root.transform, "Ground fire tongues", groundFlame, out wave.crownRenderer);
            wave.ring = CreateMesh(wave.root.transform, "Ground heat front", groundGlow, out wave.ringRenderer);
            var uv = new Vector2[wave.crownVertices.Length];
            var colors = new Color[uv.Length];
            var triangles = new List<int>();
            for (int petal = 0; petal < Tongues; petal++)
                for (int row = 0; row < Rows; row++)
                    for (int column = 0; column < Columns; column++)
                    {
                        int index = petal * Rows * Columns + row * Columns + column;
                        uv[index] = new Vector2(column / (Columns - 1f), row / (Rows - 1f));
                        colors[index] = Color.white;
                        if (row + 1 < Rows && column + 1 < Columns)
                        {
                            triangles.Add(index); triangles.Add(index + Columns); triangles.Add(index + 1);
                            triangles.Add(index + 1); triangles.Add(index + Columns); triangles.Add(index + Columns + 1);
                        }
                    }
            wave.crown.vertices = wave.crownVertices; wave.crown.uv = uv; wave.crown.colors = colors;
            wave.crown.SetTriangles(triangles, 0);
            var ringUV = new Vector2[wave.ringVertices.Length];
            var ringColors = new Color[ringUV.Length];
            triangles.Clear();
            for (int i = 0; i <= RingSegments; i++)
            {
                ringUV[i * 2] = new Vector2(i / (float)RingSegments, 0);
                ringUV[i * 2 + 1] = new Vector2(i / (float)RingSegments, 1);
                ringColors[i * 2] = ringColors[i * 2 + 1] = Color.white;
                if (i < RingSegments)
                {
                    int j = i * 2;
                    triangles.Add(j); triangles.Add(j + 2); triangles.Add(j + 1);
                    triangles.Add(j + 1); triangles.Add(j + 2); triangles.Add(j + 3);
                }
            }
            wave.ring.vertices = wave.ringVertices; wave.ring.uv = ringUV; wave.ring.colors = ringColors;
            wave.ring.SetTriangles(triangles, 0);
            wave.light = new GameObject("Ground fire bounce").AddComponent<Light>();
            wave.light.transform.SetParent(wave.root.transform, false);
            wave.light.type = LightType.Point; wave.light.color = new Color(1, .25f, .045f);
            wave.light.shadows = LightShadows.None;
            owned.Add(wave);
            return wave;
        }

        private static Mesh CreateMesh(Transform parent, string label, Material material, out MeshRenderer renderer)
        {
            var obj = new GameObject(label); obj.transform.SetParent(parent, false);
            renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            var mesh = new Mesh { name = label }; mesh.MarkDynamic();
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            return mesh;
        }

        private void UpdateWave(Wave wave)
        {
            float radius = ArenaCombatFx.PulseRadius(wave.age, wave.maximum);
            LatestWaveRadius = radius; LatestWaveCentre = wave.centre;
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.26f, .92f, wave.age));
            float rise = Mathf.SmoothStep(0, 1, Mathf.Clamp01(wave.age / .10f));
            float height = crownHeight * (.35f + .65f * rise) * fade;
            for (int petal = 0; petal < Tongues; petal++)
            {
                float angle = petal * Mathf.PI * 2 / Tongues;
                float variation = .67f + .33f * Mathf.Sin(petal * 4.71f + wave.seed);
                for (int row = 0; row < Rows; row++)
                    for (int column = 0; column < Columns; column++)
                    {
                        float v = row / (Rows - 1f), u = column / (Columns - 1f) - .5f;
                        float curl = Mathf.Sin(v * 3.2f + wave.age * 8 + petal) * .16f * v;
                        float a = angle + u * .32f * (1 - v * .68f) + curl / Mathf.Max(radius, .2f);
                        float radial = radius - .12f + v * v * .30f;
                        float y = height * variation * v * (1 - Mathf.Abs(u) * .4f);
                        wave.crownVertices[petal * Rows * Columns + row * Columns + column] =
                            wave.root.transform.InverseTransformPoint(wave.centre + new Vector3(Mathf.Sin(a) * radial, y, Mathf.Cos(a) * radial));
                    }
            }
            wave.crown.vertices = wave.crownVertices; wave.crown.RecalculateBounds();
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2 / RingSegments;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                wave.ringVertices[i * 2] = wave.root.transform.InverseTransformPoint(wave.centre + direction * Mathf.Max(0, radius - .72f));
                wave.ringVertices[i * 2 + 1] = wave.root.transform.InverseTransformPoint(wave.centre + direction * radius);
            }
            wave.ring.vertices = wave.ringVertices; wave.ring.RecalculateBounds();
            wave.properties.SetFloat("_LocalTime", wave.age);
            wave.properties.SetFloat("_Seed", wave.seed);
            wave.properties.SetFloat("_Opacity", .84f * fade);
            wave.crownRenderer.SetPropertyBlock(wave.properties);
            wave.properties.SetFloat("_Opacity", .48f * fade);
            wave.ringRenderer.SetPropertyBlock(wave.properties);
            wave.light.transform.position = wave.centre + Vector3.up * .75f;
            wave.light.range = wave.maximum + 2;
            wave.light.intensity = 4.5f * Mathf.Exp(-wave.age * 6.5f);
        }

        private void Release(Wave wave) { active.Remove(wave); wave.root.SetActive(false); pool.Push(wave); }
        public void ResetEffects()
        {
            while (active.Count > 0) Release(active[active.Count - 1]);
            if (fire != null) fire.StopAll(true);
            serial = 0; LatestWaveRadius = 0;
        }
        private void OnDisable() => ResetEffects();
        private void OnDestroy()
        {
            foreach (Wave wave in owned) { Destroy(wave.crown); Destroy(wave.ring); }
        }
    }
}
