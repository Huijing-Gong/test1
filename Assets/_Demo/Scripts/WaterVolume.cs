using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class WaterVolume : MonoBehaviour
{
    [SerializeField] private float waterSurfaceY;
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color debugColor = new Color(0.1f, 0.65f, 1f, 0.2f);

    [SerializeField] private MeshFilter surfaceFootprint;
    private Mesh cachedFootprint;
    private Vector3[] footprintVertices;
    private int[] footprintTriangles;

    public bool ContainsSurfaceXZ(Vector3 worldPoint)
    {
        if (!surfaceFootprint || !surfaceFootprint.sharedMesh) return true;
        var mesh = surfaceFootprint.sharedMesh;
        if (mesh != cachedFootprint) { cachedFootprint = mesh; footprintVertices = mesh.vertices; footprintTriangles = mesh.triangles; }
        var p = surfaceFootprint.transform.InverseTransformPoint(worldPoint);
        for (int i = 0; i < footprintTriangles.Length; i += 3)
        {
            var a = footprintVertices[footprintTriangles[i]]; var b = footprintVertices[footprintTriangles[i+1]]; var c = footprintVertices[footprintTriangles[i+2]];
            float area = (b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
            if (Mathf.Abs(area) < .000001f) continue;
            float ab = (b.x-a.x)*(p.z-a.z)-(b.z-a.z)*(p.x-a.x);
            float bc = (c.x-b.x)*(p.z-b.z)-(c.z-b.z)*(p.x-b.x);
            float ca = (a.x-c.x)*(p.z-c.z)-(a.z-c.z)*(p.x-c.x);
            if ((ab >= -.00001f && bc >= -.00001f && ca >= -.00001f) || (ab <= .00001f && bc <= .00001f && ca <= .00001f)) return true;
        }
        return false;
    }

    private bool validationErrorLogged;
    private Collider volumeCollider;

    public float WaterSurfaceY => waterSurfaceY;
    public Collider VolumeCollider
    {
        get
        {
            if (volumeCollider == null)
            {
                volumeCollider = GetComponent<Collider>();
            }

            return volumeCollider;
        }
    }

    public bool IsValid => isActiveAndEnabled &&
                           VolumeCollider != null &&
                           VolumeCollider.enabled &&
                           VolumeCollider.isTrigger;

    private void Awake()
    {
        ValidateVolume();
    }

    private void OnEnable()
    {
        ValidateVolume();
    }

    private void ValidateVolume()
    {
        volumeCollider = GetComponent<Collider>();
        if (VolumeCollider != null && VolumeCollider.isTrigger)
        {
            validationErrorLogged = false;
            return;
        }

        if (!validationErrorLogged)
        {
            Debug.LogError("WaterVolume requires an enabled 3D Collider configured as a Trigger.", this);
            validationErrorLogged = true;
        }

        enabled = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Collider volumeCollider = GetComponent<Collider>();
        if (volumeCollider == null)
        {
            return;
        }

        Gizmos.color = debugColor;
        Gizmos.DrawCube(volumeCollider.bounds.center, volumeCollider.bounds.size);
        Gizmos.color = new Color(debugColor.r, debugColor.g, debugColor.b, 0.8f);
        Gizmos.DrawWireCube(volumeCollider.bounds.center, volumeCollider.bounds.size);
    }
}
