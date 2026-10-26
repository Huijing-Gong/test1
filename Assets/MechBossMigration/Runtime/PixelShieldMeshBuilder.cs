using UnityEngine;

namespace KingOfGraphic.MechBossMigration
{
public static class PixelShieldMeshBuilder
{
    public static Mesh BuildOpenCylinder(float cylinderRadius, float cylinderHeight, int cylinderSegments)
    {
        // Generate mesh data independently of materials and scene objects.
        cylinderRadius = Mathf.Max(0.5f, cylinderRadius);
        cylinderHeight = Mathf.Max(0.5f, cylinderHeight);
        cylinderSegments = Mathf.Clamp(cylinderSegments, 12, 192);

        // Duplicate the seam vertices so u=0 and u=1 share positions but retain distinct UVs.
        Vector3[] vertices = new Vector3[(cylinderSegments + 1) * 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[cylinderSegments * 6];

        for (int i = 0; i <= cylinderSegments; i++)
        {
            // The horizontal UV coordinate controls pixel columns and orbit around the Y axis.
            float u = i / (float)cylinderSegments;
            float angle = u * Mathf.PI * 2f;
            Vector3 normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = normal * cylinderRadius + Vector3.down * cylinderHeight * 0.5f;
            vertices[i * 2 + 1] = normal * cylinderRadius + Vector3.up * cylinderHeight * 0.5f;
            normals[i * 2] = normal;
            normals[i * 2 + 1] = normal;
            // The vertical UV coordinate controls pixel rows and the boundary rings.
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }

        for (int i = 0; i < cylinderSegments; i++)
        {
            // Each cylinder segment consists of two triangles.
            int vertex = i * 2;
            int tri = i * 6;
            triangles[tri] = vertex;
            triangles[tri + 1] = vertex + 1;
            triangles[tri + 2] = vertex + 2;
            triangles[tri + 3] = vertex + 1;
            triangles[tri + 4] = vertex + 3;
            triangles[tri + 5] = vertex + 2;
        }

        Mesh mesh = new Mesh { name = "PixelShield_OpenCylinder" };
        // Positions define geometry, normals support rim lighting, and UVs define the pixel pattern.
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}

}
