using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class RayTracer
{
    // We use UnityEngine.Ray and the HitRecord defined in IHittable.cs

    public Texture2D Render(ObjectData scene, RenderSettings settings)
    {
        // Synchronous wrapper for backward compatibility
        var task = RenderAsync(scene, settings, null, CancellationToken.None);
        task.Wait();
        var (colors, width, height) = task.Result;
        
        Texture2D outputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        outputTexture.SetPixels(colors);
        outputTexture.Apply();
        return outputTexture;
    }

    public async Task<(Color[] pixels, int width, int height)> RenderAsync(ObjectData scene, RenderSettings settings, IProgress<float> progress, CancellationToken token)
    {
        // Apply Resolution Override
        int width = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.x : Mathf.Max(1, scene.Image != null ? scene.Image.horizontal : 256);
        int height = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.y : Mathf.Max(1, scene.Image != null ? scene.Image.vertical : 256);
        
        // Apply Background Color Override
        Color backgroundColor = settings.BackgroundColorOverride.HasValue ? settings.BackgroundColorOverride.Value : (scene.Image != null ? scene.Image.background : Color.black);

        Color[] pixels = new Color[width * height];

        // --- Camera Setup ---
        Matrix4x4 sceneMat = Matrix4x4.identity;

        // Check if we have overrides for the camera
        if (settings.CameraPositionOverride.HasValue || settings.CameraRotationOverride.HasValue)
        {
            // Construct View Matrix from overrides
            // User inputs Camera World Position and Rotation.
            // The scene transformation matrix (View Matrix) is the Inverse of the Camera's World Matrix.
            
            // Default to identity/zero if one component is missing but the other present
            Vector3 camPos = settings.CameraPositionOverride ?? Vector3.zero;
            Vector3 camRotEuler = settings.CameraRotationOverride ?? Vector3.zero;
            Quaternion camRot = Quaternion.Euler(camRotEuler);

            // Camera -> World
            Matrix4x4 cameraToWorld = Matrix4x4.TRS(camPos, camRot, Vector3.one);
            
            // World -> Camera (Scene Matrix)
            sceneMat = cameraToWorld.inverse;
        }
        else if (scene.Camera != null && scene.Camera.transformationIndex >= 0 && scene.Camera.transformationIndex < scene.Transformations.Count)
        {
            // Use existing scene camera setup
            var camComp = BuildComposite(scene.Transformations[scene.Camera.transformationIndex]);
            sceneMat = camComp; 
        }

        // Camera Distance always comes from scene (no UI override for distance currently)
        float cameraDistance = scene.Camera != null ? Mathf.Max(0.0001f, scene.Camera.distance) : 1f;

        // FOV can be overridden from UI
        float verticalFov = settings.CameraFovOverride.HasValue ? settings.CameraFovOverride.Value : (scene.Camera != null ? Mathf.Max(0.0001f, scene.Camera.verticalFovDeg) : 60f);

        Debug.Log($"[RT] Camera tIndex={scene.Camera?.transformationIndex} dist={cameraDistance} vfov={verticalFov}");
        Debug.Log($"[RT] Scene (camera) matrix (direct):\n{sceneMat}");
        
        // --- Light Setup ---
        // Apply Intensity Scale
        float intensityScale = Mathf.Max(0f, settings.LightIntensityScale);
        List<(Vector3 posWS, Color rgb)> lightPoints = new List<(Vector3, Color)>();
        foreach (var light in scene.Lights)
        {
            Matrix4x4 lm = sceneMat * BuildByIndex(scene, light.transformationIndex);
            Vector3 pos = lm.MultiplyPoint3x4(Vector3.zero);
            lightPoints.Add((pos, light.rgb * intensityScale));
        }

        // --- Build BVH ---
        Debug.Log("[RT] Building BVH...");
        IHittable bvhRoot = BuildBVH(scene, sceneMat);
        Debug.Log("[RT] BVH Built.");

        // --- Projection Plane Calculations ---
        // If overriding, we just use the params. Original logic coupled distance to transformation.
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
                    
                    // Trace the ray and get the color - Recursive depth from settings
                    // Pass settings to trace for toggles
                    Color c = TraceRay(scene, lightPoints, bvhRoot, ray, backgroundColor, settings.MaxDepth, settings, dbg);
                    
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

    Color TraceRay(ObjectData scene, List<(Vector3 posWS, Color rgb)> lights, IHittable bvhRoot, Ray rayWS, Color backgroundColor, int depth, RenderSettings settings, bool debug = false)
    {
        if (bvhRoot.Hit(rayWS, 1e-6f, float.MaxValue, out HitRecord rec))
        {
            if (debug)
            {
                Debug.Log($"[RT] Hit: t={rec.t} pos=({rec.positionWS.x:F10}, {rec.positionWS.y:F10}, {rec.positionWS.z:F10}) n={rec.normalWS} mat={rec.materialIndex}");
            }
            return Shade(scene, lights, rec, rayWS, bvhRoot, depth, settings);
        }

        if (debug)
        {
            Debug.Log("[RT] No hit, returning background");
        }
        return backgroundColor;
    }

    Color Shade(ObjectData scene, List<(Vector3 posWS, Color rgb)> lights, HitRecord hit, Ray rayWS, IHittable bvhRoot, int depth, RenderSettings settings)
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

            // Accumulate based on enabled toggles
            if (settings.EnableAmbient) totalColor += ambient;
            if (settings.EnableDiffuse) totalColor += diffuse;
        }

        // Average the result of direct lighting (ambient + diffuse)
        if (lights.Count > 0)
        {
            totalColor /= (float)lights.Count;
        }

        // --- Recursive Specular Reflection (Stage 6) ---
        // Checked Toggle: EnableSpecular
        if (settings.EnableSpecular && depth > 0 && scene.Materials[hit.materialIndex].specular > 0)
        {
            var mat = scene.Materials[hit.materialIndex];
            float kSpecular = mat.specular;

            // Calculate cosine of the angle of incidence
            // V is ray direction (incoming). N is normal.
            // cosThetaV = -(V . N)
            float cosThetaV = -Vector3.Dot(rayWS.direction, N);

            if (cosThetaV > 0) // Ensure we are hitting the front face
            {
                // Calculate Reflection Vector R
                // R = V + 2 * cos(theta) * N
                Vector3 R = rayWS.direction + (2.0f * cosThetaV * N);
                R.Normalize();

                // Create Reflected Ray
                // Offset origin by epsilon along normal to avoid self-intersection (acne)
                Ray reflectedRay = new Ray(hit.positionWS + (1e-3f * N), R);

                // Recursive call
                Color reflectedColor = TraceRay(scene, lights, bvhRoot, reflectedRay, scene.Image != null ? scene.Image.background : Color.black, depth - 1, settings);

                // Add to total color: material.color * kSpecular * reflectedColor
                // Using the simpler model requested first: material.color * material.specularCoefficient * traceRay(...)
                Color specComponent = Mul(matColor, reflectedColor) * kSpecular;
                
                totalColor += specComponent;
            }
        }

        // --- Recursive Refraction (Stage 7) ---
        // Checked Toggle: EnableRefraction
        if (settings.EnableRefraction && depth > 0 && scene.Materials[hit.materialIndex].refraction > 0)
        {
            var mat = scene.Materials[hit.materialIndex];
            float kRefraction = mat.refraction;
            float ior = mat.ior;

            // Calculate cosine of incident angle
            // cosThetaV = -(V . N)
            // If > 0: Entering material (Ray opposes Normal)
            // If < 0: Exiting material (Ray aligns with Normal)
            float cosThetaV = -Vector3.Dot(rayWS.direction, N);
            
            float eta;

            // Assume Entering first (Air -> Object)
            // Air IOR ~ 1.0
            eta = 1.0f / ior;
            
            // Check direction to adjust eta and normal logic if Exiting
            if (cosThetaV < 0)
            {
                // Exiting: Object -> Air
                eta = ior; // (ior / 1.0)
                // Note: The prompt logic suggests handling `cosThetaR` sign flip below, 
                // effectively treating N as if it points into the volume, or solving math relative to same Normal.
            }

            // Calculate cos^2(ThetaR) using Snell's law identity:
            // 1 - eta^2 * (1 - cos^2(ThetaV))
            // Note: cosThetaV in calculation must be standard positive cosine for the trig identity? 
            // The prompt formula uses `costThetaV` directly, which might be negative if exiting.
            // Let's verify the prompt's specifics:
            // "cosThetaR = sqrt(1.0 - eta*eta * (1.0 - cosThetaV*cosThetaV))" -> squaring kills the sign, so it works.
            
            float discriminant = 1.0f - (eta * eta) * (1.0f - cosThetaV * cosThetaV);

            // Check for Total Internal Reflection (TIR)
            if (discriminant >= 0.0f)
            {
                float cosThetaR = Mathf.Sqrt(discriminant);

                // Adjust for Exiting Case per Prompt
                if (cosThetaV < 0)
                {
                    cosThetaR = -cosThetaR;
                }

                // Calculate Refracted Vector R
                // R = eta * V + (eta * cosThetaV - cosThetaR) * N
                Vector3 R_refract = (eta * rayWS.direction) + ((eta * cosThetaV - cosThetaR) * N);
                R_refract.Normalize();

                // Create Refracted Ray
                // Offset origin by epsilon along the refracted ray direction to avoid acne
                // (Using prompt alternative: hit.point + epsilon * r)
                Ray refractedRay = new Ray(hit.positionWS + (1e-3f * R_refract), R_refract);

                // Recursive call
                Color refractedColor = TraceRay(scene, lights, bvhRoot, refractedRay, scene.Image != null ? scene.Image.background : Color.black, depth - 1, settings);

                // Add to total color: material.color * kRefraction * refractedColor
                Color refractComponent = Mul(matColor, refractedColor) * kRefraction;
                
                totalColor += refractComponent;
            }
            // else: Total Internal Reflection occurs, no refracted ray is spawned.
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
