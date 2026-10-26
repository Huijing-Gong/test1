using System.Collections.Generic;
using UnityEngine;

namespace SamProduction.BossArena
{
    public sealed class ArenaCombatFx : MonoBehaviour
    {
        public Material orange, cyan, warning;
        public ArenaFlameFx flames;
        public ArenaBlackSunFx blackSun;
        [Tooltip("Show ground outlines for attack ranges. Disabled for the current combat presentation.")]
        public bool showAttackRangeWarnings = false;
        private LineRenderer telegraph;
        readonly LineRenderer[] courtArcs=new LineRenderer[4];
        private readonly List<Burst> active = new List<Burst>();
        private readonly Stack<Burst> pool = new Stack<Burst>();
        private readonly List<Burst> owned = new List<Burst>();
        private int serial;
        public int ActiveCount => active.Count + (flames != null ? flames.ActiveCount : 0) + (blackSun != null && blackSun.Active ? 1 : 0);
        public bool WarningVisible => (telegraph != null && telegraph.enabled)||(courtArcs[0]!=null&&courtArcs[0].enabled);
        private sealed class Burst
        {
            public GameObject root;
            public LineRenderer[] lines;
            public Vector3 centre;
            public float age, maximum;
            public bool ground;
            public Vector3[] directions;
        }
        public static float PulseRadius(float age, float maximum) => Mathf.Lerp(.15f, maximum, Mathf.Clamp01(age / .36f));

        private LineRenderer Line(string name, Transform parent, Material material, float width, int count)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.positionCount = count;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void EnsureWarning()
        { if (telegraph == null) telegraph = Line("Attack warning", transform, warning, .055f, 65); telegraph.enabled = true; }

        public void WarnRing(Vector3 centre, float radius, float progress)
        {
            if (!showAttackRangeWarnings) { HideWarning(); return; }
            EnsureWarning();
            telegraph.positionCount = 65;
            centre.y += .027f;
            for (int i = 0; i <= 64; i++)
            { float a = i * Mathf.PI * 2 / 64; telegraph.SetPosition(i, centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius); }
            telegraph.widthMultiplier = Mathf.Lerp(.04f, .10f, progress);
        }

        public void WarnStrike(Vector3 centre, Vector3 forward, float reach, float width, float progress)
        {
            if (!showAttackRangeWarnings) { HideWarning(); return; }
            EnsureWarning();
            centre.y += .03f;
            Vector3 right = Vector3.Cross(Vector3.up, forward) * width * .5f;
            telegraph.positionCount = 5;
            telegraph.SetPositions(new[] { centre - right, centre + forward * (reach - .3f) - right,
                centre + forward * reach, centre + forward * (reach - .3f) + right, centre + right });
            telegraph.widthMultiplier = Mathf.Lerp(.035f, .07f, progress);
        }

        public void WarnCourt(Vector3 centre,float radius,float halfExtent,float progress)
        {
            HideWarning();
            if (!showAttackRangeWarnings) return;
            float begin=Mathf.Acos(Mathf.Clamp01(halfExtent/radius)),end=Mathf.PI*.5f-begin;
            if(end<=begin)return;
            for(int q=0;q<4;q++)
            {
                if(!courtArcs[q])courtArcs[q]=Line("Court warning corner "+q,transform,warning,.12f,25);
                var line=courtArcs[q];line.enabled=true;line.widthMultiplier=Mathf.Lerp(.08f,.16f,progress);
                for(int i=0;i<25;i++){float angle=q*Mathf.PI*.5f+Mathf.Lerp(begin,end,i/24f);line.SetPosition(i,centre+new Vector3(Mathf.Cos(angle)*radius,.035f,Mathf.Sin(angle)*radius));}
            }
        }
        public void HideWarning() { if (telegraph != null) telegraph.enabled = false;foreach(var line in courtArcs)if(line)line.enabled=false; }

        public int BeginBlackSun(Vector3 point, float radius, Camera view) => blackSun != null ? blackSun.Begin(point, radius, view) : 0;
        public void PoseBlackSun(int token, float time, Vector3 point, bool grounded) => blackSun?.Pose(token, time, point, grounded);
        public bool ReleaseBlackSun(int token, Vector3 point) => blackSun != null && blackSun.Release(token, point);
        public void ClearBlackSun() => blackSun?.Clear();

        public void Charge(Transform socket) => flames?.Charge(socket);
        public void Swing(Transform socket, Vector3 direction) => flames?.Swing(socket, direction);

        private Burst GetBurst()
        {
            if (active.Count >= 20) Return(active[0]);
            Burst burst;
            if (pool.Count > 0) burst = pool.Pop();
            else
            {
                burst = new Burst { root = new GameObject("Pooled combat response"), lines = new LineRenderer[9], directions = new Vector3[9] };
                burst.root.transform.SetParent(transform, false);
                for (int i = 0; i < burst.lines.Length; i++) burst.lines[i] = Line("Arc " + i, burst.root.transform, orange, .026f, 2);
                owned.Add(burst);
            }
            burst.age = 0;
            burst.root.SetActive(true);
            active.Add(burst);
            return burst;
        }

        public void Impact(Vector3 point, bool successful, ArenaTeam source, Vector3 direction = default, bool suppressBossFlame = false)
        {
            // The dash owner releases its fire once even on a miss. Its actual hit
            // callback suppresses only this legacy fire; contact sparks remain.
            if (flames != null && successful && source == ArenaTeam.Boss && !suppressBossFlame)
                flames.Hit(point, direction.sqrMagnitude > .001f ? direction.normalized : Vector3.up);
            Burst b = GetBurst();
            b.centre = point;
            b.ground = false;
            b.maximum = successful ? .50f : .34f;
            var random = new System.Random(++serial * 7919);
            for (int i = 0; i < b.lines.Length; i++)
            {
                b.directions[i] = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 1.4f - .3f,
                    (float)random.NextDouble() * 2 - 1).normalized;
                b.lines[i].sharedMaterial = successful ? orange : cyan;
                b.lines[i].positionCount = 2;
                b.lines[i].widthMultiplier = successful ? .028f : .018f;
            }
        }

        public void GroundPulse(Vector3 point, float maximum)
        {
            if (flames != null) { flames.GroundPulse(point, maximum); return; }
            Burst b = GetBurst();
            b.centre = point + Vector3.up * .035f;
            b.ground = true;
            b.maximum = maximum;
            for (int i = 0; i < b.lines.Length; i++)
            { b.lines[i].sharedMaterial = orange; b.lines[i].positionCount = i == 0 ? 65 : 8; }
        }

        public void Tick(float delta)
        {
            flames?.Tick(delta);
            blackSun?.Tick(delta);
            for (int n = active.Count - 1; n >= 0; n--)
            {
                Burst b = active[n]; b.age += delta;
                float life = b.ground ? .62f : .24f;
                if (b.age >= life) { Return(b); continue; }
                float fade = 1 - b.age / life;
                if (b.ground)
                {
                    float radius = PulseRadius(b.age, b.maximum);
                    for (int i = 0; i <= 64; i++)
                    { float a = i * Mathf.PI * 2 / 64; b.lines[0].SetPosition(i, b.centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius); }
                    b.lines[0].widthMultiplier = .17f * fade;
                    for (int j = 1; j < b.lines.Length; j++)
                    {
                        float a = j * Mathf.PI * .25f;
                        Vector3 ray = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        for (int k = 0; k < 8; k++)
                            b.lines[j].SetPosition(k, b.centre + ray * radius * (k / 7f) + Vector3.up * Mathf.Sin(k * Mathf.PI / 7) * .18f * fade);
                        b.lines[j].widthMultiplier = .035f * fade;
                    }
                }
                else for (int j = 0; j < b.lines.Length; j++)
                {
                    float travel = b.age * 2;
                    b.lines[j].SetPosition(0, b.centre + b.directions[j] * travel);
                    b.lines[j].SetPosition(1, b.centre + b.directions[j] * (travel + b.maximum * fade));
                    b.lines[j].widthMultiplier = .035f * fade;
                }
            }
        }

        private void Return(Burst b) { active.Remove(b); b.root.SetActive(false); pool.Push(b); }
        public void ResetEffects()
        { while (active.Count > 0) Return(active[active.Count - 1]); flames?.ResetEffects(); blackSun?.Clear(); HideWarning(); serial = 0; }
    }
}
