using System;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public sealed class OwnInstancedMeadow : MonoBehaviour
{
    [Serializable]
    public sealed class Batch
    {
        public Mesh mesh;
        public Mesh farMesh;
        [Min(1f)] public float detailDistance = 28f;
        public Material material;
        // Visit-only override; the original asset and distant instances are untouched.
        [NonSerialized] public Material presentationMaterial;
        [NonSerialized] public Vector4 presentationRegion;
        public bool castShadows;
        public Matrix4x4 meshToRoot = Matrix4x4.identity;
        public Vector3[] positions = Array.Empty<Vector3>();
        public Vector3[] eulers = Array.Empty<Vector3>();
        public Vector3[] scales = Array.Empty<Vector3>();
    }
    public Batch[] batches = Array.Empty<Batch>();
    public float distance = 150f;
    Matrix4x4[][][] matrices;
    Matrix4x4[][][] originalMatrices;
    Bounds[][] spatialBounds;
    [NonSerialized] public bool BypassSpatialCullingForQA;
    readonly Plane[] frustum = new Plane[6];
    readonly Matrix4x4[] visible = new Matrix4x4[1023];
    readonly int[] farIndices = new int[1023];
    readonly Matrix4x4[] farVisible = new Matrix4x4[1023];
    readonly Matrix4x4[] presentationVisible = new Matrix4x4[1023];
    readonly Matrix4x4[] unchangedVisible = new Matrix4x4[1023];
    public int InstanceCount { get; private set; }

    void OnEnable() { Rebuild(); RenderPipelineManager.beginCameraRendering += DrawCamera; }
    void OnDisable() { RenderPipelineManager.beginCameraRendering -= DrawCamera; matrices = null; originalMatrices = null; spatialBounds = null; }
    void OnValidate() { Rebuild(); }
    public void Rebuild()
    {
        InstanceCount = 0;
        matrices = new Matrix4x4[batches.Length][][];
        originalMatrices = new Matrix4x4[batches.Length][][];
        spatialBounds = new Bounds[batches.Length][];
        for (int b = 0; b < batches.Length; b++)
        {
            var batch = batches[b];
            int count = batch.positions.Length;
            matrices[b] = new Matrix4x4[(count + 1022) / 1023][];
            for (int c = 0; c < matrices[b].Length; c++)
            {
                int length = Mathf.Min(1023, count - c * 1023);
                matrices[b][c] = new Matrix4x4[length];
                for (int j = 0; j < length; j++)
                {
                    int i = c * 1023 + j;
                    matrices[b][c][j] = Matrix4x4.TRS(batch.positions[i], Quaternion.Euler(batch.eulers[i]), batch.scales[i]) * batch.meshToRoot;
                }
            }
            originalMatrices[b] = matrices[b];
            bool opaque = batch.material && batch.material.renderQueue < 3000 && (!batch.material.HasProperty("_Surface") || batch.material.GetFloat("_Surface") == 0);
            if (!batch.castShadows && opaque && batch.mesh)
            {
                Bounds local = batch.mesh.bounds;
                if (batch.farMesh) local.Encapsulate(batch.farMesh.bounds);
                var cells = new System.Collections.Generic.Dictionary<Vector2Int, System.Collections.Generic.List<Matrix4x4>>();
                foreach (var chunk in matrices[b]) foreach (var matrix in chunk)
                {
                    Vector3 p = matrix.GetColumn(3);
                    var key = new Vector2Int(Mathf.FloorToInt(p.x / 18), Mathf.FloorToInt(p.z / 18));
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new System.Collections.Generic.List<Matrix4x4>();
                    list.Add(matrix);
                }
                var groups = new System.Collections.Generic.List<Matrix4x4[]>();
                var bounds = new System.Collections.Generic.List<Bounds>();
                foreach (var cell in cells.Values)
                {
                    for (int first = 0; first < cell.Count; first += 1023)
                    {
                        int length = Mathf.Min(1023, cell.Count - first);
                        var group = cell.GetRange(first, length).ToArray();
                        Bounds bound = WorldBounds(group[0], local);
                        for (int j = 1; j < length; j++) bound.Encapsulate(WorldBounds(group[j], local));
                        groups.Add(group); bounds.Add(bound);
                    }
                }
                matrices[b] = groups.ToArray(); spatialBounds[b] = bounds.ToArray();
            }
            InstanceCount += count;
        }
    }
    static Bounds WorldBounds(Matrix4x4 matrix, Bounds local)
    {
        Vector3 e = local.extents;
        Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
        Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
        Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
        Vector3 ext = new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x), Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y), Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
        var bounds = new Bounds(matrix.MultiplyPoint3x4(local.center), ext * 2);
        bounds.Expand(.3f);
        return bounds;
    }
    void DrawVisible(Mesh mesh, Batch batch, Matrix4x4[] transforms, int count, Camera camera)
    {
        if(!batch.presentationMaterial){DrawBatch(mesh,batch,batch.material,transforms,count,camera);return;}
        int affected=0,unchanged=0;Vector4 region=batch.presentationRegion;
        for(int i=0;i<count;i++)
        {
            Vector4 p=transforms[i].GetColumn(3);float x=p.x-region.x,z=p.z-region.z;
            if(p.y>=region.y && x*x+z*z<=region.w*region.w)presentationVisible[affected++]=transforms[i];
            else unchangedVisible[unchanged++]=transforms[i];
        }
        if(unchanged>0)DrawBatch(mesh,batch,batch.material,unchangedVisible,unchanged,camera);
        if(affected>0)DrawBatch(mesh,batch,batch.presentationMaterial,presentationVisible,affected,camera);
    }
    void DrawBatch(Mesh mesh,Batch batch,Material material,Matrix4x4[] transforms,int count,Camera camera)
    {
        Graphics.DrawMeshInstanced(mesh,0,material,transforms,count,null,
            batch.castShadows?ShadowCastingMode.On:ShadowCastingMode.Off,true,gameObject.layer,camera,LightProbeUsage.Off);
    }
    void DrawCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!camera || camera.cameraType == CameraType.Reflection || matrices == null) return;
        GeometryUtility.CalculateFrustumPlanes(camera, frustum);
        Vector3 eye = camera.transform.position;
        float limit = distance * distance;
        Vector4 eyePoint = new Vector4(eye.x, eye.y, eye.z, 1);
        for (int b = 0; b < batches.Length; b++)
        {
            var batch = batches[b];
            if (!batch.mesh || !batch.material || !batch.material.enableInstancing) continue;
            float nearLimit = batch.detailDistance * batch.detailDistance;
            if (!BypassSpatialCullingForQA && spatialBounds[b] != null)
            {
                int nearCount = 0, farCount = 0;
                for (int cell = 0; cell < matrices[b].Length; cell++)
                {
                    if (!GeometryUtility.TestPlanesAABB(frustum, spatialBounds[b][cell])) continue;
                    foreach (var matrix in matrices[b][cell])
                    {
                        float range = (matrix.GetColumn(3) - eyePoint).sqrMagnitude;
                        if (range >= limit) continue;
                        if (!batch.farMesh || range < nearLimit)
                        {
                            visible[nearCount++] = matrix;
                            if (nearCount == 1023) { DrawVisible(batch.mesh, batch, visible, nearCount, camera); nearCount = 0; }
                        }
                        else
                        {
                            farVisible[farCount++] = matrix;
                            if (farCount == 1023) { DrawVisible(batch.farMesh, batch, farVisible, farCount, camera); farCount = 0; }
                        }
                    }
                }
                if (nearCount > 0) DrawVisible(batch.mesh, batch, visible, nearCount, camera);
                if (farCount > 0) DrawVisible(batch.farMesh, batch, farVisible, farCount, camera);
                continue;
            }
            var chunks = BypassSpatialCullingForQA ? originalMatrices[b] : matrices[b];
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                if (!BypassSpatialCullingForQA && spatialBounds[b] != null && !GeometryUtility.TestPlanesAABB(frustum, spatialBounds[b][chunkIndex])) continue;
                var chunk = chunks[chunkIndex];
                int count = 0, farCount = 0;
                for (int j = 0; j < chunk.Length; j++)
                {
                    var matrix = chunk[j];
                    float range = (matrix.GetColumn(3) - eyePoint).sqrMagnitude;
                    if (range < limit)
                    {
                        if (!batch.farMesh || range < nearLimit) visible[count++] = matrix;
                        else if (range >= nearLimit) farIndices[farCount++] = j;
                    }
                }
                if (count > 0)
                    DrawVisible(batch.mesh,batch,visible,count,camera);
                if (!batch.farMesh || farCount == 0) continue;
                for (int j = 0; j < farCount; j++) visible[j] = chunk[farIndices[j]];
                DrawVisible(batch.farMesh,batch,visible,farCount,camera);
            }
        }
    }
}
