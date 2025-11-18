using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class RayTracer
{
    struct Ray
    {
        public Vector3 origin;
        public Vector3 dir;
    }

    struct Hit
    {
        public float t;
        public Vector3 positionWS;
        public Vector3 normalWS;
        public int materialIndex;
        public bool hit;
    }

    public Texture2D Render(ObjectData scene)
    {
        int width = Mathf.Max(1, scene.Image != null ? scene.Image.horizontal : 256);
        int height = Mathf.Max(1, scene.Image != null ? scene.Image.vertical : 256);
        Color bg = scene.Image != null ? scene.Image.background : Color.black;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        Matrix4x4 sceneMat = Matrix4x4.identity;
        if (scene.Camera != null && scene.Camera.transformationIndex >= 0 && scene.Camera.transformationIndex < scene.Transformations.Count)
        {
            var camComp = BuildComposite(scene.Transformations[scene.Camera.transformationIndex]);
            sceneMat = camComp; // apply camera composite to the scene directly
        }
        Debug.Log($"[RT] Camera tIndex={scene.Camera?.transformationIndex} dist={scene.Camera?.distance} vfov={scene.Camera?.verticalFovDeg}");
        Debug.Log($"[RT] Scene (camera) matrix (direct):\n{sceneMat}");

        List<(Vector3 posWS, Color rgb)> lightPoints = new List<(Vector3, Color)>();
        foreach (var light in scene.Lights)
        {
            Matrix4x4 lm = sceneMat * BuildByIndex(scene, light.transformationIndex);
            Vector3 pos = lm.MultiplyPoint3x4(Vector3.zero);
            lightPoints.Add((pos, light.rgb));
        }
        // Extra diagnostics: sample object placements
        if (scene.Spheres.Count > 0)
        {
            var s = scene.Spheres[0];
            var om = sceneMat * BuildByIndex(scene, s.transformationIndex);
            Vector3 c = om.MultiplyPoint3x4(Vector3.zero);
            Debug.Log($"[RT] Sphere[0] center WS ~ {c}");
        }
        if (scene.Boxes.Count > 0)
        {
            var b = scene.Boxes[0];
            var om = sceneMat * BuildByIndex(scene, b.transformationIndex);
            Vector3 c = om.MultiplyPoint3x4(Vector3.zero);
            Debug.Log($"[RT] Box[0] center WS ~ {c}");
        }

        // Compute camera distance and image plane size early so diagnostics can use them
        float d = scene.Camera != null ? Mathf.Max(0.0001f, scene.Camera.distance) : 1f;
        float vfov = scene.Camera != null ? Mathf.Max(0.0001f, scene.Camera.verticalFovDeg) : 60f;
        float aspect = (float)width / (float)height;
        float halfH = d * Mathf.Tan(0.5f * vfov * Mathf.Deg2Rad);
        float planeH = 2f * halfH;
        float planeW = planeH * aspect;

        if (scene.TriangleMeshes.Count > 0 && scene.TriangleMeshes[0].Triangles.Count > 0)
        {
            var m = scene.TriangleMeshes[0];
            var om = sceneMat * BuildByIndex(scene, m.transformationIndex);
            var tri = m.Triangles[0];
            Vector3 aWS = om.MultiplyPoint3x4(tri.v0);
            Vector3 bWS = om.MultiplyPoint3x4(tri.v1);
            Vector3 cWS = om.MultiplyPoint3x4(tri.v2);
            Debug.Log($"[RT] Tri[0] v0.z={aWS.z:F2} v1.z={bWS.z:F2} v2.z={cWS.z:F2}");
            // Project aWS to z=0 along line from camera (0,0,d)
            if (Mathf.Abs(aWS.z - 0f) > 1e-6f)
            {
                float tProj = (0f -  d) / (aWS.z - d); // param along line from cam to aWS to reach z=0
                Vector3 aOnPlane = new Vector3(0,0,d) + tProj * (aWS - new Vector3(0,0,d));
                Debug.Log($"[RT] Tri[0].v0 projected on z=0: x'={aOnPlane.x:F2}, y'={aOnPlane.y:F2}; plane halfW={planeW/2f:F2}, halfH={planeH/2f:F2}");
            }
            // World-space intersection test for center ray
            Ray centerRay = new Ray { origin = new Vector3(0,0,d), dir = new Vector3(0,0,-1) };
            if (IntersectTriangleWS(centerRay, aWS, bWS, cWS, out float tWS, out _))
            {
                Debug.Log($"[RT] Center ray hits Tri[0] in WS at t={tWS:F3}");
            }
        }

        int cy = height / 2; int cx = width / 2; // center pixel for debugging
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = ((x + 0.5f) / width - 0.5f) * planeW;
                float v = ((y + 0.5f) / height - 0.5f) * planeH;
                Ray ray = new Ray
                {
                    origin = new Vector3(0f, 0f, d),
                    dir = new Vector3(u, v, -d).normalized
                };

                bool dbg = (x == cx && y == cy);
                if (dbg)
                {
                    Debug.Log($"[RT] Center pixel ray origin={ray.origin} dir={ray.dir}");
                }
                Color c = TracePrimary(scene, sceneMat, lightPoints, ray, bg, dbg);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }

    Color TracePrimary(ObjectData scene, Matrix4x4 sceneMat, List<(Vector3 posWS, Color rgb)> lights, Ray rayWS, Color bg, bool debug = false)
    {
        Hit best = new Hit { t = float.MaxValue, hit = false };
        float bestSphere = float.PositiveInfinity;
        float bestBox = float.PositiveInfinity;
        float bestTri = float.PositiveInfinity;

        // Spheres
        for (int i = 0; i < scene.Spheres.Count; i++)
        {
            var s = scene.Spheres[i];
            Matrix4x4 om = sceneMat * BuildByIndex(scene, s.transformationIndex);
            Matrix4x4 inv = om.inverse;
            Ray rOS = TransformRay(inv, rayWS);
            if (IntersectUnitSphere(rOS, out float tOS))
            {
                Vector3 pOS = rOS.origin + tOS * rOS.dir;
                Vector3 nOS = pOS.normalized;
                Vector3 pWS = om.MultiplyPoint3x4(pOS);
                Vector3 nWS = (inv.transpose.MultiplyVector(nOS)).normalized;
                float tWS = (pWS - rayWS.origin).magnitude;
                if (tWS < bestSphere) bestSphere = tWS;
                if (tWS > 1e-4f && tWS < best.t)
                {
                    best = new Hit { t = tWS, hit = true, positionWS = pWS, normalWS = nWS, materialIndex = s.materialIndex };
                }
            }
        }

        // Boxes (unit cube centered at origin, bounds [-0.5, 0.5])
        for (int i = 0; i < scene.Boxes.Count; i++)
        {
            var b = scene.Boxes[i];
            Matrix4x4 om = sceneMat * BuildByIndex(scene, b.transformationIndex);
            Matrix4x4 inv = om.inverse;
            Ray rOS = TransformRay(inv, rayWS);
            if (IntersectUnitBox(rOS, out float tOS, out Vector3 nOS))
            {
                Vector3 pOS = rOS.origin + tOS * rOS.dir;
                Vector3 pWS = om.MultiplyPoint3x4(pOS);
                Vector3 nWS = (inv.transpose.MultiplyVector(nOS)).normalized;
                float tWS = (pWS - rayWS.origin).magnitude;
                if (tWS < bestBox) bestBox = tWS;
                if (tWS > 1e-4f && tWS < best.t)
                {
                    best = new Hit { t = tWS, hit = true, positionWS = pWS, normalWS = nWS, materialIndex = b.materialIndex };
                }
            }
        }

        // Triangles (intersect in world space to avoid numeric issues with non-uniform transforms)
        for (int mi = 0; mi < scene.TriangleMeshes.Count; mi++)
        {
            var m = scene.TriangleMeshes[mi];
            Matrix4x4 om = sceneMat * BuildByIndex(scene, m.transformationIndex);
            Matrix4x4 inv = om.inverse;
            for (int ti = 0; ti < m.Triangles.Count; ti++)
            {
                var tri = m.Triangles[ti];
                Vector3 aWS = om.MultiplyPoint3x4(tri.v0);
                Vector3 bWS = om.MultiplyPoint3x4(tri.v1);
                Vector3 cWS = om.MultiplyPoint3x4(tri.v2);
                if (IntersectTriangleWS(rayWS, aWS, bWS, cWS, out float tWS, out _))
                {
                    Vector3 pWS = rayWS.origin + tWS * rayWS.dir;
                    Vector3 nWS = Vector3.Cross(bWS - aWS, cWS - aWS).normalized;
                    if (tWS < bestTri) bestTri = tWS;
                    if (tWS > 1e-4f && tWS < best.t)
                    {
                        best = new Hit { t = tWS, hit = true, positionWS = pWS, normalWS = nWS, materialIndex = tri.materialIndex };
                    }
                }
            }
        }
        if (debug)
        {
            Debug.Log($"[RT] Closest by type (center pixel): sphere={bestSphere}, box={bestBox}, tri={bestTri}");
            if (best.hit)
                Debug.Log($"[RT] Hit: t={best.t} pos={best.positionWS} n={best.normalWS} mat={best.materialIndex}");
            else
                Debug.Log("[RT] No hit, returning background");
        }
        if (!best.hit) return bg;
        return Shade(scene, lights, best, rayWS);
    }

    Color Shade(ObjectData scene, List<(Vector3 posWS, Color rgb)> lights, Hit hit, Ray rayWS)
    {
        Color baseCol = Color.white;
        float ka = 0.1f, kd = 0.7f, ks = 0.2f;
        float shininess = 32f;
        if (hit.materialIndex >= 0 && hit.materialIndex < scene.Materials.Count)
        {
            var m = scene.Materials[hit.materialIndex];
            baseCol = m.color;
            ka = m.ambient;
            kd = m.diffuse;
            ks = m.specular;
        }

        Vector3 N = hit.normalWS.normalized;
        Vector3 V = (-rayWS.dir).normalized;
        Color result = ka * baseCol;

        foreach (var lp in lights)
        {
            Vector3 L = (lp.posWS - hit.positionWS).normalized;
            float NdotL = Mathf.Max(0f, Vector3.Dot(N, L));
            Color diffuse = kd * NdotL * Mul(baseCol, lp.rgb);

            Vector3 H = (L + V).normalized;
            float NdotH = Mathf.Max(0f, Vector3.Dot(N, H));
            Color spec = ks * Mathf.Pow(NdotH, shininess) * lp.rgb;

            result += diffuse + spec;
        }

        result.r = Mathf.Clamp01(result.r);
        result.g = Mathf.Clamp01(result.g);
        result.b = Mathf.Clamp01(result.b);
        result.a = 1f;
        return result;
    }

    static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

    Matrix4x4 BuildByIndex(ObjectData scene, int index)
    {
        if (index < 0 || index >= scene.Transformations.Count) return Matrix4x4.identity;
        return BuildComposite(scene.Transformations[index]);
    }

    Matrix4x4 BuildComposite(CompositeTransformation comp)
    {
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
            // Aplica a transformação atual à direita da matriz acumulada (M = M * transform)
            M = M * transform;
        }
        return M;
    }

    static Ray TransformRay(Matrix4x4 M, Ray r)
    {
        Vector3 o = M.MultiplyPoint3x4(r.origin);
        Vector3 d = (M.MultiplyVector(r.dir)).normalized;
        return new Ray { origin = o, dir = d };
    }

    static bool IntersectUnitSphere(Ray r, out float t)
    {
        // sphere radius 1 centered at origin
        float a = Vector3.Dot(r.dir, r.dir);
        float b = 2f * Vector3.Dot(r.origin, r.dir);
        float c = Vector3.Dot(r.origin, r.origin) - 1f;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) { t = 0f; return false; }
        float s = Mathf.Sqrt(disc);
        float t0 = (-b - s) / (2f * a);
        float t1 = (-b + s) / (2f * a);
        t = t0;
        if (t < 1e-4f) t = t1;
        return t >= 1e-4f;
    }

    static bool IntersectUnitBox(Ray r, out float t, out Vector3 n)
    {
        // bounds [-0.5, 0.5]
        Vector3 min = new Vector3(-0.5f, -0.5f, -0.5f);
        Vector3 max = new Vector3(0.5f, 0.5f, 0.5f);
        t = 0f; n = Vector3.zero;
        float tmin = -1e20f, tmax = 1e20f;
        Vector3 nmin = Vector3.zero, nmax = Vector3.zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? r.origin.x : axis == 1 ? r.origin.y : r.origin.z;
            float d = axis == 0 ? r.dir.x : axis == 1 ? r.dir.y : r.dir.z;
            float invD = Mathf.Abs(d) > 1e-8f ? 1f / d : float.PositiveInfinity;
            float t1 = ( (axis==0?min.x:axis==1?min.y:min.z) - o) * invD;
            float t2 = ( (axis==0?max.x:axis==1?max.y:max.z) - o) * invD;
            Vector3 n1 = Vector3.zero; Vector3 n2 = Vector3.zero;
            if (axis == 0) { n1 = new Vector3(-1,0,0); n2 = new Vector3(1,0,0); }
            else if (axis == 1) { n1 = new Vector3(0,-1,0); n2 = new Vector3(0,1,0); }
            else { n1 = new Vector3(0,0,-1); n2 = new Vector3(0,0,1); }
            if (t1 > t2) { (t1, t2) = (t2, t1); (n1, n2) = (n2, n1); }
            if (t1 > tmin) { tmin = t1; nmin = n1; }
            if (t2 < tmax) { tmax = t2; nmax = n2; }
            if (tmin > tmax) return false;
            if (tmax < 1e-4f) return false;
        }
        t = tmin >= 1e-4f ? tmin : tmax;
        n = t == tmin ? nmin : nmax;
        return t >= 1e-4f;
    }

    static bool IntersectTriangle(Ray r, Vector3 a, Vector3 b, Vector3 c, out float t, out Vector3 bary)
    {
        t = 0f; bary = Vector3.zero;
        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 p = Vector3.Cross(r.dir, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-8f) return false;
        float invDet = 1f / det;
        Vector3 s = r.origin - a;
        float u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(r.dir, q) * invDet;
        if (v < 0f || u + v > 1f) return false;
        float tt = Vector3.Dot(e2, q) * invDet;
        if (tt < 1e-4f) return false;
        t = tt;
        bary = new Vector3(1f - u - v, u, v);
        return true;
    }

    static bool IntersectTriangleWS(Ray r, Vector3 aWS, Vector3 bWS, Vector3 cWS, out float t, out Vector3 bary)
    {
        t = 0f; bary = Vector3.zero;
        Vector3 e1 = bWS - aWS;
        Vector3 e2 = cWS - aWS;
        Vector3 p = Vector3.Cross(r.dir, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-8f) return false;
        float invDet = 1f / det;
        Vector3 s = r.origin - aWS;
        float u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(r.dir, q) * invDet;
        if (v < 0f || u + v > 1f) return false;
        float tt = Vector3.Dot(e2, q) * invDet;
        if (tt < 1e-4f) return false;
        t = tt;
        bary = new Vector3(1f - u - v, u, v);
        return true;
    }

    public static void SaveTexture(Texture2D tex, string path)
    {
        byte[] png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);
    }
}
