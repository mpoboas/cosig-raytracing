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
            return Shade(scene, lights, rec, rayWS);
        }

        if (debug)
        {
            Debug.Log("[RT] No hit, returning background");
        }
        return backgroundColor;
    }

    Color Shade(ObjectData scene, List<(Vector3 posWS, Color rgb)> lights, HitRecord hit, Ray rayWS)
    {
        Color baseColor = Color.white;
        float ambientCoeff = 0.1f, diffuseCoeff = 0.7f, specularCoeff = 0.2f;
        float shininess = 32f;
        
        if (hit.materialIndex >= 0 && hit.materialIndex < scene.Materials.Count)
        {
            var m = scene.Materials[hit.materialIndex];
            baseColor = m.color;
            ambientCoeff = m.ambient;
            diffuseCoeff = m.diffuse;
            specularCoeff = m.specular;
        }

        Vector3 N = hit.normalWS.normalized;
        Vector3 V = (-rayWS.direction).normalized;
        Color result = ambientCoeff * baseColor;

        foreach (var lp in lights)
        {
            Vector3 L = (lp.posWS - hit.positionWS).normalized;
            float NdotL = Mathf.Max(0f, Vector3.Dot(N, L));
            Color diffuse = diffuseCoeff * NdotL * Mul(baseColor, lp.rgb);

            Vector3 H = (L + V).normalized;
            float NdotH = Mathf.Max(0f, Vector3.Dot(N, H));
            Color specular = specularCoeff * Mathf.Pow(NdotH, shininess) * lp.rgb;

            result += diffuse + specular;
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
