using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts scene primitives (meshes, spheres, boxes) into GPU-compatible triangles.
/// All geometry is extracted in Object Space (without camera transforms) to enable
/// static BVH caching - the BVH only needs rebuilding when geometry changes.
/// </summary>
public static class SceneGeometryConverter
{
    /// <summary>
    /// Extracts all scene geometry as GPU triangles in Object Space.
    /// Each primitive is transformed by its object matrix only (no camera transform).
    /// This enables the BVH to remain static when only the camera moves.
    /// </summary>
    /// <param name="scene">Scene data containing meshes, spheres, and boxes</param>
    /// <returns>List of triangles in Object Space coordinates</returns>
    public static List<GPUTriangle> ExtractTriangles(ObjectData scene)
    {
        List<GPUTriangle> allTriangles = new List<GPUTriangle>();

        // Process triangle meshes (apply object-to-world transform only)
        foreach (var mesh in scene.TriangleMeshes)
        {
            Matrix4x4 objectToWorld = BuildMatrix(scene, mesh.transformationIndex);
            foreach (var tri in mesh.Triangles)
            {
                Vector3 v0 = objectToWorld.MultiplyPoint3x4(tri.v0);
                Vector3 v1 = objectToWorld.MultiplyPoint3x4(tri.v1);
                Vector3 v2 = objectToWorld.MultiplyPoint3x4(tri.v2);

                allTriangles.Add(CreateGPUTriangle(v0, v1, v2, tri.materialIndex));
            }
        }

        // Process boxes (unit cube centered at origin, scaled by transformation)
        foreach (var box in scene.Boxes)
        {
            Matrix4x4 objectToWorld = BuildMatrix(scene, box.transformationIndex);
            AddCube(allTriangles, objectToWorld, box.materialIndex);
        }

        // Process spheres (UV sphere tessellation with smooth normals)
        foreach (var sphere in scene.Spheres)
        {
            Matrix4x4 objectToWorld = BuildMatrix(scene, sphere.transformationIndex);
            AddSphere(allTriangles, objectToWorld, sphere.materialIndex);
        }

        return allTriangles;
    }

    /// <summary>
    /// Creates a GPU triangle with flat-shaded normals (face normal at all vertices).
    /// </summary>
    private static GPUTriangle CreateGPUTriangle(Vector3 v0, Vector3 v1, Vector3 v2, int matIdx)
    {
        Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
        return CreateGPUTriangleWithNormals(v0, v1, v2, faceNormal, faceNormal, faceNormal, matIdx);
    }
    
    /// <summary>
    /// Creates a GPU triangle with per-vertex normals for smooth shading.
    /// </summary>
    private static GPUTriangle CreateGPUTriangleWithNormals(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 n0, Vector3 n1, Vector3 n2, int matIdx)
    {
        GPUTriangle t = new GPUTriangle();
        t.v0 = v0;
        t.v1 = v1;
        t.v2 = v2;
        t.n0 = n0;
        t.n1 = n1;
        t.n2 = n2;
        t.center = (v0 + v1 + v2) / 3.0f; // Pre-computed for BVH partitioning
        t.materialIndex = matIdx;
        return t;
    }

    /// <summary>
    /// Builds a 4x4 transformation matrix from a composite transformation.
    /// Transforms are applied in sequence (left-to-right multiplication).
    /// </summary>
    private static Matrix4x4 BuildMatrix(ObjectData scene, int index)
    {
        if (index < 0 || index >= scene.Transformations.Count) return Matrix4x4.identity;
        
        var comp = scene.Transformations[index];
        Matrix4x4 M = Matrix4x4.identity;
        
        foreach (var e in comp.Elements)
        {
            Matrix4x4 transform = Matrix4x4.identity;
            switch (e.Type)
            {
                case TransformType.T:
                    transform = Matrix4x4.Translate(e.XYZ);
                    break;
                case TransformType.S:
                    transform = Matrix4x4.Scale(e.XYZ);
                    break;
                case TransformType.Rx:
                    transform = Matrix4x4.Rotate(Quaternion.AngleAxis(e.AngleDeg, Vector3.right));
                    break;
                case TransformType.Ry:
                    transform = Matrix4x4.Rotate(Quaternion.AngleAxis(e.AngleDeg, Vector3.up));
                    break;
                case TransformType.Rz:
                    transform = Matrix4x4.Rotate(Quaternion.AngleAxis(e.AngleDeg, Vector3.forward));
                    break;
            }
            M = M * transform;
        }
        return M;
    }

    /// <summary>
    /// Generates a unit cube (from -0.5 to +0.5 on each axis) as 12 triangles.
    /// The cube is then transformed by the provided matrix.
    /// </summary>
    private static void AddCube(List<GPUTriangle> tris, Matrix4x4 m, int matIdx)
    {
        // Define 8 corners of a unit cube centered at origin
        Vector3[] v = new Vector3[8];
        v[0] = new Vector3(-0.5f, -0.5f, -0.5f);
        v[1] = new Vector3( 0.5f, -0.5f, -0.5f);
        v[2] = new Vector3( 0.5f,  0.5f, -0.5f);
        v[3] = new Vector3(-0.5f,  0.5f, -0.5f);
        v[4] = new Vector3(-0.5f, -0.5f,  0.5f);
        v[5] = new Vector3( 0.5f, -0.5f,  0.5f);
        v[6] = new Vector3( 0.5f,  0.5f,  0.5f);
        v[7] = new Vector3(-0.5f,  0.5f,  0.5f);

        // Transform all vertices to world space
        for (int i = 0; i < 8; i++) v[i] = m.MultiplyPoint3x4(v[i]);

        // Generate 2 triangles per face (6 faces = 12 triangles)
        // Front face (-Z)
        tris.Add(CreateGPUTriangle(v[0], v[2], v[1], matIdx));
        tris.Add(CreateGPUTriangle(v[0], v[3], v[2], matIdx));
        // Back face (+Z)
        tris.Add(CreateGPUTriangle(v[5], v[7], v[6], matIdx));
        tris.Add(CreateGPUTriangle(v[5], v[4], v[7], matIdx));
        // Top face (+Y)
        tris.Add(CreateGPUTriangle(v[3], v[6], v[2], matIdx));
        tris.Add(CreateGPUTriangle(v[3], v[7], v[6], matIdx));
        // Bottom face (-Y)
        tris.Add(CreateGPUTriangle(v[4], v[1], v[5], matIdx));
        tris.Add(CreateGPUTriangle(v[4], v[0], v[1], matIdx));
        // Left face (-X)
        tris.Add(CreateGPUTriangle(v[4], v[3], v[7], matIdx));
        tris.Add(CreateGPUTriangle(v[4], v[0], v[3], matIdx));
        // Right face (+X)
        tris.Add(CreateGPUTriangle(v[1], v[6], v[2], matIdx));
        tris.Add(CreateGPUTriangle(v[1], v[5], v[6], matIdx));
    }

    /// <summary>
    /// Generates a UV sphere (latitude/longitude tessellation) with smooth vertex normals.
    /// Resolution: 24 longitude segments x 16 latitude segments = 768 triangles.
    /// </summary>
    private static void AddSphere(List<GPUTriangle> tris, Matrix4x4 m, int matIdx)
    {
        int nbLong = 24;  // Longitude segments (horizontal slices)
        int nbLat = 16;   // Latitude segments (vertical slices)
        float radius = 1.0f;

        // Generate vertices using spherical coordinates
        Vector3[] sphereVerts = new Vector3[(nbLong + 1) * nbLat + 2];
        float _pi = Mathf.PI;
        float _2pi = _pi * 2f;

        // Top pole
        sphereVerts[0] = Vector3.up * radius;
        
        // Middle vertices (latitude rings)
        for (int lat = 0; lat < nbLat; lat++)
        {
            float a1 = _pi * (float)(lat + 1) / (nbLat + 1);
            float sin1 = Mathf.Sin(a1);
            float cos1 = Mathf.Cos(a1);

            for (int lon = 0; lon <= nbLong; lon++)
            {
                float a2 = _2pi * (float)(lon == nbLong ? 0 : lon) / nbLong;
                float sin2 = Mathf.Sin(a2);
                float cos2 = Mathf.Cos(a2);

                sphereVerts[lon + lat * (nbLong + 1) + 1] = new Vector3(sin1 * cos2, cos1, sin1 * sin2) * radius;
            }
        }
        
        // Bottom pole
        sphereVerts[sphereVerts.Length - 1] = Vector3.down * radius;

        // Generate triangles with smooth normals
        
        // Top cap (triangles from pole to first ring)
        for (int lon = 0; lon < nbLong; lon++)
        {
             Vector3 v0 = sphereVerts[0];
             Vector3 v1 = sphereVerts[lon + 2];
             Vector3 v2 = sphereVerts[lon + 1];
             AddSmoothTri(tris, m, v0, v1, v2, matIdx);
        }

        // Middle bands (quad strips between latitude rings)
        for (int lat = 0; lat < nbLat - 1; lat++)
        {
            for (int lon = 0; lon < nbLong; lon++)
            {
                int current = lon + lat * (nbLong + 1) + 1;
                int next = current + 1;
                int below = current + (nbLong + 1);
                int belowNext = below + 1;

                AddSmoothTri(tris, m, sphereVerts[current], sphereVerts[below], sphereVerts[next], matIdx);
                AddSmoothTri(tris, m, sphereVerts[next], sphereVerts[below], sphereVerts[belowNext], matIdx);
            }
        }

        // Bottom cap (triangles from last ring to pole)
        int last = sphereVerts.Length - 1;
        for (int lon = 0; lon < nbLong; lon++)
        {
             Vector3 v0 = sphereVerts[last];
             Vector3 v1 = sphereVerts[last - (nbLong + 1) + lon];
             Vector3 v2 = sphereVerts[last - (nbLong + 1) + lon + 1];
             AddSmoothTri(tris, m, v0, v1, v2, matIdx);
        }
    }

    /// <summary>
    /// Adds a flat-shaded triangle (all vertices share face normal).
    /// </summary>
    private static void AddTri(List<GPUTriangle> tris, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, int matIdx)
    {
        tris.Add(CreateGPUTriangle(m.MultiplyPoint3x4(a), m.MultiplyPoint3x4(b), m.MultiplyPoint3x4(c), matIdx));
    }

    /// <summary>
    /// Adds a smooth-shaded triangle with per-vertex normals.
    /// For a unit sphere, vertex normal = normalized vertex position.
    /// Normals are transformed using the inverse-transpose to handle non-uniform scaling.
    /// </summary>
    private static void AddSmoothTri(List<GPUTriangle> tris, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, int matIdx)
    {
        // For unit sphere: normal at vertex = vertex position (normalized)
        Vector3 na = a.normalized;
        Vector3 nb = b.normalized;
        Vector3 nc = c.normalized;
        
        // Transform vertices to world space
        Vector3 va = m.MultiplyPoint3x4(a);
        Vector3 vb = m.MultiplyPoint3x4(b);
        Vector3 vc = m.MultiplyPoint3x4(c);
        
        // Transform normals using inverse-transpose (correct for non-uniform scale)
        Matrix4x4 normalMat = m.inverse.transpose;
        Vector3 tna = normalMat.MultiplyVector(na).normalized;
        Vector3 tnb = normalMat.MultiplyVector(nb).normalized;
        Vector3 tnc = normalMat.MultiplyVector(nc).normalized;
        
        tris.Add(CreateGPUTriangleWithNormals(va, vb, vc, tna, tnb, tnc, matIdx));
    }
}
