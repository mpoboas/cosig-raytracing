using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class RayTracer
{
    // We use UnityEngine.Ray and the HitRecord defined in IHittable.cs

    private ComputeShader computeShader;
    private ComputeBuffer bvhBuffer;
    private ComputeBuffer triangleBuffer;
    private ComputeBuffer materialBuffer;
    private RenderTexture targetTexture;

    public void SetComputeShader(ComputeShader shader)
    {
        this.computeShader = shader;
    }

    public void ReleaseBuffers()
    {
        if (bvhBuffer != null) bvhBuffer.Release();
        if (triangleBuffer != null) triangleBuffer.Release();
        if (materialBuffer != null) materialBuffer.Release();
        if (targetTexture != null) targetTexture.Release();
        bvhBuffer = null;
        triangleBuffer = null;
        materialBuffer = null;
        targetTexture = null;
    }

    public async Task<Texture2D> RenderAsync(ObjectData scene, RenderSettings settings, IProgress<float> progress, CancellationToken token)
    {
        if (computeShader == null)
        {
            Debug.LogError("ComputeShader not assigned to RayTracer!");
            return null;
        }

        // Apply Resolution Override
        int width = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.x : Mathf.Max(1, scene.Image != null ? scene.Image.horizontal : 256);
        int height = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.y : Mathf.Max(1, scene.Image != null ? scene.Image.vertical : 256);

        // Calculate sceneMat WITH overrides (matching CPU implementation)
        Matrix4x4 cameraToWorld = Matrix4x4.identity;
        Vector3 basePos = Vector3.zero;
        
        if (scene.Camera != null && scene.Camera.transformationIndex >= 0 && scene.Camera.transformationIndex < scene.Transformations.Count)
        {
             cameraToWorld = BuildComposite(scene.Transformations[scene.Camera.transformationIndex]);
             basePos = cameraToWorld.GetColumn(3);
        }

        // Apply camera overrides
        if (settings.CameraPositionOverride.HasValue || settings.CameraRotationOverride.HasValue)
        {
             Vector3 pos = settings.CameraPositionOverride ?? basePos;
             Vector3 rot = settings.CameraRotationOverride ?? Vector3.zero;
             cameraToWorld = Matrix4x4.TRS(pos, Quaternion.Euler(rot), Vector3.one);
        }

        // Always rebuild BVH with current camera transform (geometry is in camera space)
        progress?.Report(0.05f); // Starting BVH build
        await Task.Yield(); // Allow UI to update
        
        RebuildBVH(scene, cameraToWorld);

        progress?.Report(0.5f); // BVH complete, starting GPU render
        await Task.Yield(); // Allow UI to update
        
        if (token.IsCancellationRequested) return null;

        // 2. Setup Texture
        if (targetTexture == null || targetTexture.width != width || targetTexture.height != height)
        {
            if (targetTexture != null) targetTexture.Release();
            targetTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
            targetTexture.enableRandomWrite = true;
            targetTexture.Create();
        }

        // 3. Setup Shader Parameters
        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetTexture(kernel, "Result", targetTexture);
        computeShader.SetBuffer(kernel, "BVHNodes", bvhBuffer);
        computeShader.SetBuffer(kernel, "Triangles", triangleBuffer);
        SetupMaterialBuffer(scene, kernel);
        
        // Settings
        computeShader.SetInt("_MaxDepth", settings.MaxDepth);
        computeShader.SetInt("_DebugMode", 0); // 0=normal shading, 3=hit visualization
        computeShader.SetInt("_EnableAmbient", settings.EnableAmbient ? 1 : 0);
        computeShader.SetInt("_EnableDiffuse", settings.EnableDiffuse ? 1 : 0);
        computeShader.SetInt("_EnableSpecular", settings.EnableSpecular ? 1 : 0);
        computeShader.SetInt("_EnableRefraction", settings.EnableRefraction ? 1 : 0);
        computeShader.SetFloat("_LightIntensity", settings.LightIntensityScale);
        
        // Background color from UI
        Color bgColor = settings.BackgroundColorOverride ?? (scene.Image != null ? scene.Image.background : new Color(0.2f, 0.2f, 0.2f));
        computeShader.SetVector("_BackgroundColor", new Vector4(bgColor.r, bgColor.g, bgColor.b, 1));
        
        // Light position from scene (apply sceneMat transform like CPU does)
        Vector3 lightPos = Vector3.zero;
        if (scene.Lights != null && scene.Lights.Count > 0)
        {
            var light = scene.Lights[0];
            Matrix4x4 lightMat = Matrix4x4.identity;
            if (light.transformationIndex >= 0 && light.transformationIndex < scene.Transformations.Count)
            {
                lightMat = cameraToWorld * BuildComposite(scene.Transformations[light.transformationIndex]);
            }
            lightPos = lightMat.GetColumn(3);
        }
        computeShader.SetVector("_LightPosition", lightPos);

        // Projection (must declare these before orthographic uses them)
        float fov = settings.CameraFovOverride.HasValue ? settings.CameraFovOverride.Value : (scene.Camera != null ? scene.Camera.verticalFovDeg : 50f);
        float cameraDistance = scene.Camera != null ? scene.Camera.distance : 30f;
        float aspect = (float)width / height;
        Matrix4x4 projection = Matrix4x4.Perspective(fov, aspect, 0.1f, 1000f);
        Matrix4x4 invProjection = projection.inverse;
        
        // Orthographic mode
        computeShader.SetInt("_IsOrthographic", settings.IsOrthographic ? 1 : 0);
        float orthoSize = cameraDistance * Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f); // Use same size as perspective frustum at distance
        computeShader.SetFloat("_OrthoSize", orthoSize);

        computeShader.SetMatrix("_CameraToWorld", cameraToWorld);
        computeShader.SetMatrix("_CameraInverseProjection", invProjection);
        computeShader.SetFloat("_CameraDistance", cameraDistance);
        computeShader.SetFloat("_CameraFOV", fov);
        
        Debug.Log($"[GPU RayTracer] Camera distance={cameraDistance}, FOV={fov}, Ortho={settings.IsOrthographic}");
        
        // 4. Dispatch
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        
        progress?.Report(0.8f); // Dispatching to GPU
        await Task.Yield();
        
        computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

        progress?.Report(0.9f); // Reading back results
        await Task.Yield();

        // 5. Readback
        // Using ReadPixels on Main Thread (Async method runs on main thread context in Unity unless Task.Run)
        // Since we are in an async method invoked by Unity event loop, we are on Linear context? 
        
        // Wait for GPU? Dispatch is async.
        // We can use AsyncGPUReadback or just ReadPixels.
        // For simplicity and immediate compatibility:
        
        // Use RGBA32 for proper gamma handling in editor display
        Texture2D resultTex = new Texture2D(width, height, TextureFormat.RGBA32, false, false); // sRGB
        RenderTexture.active = targetTexture;
        resultTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        resultTex.Apply();
        RenderTexture.active = null;
        
        progress?.Report(1.0f); // Complete
        
        return resultTex;
    }

    private void RebuildBVH(ObjectData scene, Matrix4x4 sceneMat)
    {
        Debug.Log("[GPU RayTracer] Rebuilding BVH...");
        
        // Extract Geometry with camera transform (passed as parameter)
        var gpuTriangles = SceneGeometryConverter.ExtractTriangles(scene, sceneMat);
        
        // Debug: Log first few triangles
        if (gpuTriangles.Count > 0)
        {
            Debug.Log($"[GPU RayTracer] First triangle: v0={gpuTriangles[0].v0}, v1={gpuTriangles[0].v1}, v2={gpuTriangles[0].v2}");
            if (gpuTriangles.Count > 1)
                Debug.Log($"[GPU RayTracer] Second triangle: v0={gpuTriangles[1].v0}");
        }
        
        // 2. Build BVH
        BVHBuilder builder = new BVHBuilder();
        var bvh = builder.Build(gpuTriangles);
        
        // Debug: Log BVH root bounds
        if (bvh.nodes.Length > 0)
        {
            Debug.Log($"[GPU RayTracer] Root BVH bounds: min={bvh.nodes[0].min}, max={bvh.nodes[0].max}");
        }
        
        // 3. Upload Buffers
        // Nodes (32 bytes stride)
        if (bvhBuffer != null) bvhBuffer.Release();
        bvhBuffer = new ComputeBuffer(bvh.nodes.Length, 32); 
        bvhBuffer.SetData(bvh.nodes);
        
        // Triangles (88 bytes stride: Vector3*7(84) + int(4) = 88)
        if (triangleBuffer != null) triangleBuffer.Release();
        triangleBuffer = new ComputeBuffer(bvh.triangles.Length, 88); 
        triangleBuffer.SetData(bvh.triangles);
        
        Debug.Log($"[GPU RayTracer] BVH Built. Nodes: {bvh.nodes.Length}, Tris: {bvh.triangles.Length}");
    }

    // Helper for Legacy Matrix calc (SceneGeometryConverter replicates this too, but we need it for Camera here)
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
            M = M * transform;
        }
        return M;
    }

    private struct GPUMaterial
    {
        public Vector3 color;
        public float ambient;
        public float diffuse;
        public float specular;
        public float refraction;
        public float ior;
    }
    
    private void SetupMaterialBuffer(ObjectData scene, int kernel)
    {
        if (scene.Materials == null || scene.Materials.Count == 0)
        {
            // Create a default material
            GPUMaterial[] defaultMats = new GPUMaterial[1];
            defaultMats[0] = new GPUMaterial
            {
                color = new Vector3(1, 1, 1),
                ambient = 0.1f,
                diffuse = 0.7f,
                specular = 0,
                refraction = 0,
                ior = 1.0f
            };
            
            if (materialBuffer != null) materialBuffer.Release();
            materialBuffer = new ComputeBuffer(1, 32); // 3*4 + 5*4 = 32 bytes
            materialBuffer.SetData(defaultMats);
        }
        else
        {
            GPUMaterial[] gpuMats = new GPUMaterial[scene.Materials.Count];
            for (int i = 0; i < scene.Materials.Count; i++)
            {
                var m = scene.Materials[i];
                gpuMats[i] = new GPUMaterial
                {
                    color = new Vector3(m.color.r, m.color.g, m.color.b),
                    ambient = m.ambient,
                    diffuse = m.diffuse,
                    specular = m.specular,
                    refraction = m.refraction,
                    ior = m.ior
                };
            }
            
            if (materialBuffer != null) materialBuffer.Release();
            materialBuffer = new ComputeBuffer(gpuMats.Length, 32);
            materialBuffer.SetData(gpuMats);
        }
        
        computeShader.SetBuffer(kernel, "Materials", materialBuffer);
    }

    public static void SaveTexture(Texture2D tex, string path)
    {
        byte[] png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);
    }
}
