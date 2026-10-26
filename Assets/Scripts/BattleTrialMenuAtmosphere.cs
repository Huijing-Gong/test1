using UnityEngine;
using UnityEngine.UI;

// One small mesh behind the paper cards. A fixed budget of eight leaves and
// twelve dust flecks, with no particle system, realtime light or imported art.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleTrialMenuAtmosphere : MaskableGraphic
{
    public BattleTrialMenuAtmosphere() { useLegacyMeshGeneration = false; }
    float age;
    void Update() { age += Time.unscaledDeltaTime; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        for (int i = 0; i < 20; i++)
        {
            bool leaf = i < 8;
            float cycle = Mathf.Repeat(age / (leaf ? 16f + i : 11f + i * .4f) + i * .173f, 1f);
            float opacity = Mathf.Sin(cycle * Mathf.PI);
            float x = Mathf.Repeat(127 + i * 277f + cycle * (leaf ? 155 : 40), 1920);
            float y = leaf ? 1080 - cycle * 1150 : cycle * 1130 - 30;
            Vector2 center = new Vector2(x + Mathf.Sin(age * .45f + i) * 18, -y);
            Color tint = leaf ? new Color(.45f, .64f, .36f, .38f * opacity)
                : new Color(1, .94f, .70f, .40f * opacity);
            float radius = leaf ? 7 + i % 3 * 2 : 1.5f + i % 3 * .6f;
            float angle = age * (leaf ? .45f : .15f) + i * 1.2f;
            var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            var across = new Vector2(-along.y, along.x) * (leaf ? .42f : 1f);
            int n = mesh.currentVertCount;
            mesh.AddVert(center - along, tint, Vector2.zero);
            mesh.AddVert(center + across, tint, Vector2.zero);
            mesh.AddVert(center + along, tint, Vector2.zero);
            mesh.AddVert(center - across, tint, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
            if (!leaf) continue;
            Color shade = tint; shade.r *= .7f; shade.g *= .8f;
            mesh.AddVert(center - along * .8f, shade, Vector2.zero);
            mesh.AddVert(center + across * .14f, shade, Vector2.zero);
            mesh.AddVert(center + along * .8f, shade, Vector2.zero);
            mesh.AddTriangle(n + 4, n + 5, n + 6);
        }
    }
}
