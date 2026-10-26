using UnityEngine;
using UnityEngine.UI;

// A clipped, low-intensity diagonal highlight. Native vertex colours, the
// standard UI shader, no extra shader variants, masks or full-screen effects.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleTrialButtonSheen : MaskableGraphic
{
    public BattleTrialButtonSheen() { useLegacyMeshGeneration = false; }
    float phase = -1;
    public float Phase => phase;
    public void SetPhase(float value)
    {
        if (Mathf.Approximately(phase, value)) return;
        phase = value; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (phase < 0) return;
        Rect r = rectTransform.rect;
        const int columns = 48, rows = 4;
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            int n = mesh.currentVertCount;
            Add(mesh, r, x / (float)columns, y / (float)rows);
            Add(mesh, r, (x + 1) / (float)columns, y / (float)rows);
            Add(mesh, r, (x + 1) / (float)columns, (y + 1) / (float)rows);
            Add(mesh, r, x / (float)columns, (y + 1) / (float)rows);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
        }
    }
    void Add(VertexHelper mesh, Rect r, float x, float y)
    {
        float edge = Mathf.Clamp01(Mathf.Min(x, 1 - x) * 20f);
        float band = Mathf.Max(0, 1 - Mathf.Abs(x + y * .14f - Mathf.Lerp(-.2f, 1.4f, phase)) / .10f);
        float alpha = .27f * band * band * edge * Mathf.Sin(y * Mathf.PI);
        mesh.AddVert(new Vector3(Mathf.Lerp(r.xMin + 18, r.xMax - 18, x),
            Mathf.Lerp(r.yMin + 9, r.yMax - 9, y), 0), new Color(1, 1, .87f, alpha), Vector2.zero);
    }
}
