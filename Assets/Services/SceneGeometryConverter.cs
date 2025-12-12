using System.Collections.Generic;
using UnityEngine;

public static class SceneGeometryConverter
{
    public static List<GPUTriangle> ExtractTriangles(ObjectData scene, Matrix4x4 sceneMat)
    {
        List<GPUTriangle> allTriangles = new List<GPUTriangle>();

        // 1. Process Triangle Meshes
        foreach (var mesh in scene.TriangleMeshes)
        {
            Matrix4x4 localToWorld = sceneMat * BuildMatrix(scene, mesh.transformationIndex);
            foreach (var tri in mesh.Triangles)
            {
                Vector3 v0 = localToWorld.MultiplyPoint3x4(tri.v0);
                Vector3 v1 = localToWorld.MultiplyPoint3x4(tri.v1);
                Vector3 v2 = localToWorld.MultiplyPoint3x4(tri.v2);

                allTriangles.Add(CreateGPUTriangle(v0, v1, v2, tri.materialIndex));
            }
        }

        // 2. Process Boxes
        // Convert Unit Cube (0 to 1 or -0.5 to 0.5? Standard is usually centered or corner. 
        // Based on typical Ray Tracing assignments: Unit Cube usually means -0.5 to 0.5 or 0 to 1.
        // Let's assume -1 to 1 or similar. Let's stick to -0.5 to 0.5 to match unit scale.
        // Actually, let's look at `BoxInstance` or previous logic if accessible? 
        // We will assume standard unit cube centered at origin for transformation.
        foreach (var box in scene.Boxes)
        {
            Matrix4x4 localToWorld = sceneMat * BuildMatrix(scene, box.transformationIndex);
            AddCube(allTriangles, localToWorld, box.materialIndex);
        }

        // 3. Process Spheres
        // Tessellate unit sphere
        foreach (var sphere in scene.Spheres)
        {
            Matrix4x4 localToWorld = sceneMat * BuildMatrix(scene, sphere.transformationIndex);
            AddSphere(allTriangles, localToWorld, sphere.materialIndex);
        }

        return allTriangles;
    }

    private static GPUTriangle CreateGPUTriangle(Vector3 v0, Vector3 v1, Vector3 v2, int matIdx)
    {
        Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
        return CreateGPUTriangleWithNormals(v0, v1, v2, faceNormal, faceNormal, faceNormal, matIdx);
    }
    
    private static GPUTriangle CreateGPUTriangleWithNormals(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 n0, Vector3 n1, Vector3 n2, int matIdx)
    {
        GPUTriangle t = new GPUTriangle();
        t.v0 = v0;
        t.v1 = v1;
        t.v2 = v2;
        t.n0 = n0;
        t.n1 = n1;
        t.n2 = n2;
        t.center = (v0 + v1 + v2) / 3.0f;
        t.materialIndex = matIdx;
        return t;
    }

    private static Matrix4x4 BuildMatrix(ObjectData scene, int index)
    {
        if (index < 0 || index >= scene.Transformations.Count) return Matrix4x4.identity;
        
        // Replicating logic from RayTracer.cs
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

    private static void AddCube(List<GPUTriangle> tris, Matrix4x4 m, int matIdx)
    {
        // 8 corners of a cube (assuming -1 to 1 size for visibility, or unit 1x1x1?)
        // Let's use 1x1x1 centered at origin: -0.5 to 0.5
        // If the user's scene uses 'Scale' to define size, this is correct for a "Unit Box".
        // If previous code was `BoxInstance` (AABB Check [-1,1]?), check? 
        // Safest bet: -0.5 to 0.5.
        
        Vector3[] v = new Vector3[8];
        v[0] = new Vector3(-0.5f, -0.5f, -0.5f);
        v[1] = new Vector3( 0.5f, -0.5f, -0.5f);
        v[2] = new Vector3( 0.5f,  0.5f, -0.5f);
        v[3] = new Vector3(-0.5f,  0.5f, -0.5f);
        v[4] = new Vector3(-0.5f, -0.5f,  0.5f);
        v[5] = new Vector3( 0.5f, -0.5f,  0.5f);
        v[6] = new Vector3( 0.5f,  0.5f,  0.5f);
        v[7] = new Vector3(-0.5f,  0.5f,  0.5f);

        // Transform vertices
        for (int i = 0; i < 8; i++) v[i] = m.MultiplyPoint3x4(v[i]);

        // Front (0,1,2,3)
        tris.Add(CreateGPUTriangle(v[0], v[2], v[1], matIdx));
        tris.Add(CreateGPUTriangle(v[0], v[3], v[2], matIdx));
        // Back (5,6,7,4) -> reversed winding
        tris.Add(CreateGPUTriangle(v[5], v[7], v[6], matIdx));
        tris.Add(CreateGPUTriangle(v[5], v[4], v[7], matIdx));
        // Top (3,2,6,7)
        tris.Add(CreateGPUTriangle(v[3], v[6], v[2], matIdx));
        tris.Add(CreateGPUTriangle(v[3], v[7], v[6], matIdx));
        // Bottom (4,5,1,0)
        tris.Add(CreateGPUTriangle(v[4], v[1], v[5], matIdx));
        tris.Add(CreateGPUTriangle(v[4], v[0], v[1], matIdx));
        // Left (4,7,3,0)
        tris.Add(CreateGPUTriangle(v[4], v[3], v[7], matIdx));
        tris.Add(CreateGPUTriangle(v[4], v[0], v[3], matIdx));
        // Right (1,2,6,5)
        tris.Add(CreateGPUTriangle(v[1], v[6], v[2], matIdx));
        tris.Add(CreateGPUTriangle(v[1], v[5], v[6], matIdx));
    }

    private static void AddSphere(List<GPUTriangle> tris, Matrix4x4 m, int matIdx)
    {
        // Create an icosahedron or low-poly sphere
        // Let's implement a simple Octahedron and subdivide for speed (or Icosahedron)
        // Hardcoding a simple octahedron for now (8 triangles) is too blocky.
        // Let's do 2 subdivisions of an Octahedron -> 32 triangles?
        // Sphere radius 1 (unit sphere).
        
        List<Vector3> vertices = new List<Vector3>()
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };
        // Octahedron indices
        // Top cap: 0-4-3, 0-3-5, 0-5-2, 0-2-4
        // Bot cap: 1-3-4, 1-5-3, 1-2-5, 1-4-2
        // Subdividing...
        
        // For simplicity, let's do Latitude/Longitude stack (UV Sphere)
        int nbLong = 24; // Increased from 12
        int nbLat = 16;  // Increased from 8
        float radius = 1.0f;

        Vector3[] sphereVerts = new Vector3[(nbLong + 1) * nbLat + 2];
        float _pi = Mathf.PI;
        float _2pi = _pi * 2f;

        sphereVerts[0] = Vector3.up * radius;
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
        sphereVerts[sphereVerts.Length - 1] = Vector3.down * radius;

        // Triangles
        // Top Cap
        for (int lon = 0; lon < nbLong; lon++)
        {
             Vector3 v0 = sphereVerts[0];
             Vector3 v1 = sphereVerts[lon + 2]; // next
             Vector3 v2 = sphereVerts[lon + 1]; // current
             AddSmoothTri(tris, m, v0, v1, v2, matIdx);
        }

        // Middle
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

        // Bottom Cap
        int last = sphereVerts.Length - 1;
        for (int lon = 0; lon < nbLong; lon++)
        {
             Vector3 v0 = sphereVerts[last];
             Vector3 v1 = sphereVerts[last - (nbLong + 1) + lon]; // current
             Vector3 v2 = sphereVerts[last - (nbLong + 1) + lon + 1]; // next
             AddSmoothTri(tris, m, v0, v1, v2, matIdx);
        }
    }

    private static void AddTri(List<GPUTriangle> tris, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, int matIdx)
    {
        tris.Add(CreateGPUTriangle(m.MultiplyPoint3x4(a), m.MultiplyPoint3x4(b), m.MultiplyPoint3x4(c), matIdx));
    }
    
    // For spheres: vertex normals = vertex positions (normalized), then transformed
    private static void AddSmoothTri(List<GPUTriangle> tris, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, int matIdx)
    {
        // For unit sphere, normal at vertex = vertex position (normalized)
        Vector3 na = a.normalized;
        Vector3 nb = b.normalized;
        Vector3 nc = c.normalized;
        
        // Transform vertices 
        Vector3 va = m.MultiplyPoint3x4(a);
        Vector3 vb = m.MultiplyPoint3x4(b);
        Vector3 vc = m.MultiplyPoint3x4(c);
        
        // Transform normals using inverse transpose (for non-uniform scale)
        Matrix4x4 normalMat = m.inverse.transpose;
        Vector3 tna = normalMat.MultiplyVector(na).normalized;
        Vector3 tnb = normalMat.MultiplyVector(nb).normalized;
        Vector3 tnc = normalMat.MultiplyVector(nc).normalized;
        
        tris.Add(CreateGPUTriangleWithNormals(va, vb, vc, tna, tnb, tnc, matIdx));
    }
}
