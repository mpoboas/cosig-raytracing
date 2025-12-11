using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class RayTracer
{
    // We use UnityEngine.Ray and the HitRecord defined in IHittable.cs

    public Texture2D Render(ObjectData scene)
    {
        // Synchronous wrapper for backward compatibility
        var task = RenderAsync(scene, null, CancellationToken.None);
        task.Wait();
        var (colors, width, height) = task.Result;
        
        Texture2D outputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        outputTexture.SetPixels(colors);
        outputTexture.Apply();
        return outputTexture;
    }

    public async Task<(Color[] pixels, int width, int height)> RenderAsync(ObjectData scene, IProgress<float> progress, CancellationToken token)
    {
        int width = Mathf.Max(1, scene.Image != null ? scene.Image.horizontal : 256);
        int height = Mathf.Max(1, scene.Image != null ? scene.Image.vertical : 256);
        Color backgroundColor = scene.Image != null ? scene.Image.background : Color.black;

        Color[] pixels = new Color[width * height];

        // --- Camera Setup ---
        Matrix4x4 sceneMat = Matrix4x4.identity;
        if (scene.Camera != null && scene.Camera.transformationIndex >= 0 && scene.Camera.transformationIndex < scene.Transformations.Count)
        {
            var camComp = BuildComposite(scene.Transformations[scene.Camera.transformationIndex]);
            sceneMat = camComp; // apply camera composite to the scene directly
        }
        Debug.Log($"[RT] Camera tIndex={scene.Camera?.transformationIndex} dist={scene.Camera?.distance} vfov={scene.Camera?.verticalFovDeg}");
        Debug.Log($"[RT] Scene (camera) matrix (direct):\n{sceneMat}");

        // --- Light Setup ---
        List<(Vector3 posWS, Color rgb)> lightPoints = new List<(Vector3, Color)>();
        foreach (var light in scene.Lights)
        {
            Matrix4x4 lm = sceneMat * BuildByIndex(scene, light.transformationIndex);
            Vector3 pos = lm.MultiplyPoint3x4(Vector3.zero);
            lightPoints.Add((pos, light.rgb));
        }

        // --- Build BVH ---
        Debug.Log("[RT] Building BVH...");
        IHittable bvhRoot = BuildBVH(scene, sceneMat);
        Debug.Log("[RT] BVH Built.");

        // --- Projection Plane Calculations ---
        float cameraDistance = scene.Camera != null ? Mathf.Max(0.0001f, scene.Camera.distance) : 1f;
        float verticalFov = scene.Camera != null ? Mathf.Max(0.0001f, scene.Camera.verticalFovDeg) : 60f;
        float aspect = (float)width / (float)height;
        float halfHeight = cameraDistance * Mathf.Tan(0.5f * verticalFov * Mathf.Deg2Rad);
        float planeHeight = 2f * halfHeight;
        float planeWidth = planeHeight * aspect;

        // --- Main Ray Tracing Loop ---
        int cy = height / 2; int cx = width / 2; // center pixel for debugging
        
        // Run on a background thread
        await Task.Run(() =>
        {
            for (int y = 0; y < height; y++)
            {
                if (token.IsCancellationRequested) break;

                for (int x = 0; x < width; x++)
                {
                    // Calculate ray direction through the pixel
                    float u = ((x + 0.5f) / width - 0.5f) * planeWidth;
                    float v = ((y + 0.5f) / height - 0.5f) * planeHeight;
                    
                    // UnityEngine.Ray takes origin and direction
                    Vector3 rayDir = new Vector3(u, v, -cameraDistance).normalized;
                    Ray ray = new Ray(new Vector3(0f, 0f, cameraDistance), rayDir);

                    // Debug specific pixels from the support document - Apoio ao debugging - pontos de interseção
                    bool dbg = (y == 0 && x == 100) ||
                               (y == 50 && x == 50) ||
                               (y == 50 && x == 80) ||
                               (y == 80 && x == 100) ||
                               (y == 110 && x == 150);

                    if (dbg)
                    {
                        // Note: Debug.Log is thread-safe in recent Unity versions, but be careful
                        Debug.Log($"[RT] Debugging Pixel (x={x}, y={y}) Ray origin={ray.origin} dir={ray.direction}");
                    }
                    
                    // Trace the ray and get the color
                    Color c = TracePrimary(scene, lightPoints, bvhRoot, ray, backgroundColor, dbg);
                    
                    // Store in array (SetPixel is not thread safe)
                    pixels[y * width + x] = c;
                }
                
                // Report progress
                progress?.Report((float)(y + 1) / height);
            }
        }, token);

        return (pixels, width, height);
    }

    IHittable BuildBVH(ObjectData scene, Matrix4x4 sceneMat)
    {
        List<IHittable> objects = new List<IHittable>();

        // Spheres
        for (int i = 0; i < scene.Spheres.Count; i++)
        {
            var s = scene.Spheres[i];
            Matrix4x4 objectToWorld = sceneMat * BuildByIndex(scene, s.transformationIndex);
            objects.Add(new SphereInstance(s.materialIndex, objectToWorld));
        }

        // Boxes
        for (int i = 0; i < scene.Boxes.Count; i++)
        {
            var b = scene.Boxes[i];
            Matrix4x4 objectToWorld = sceneMat * BuildByIndex(scene, b.transformationIndex);
            objects.Add(new BoxInstance(b.materialIndex, objectToWorld));
        }

        // Triangles
        for (int mi = 0; mi < scene.TriangleMeshes.Count; mi++)
        {
            var m = scene.TriangleMeshes[mi];
            Matrix4x4 objectToWorld = sceneMat * BuildByIndex(scene, m.transformationIndex);
            
            for (int ti = 0; ti < m.Triangles.Count; ti++)
            {
                var tri = m.Triangles[ti];
                Vector3 v0_WS = objectToWorld.MultiplyPoint3x4(tri.v0);
                Vector3 v1_WS = objectToWorld.MultiplyPoint3x4(tri.v1);
                Vector3 v2_WS = objectToWorld.MultiplyPoint3x4(tri.v2);
                
                objects.Add(new TriangleInstance(tri.materialIndex, v0_WS, v1_WS, v2_WS));
            }
        }

        if (objects.Count == 0) return new BVHNode(new List<IHittable>()); // Empty node

        return new BVHNode(objects);
    }

    Color TracePrimary(ObjectData scene, List<(Vector3 posWS, Color rgb)> lights, IHittable bvhRoot, Ray rayWS, Color backgroundColor, bool debug = false)
    {
        if (bvhRoot.Hit(rayWS, 1e-6f, float.MaxValue, out HitRecord rec))
        {
            if (debug)
            {
                Debug.Log($"[RT] Hit: t={rec.t} pos=({rec.positionWS.x:F10}, {rec.positionWS.y:F10}, {rec.positionWS.z:F10}) n={rec.normalWS} mat={rec.materialIndex}");
            }
            return Shade(scene, lights, rec, rayWS, bvhRoot);
        }

        if (debug)
        {
            Debug.Log("[RT] No hit, returning background");
        }
        return backgroundColor;
    }

    Color Shade(ObjectData scene, List<(Vector3 posWS, Color rgb)> lights, HitRecord hit, Ray rayWS, IHittable bvhRoot)
    {
        // Default material properties
        Color matColor = Color.white;
        float kAmbient = 0.1f;
        float kDiffuse = 0.7f;
        
        // Retrieve material properties if valid
        if (hit.materialIndex >= 0 && hit.materialIndex < scene.Materials.Count)
        {
            var m = scene.Materials[hit.materialIndex];
            matColor = m.color;
            kAmbient = m.ambient;
            kDiffuse = m.diffuse;
        }

        Vector3 N = hit.normalWS.normalized;
        Color totalColor = Color.black;

        // Iterate through all lights
        foreach (var lp in lights)
        {
            // 1. Ambient Component: light.Color * material.Color * material.Ambient
            // Note: Assignment says treat ambient as originating from the specific light source
            Color ambient = Mul(lp.rgb, matColor) * kAmbient;

            // 2. Diffuse Component (Lambertian)
            // Calculate vector L from intersection point to light
            Vector3 L_vec = lp.posWS - hit.positionWS;
            float tLight = L_vec.magnitude;
            Vector3 L = L_vec / tLight; // Normalize manually to keep length for shadow check or use .normalized

            float cosTheta = Vector3.Dot(N, L);
            
            Color diffuse = Color.black;
            
            // Only calculate diffuse if facing the light
            if (cosTheta > 0)
            {
                bool inShadow = false;

                // --- Shadow Projection Step ---
                // We cast a shadow ray from the hit point towards the light.
                // If it hits anything between the surface (t > epsilon) and the light (t < tLight),
                // then the point is in shadow.
                
                Ray shadowRay = new Ray(hit.positionWS, L);
                
                // Use epsilon (1e-3f) for tMin to avoid "shadow acne" (self-intersection due to float precision).
                // Use tLight for tMax so we don't check for objects BEHIND the light.
                if (bvhRoot.Hit(shadowRay, 1e-3f, tLight, out HitRecord shadowRec))
                {
                    inShadow = true;
                }

                if (!inShadow)
                {
                    // light.Color * material.Color * material.Diffuse * cosTheta
                    diffuse = Mul(lp.rgb, matColor) * kDiffuse * cosTheta;
                }
            }
            // else diffuse is 0 (back face or shadowed)

            // Accumulate
            totalColor += ambient + diffuse;
        }

        // Average the result
        if (lights.Count > 0)
        {
            totalColor /= (float)lights.Count;
        }

        // Ensure alpha is 1
        totalColor.a = 1f;
        
        // Clamp components to be safe (though not explicitly asked, it's good practice for display)
        totalColor.r = Mathf.Clamp01(totalColor.r);
        totalColor.g = Mathf.Clamp01(totalColor.g);
        totalColor.b = Mathf.Clamp01(totalColor.b);

        return totalColor;
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
            // Apply current transform to the right of the accumulated matrix (M = M * transform)
            M = M * transform;
        }
        return M;
    }

    public static void SaveTexture(Texture2D tex, string path)
    {
        byte[] png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);
    }
}
