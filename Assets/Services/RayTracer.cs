using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// GPU-accelerated ray tracer using a static BVH in Object Space.
/// 
/// Architecture Overview:
/// - BVH is built once from scene geometry and cached until geometry changes
/// - Camera movement does NOT trigger BVH rebuild (major performance optimization)
/// - Rays are generated in Camera Space and transformed to Object Space via inverse camera matrix
/// - This is mathematically equivalent to transforming geometry, but avoids expensive BVH reconstruction
/// </summary>
public class RayTracer
{
    private ComputeShader computeShader;
    private ComputeBuffer bvhBuffer;
    private ComputeBuffer triangleBuffer;
    private ComputeBuffer materialBuffer;
    private RenderTexture targetTexture;
    
    // BVH caching: avoids rebuilding when only camera changes
    private ObjectData cachedScene = null;
    private bool bvhNeedsRebuild = true;

    public void SetComputeShader(ComputeShader shader)
    {
        this.computeShader = shader;
    }

    /// <summary>
    /// Invalidates the BVH cache, forcing a rebuild on the next render.
    /// Call this when scene geometry changes (objects added/removed/moved).
    /// </summary>
    public void InvalidateBVHCache()
    {
        bvhNeedsRebuild = true;
        cachedScene = null;
    }

    /// <summary>
    /// Releases all GPU buffers. Call when destroying the ray tracer or switching scenes.
    /// </summary>
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
        cachedScene = null;
        bvhNeedsRebuild = true;
    }

    /// <summary>
    /// Clears the render target without releasing buffers.
    /// Call when switching between render modes to ensure fresh state.
    /// </summary>
    public void ClearRenderTarget()
    {
        if (targetTexture != null)
        {
            targetTexture.Release();
            targetTexture = null;
        }
    }

    /// <summary>
    /// Synchronous render method for real-time rendering.
    /// Returns the GPU RenderTexture directly without CPU readback (much faster).
    /// The returned texture is reused between frames - do not destroy it.
    /// </summary>
    /// <param name="scene">Scene data containing geometry, materials, lights, and camera</param>
    /// <param name="settings">Render settings from UI (resolution, toggles, camera overrides)</param>
    /// <returns>RenderTexture with the rendered image (GPU-side, reused between calls)</returns>
    public RenderTexture RenderToTexture(ObjectData scene, RenderSettings settings)
    {
        if (computeShader == null)
        {
            Debug.LogError("ComputeShader not assigned to RayTracer!");
            return null;
        }

        // Determine output resolution
        int width = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.x : Mathf.Max(1, scene.Image != null ? scene.Image.horizontal : 256);
        int height = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.y : Mathf.Max(1, scene.Image != null ? scene.Image.vertical : 256);

        // Build camera transformation matrix
        Matrix4x4 M_scene = Matrix4x4.identity;
        if (scene.Camera != null && scene.Camera.transformationIndex >= 0 && scene.Camera.transformationIndex < scene.Transformations.Count)
        {
            M_scene = BuildComposite(scene.Transformations[scene.Camera.transformationIndex]);
        }

        // Compute the ray transformation matrix (Camera Space -> Object Space)
        Matrix4x4 cameraToObject;
        bool usingOverrides = settings.CameraPositionOverride.HasValue || settings.CameraRotationOverride.HasValue;
        
        if (usingOverrides)
        {
            Vector3 pos = settings.CameraPositionOverride ?? Vector3.zero;
            Vector3 rot = settings.CameraRotationOverride ?? Vector3.zero;
            Matrix4x4 cameraTransform = Matrix4x4.TRS(pos, Quaternion.Euler(rot), Vector3.one);
            cameraToObject = cameraTransform.inverse;
        }
        else
        {
            cameraToObject = M_scene.inverse;
        }

        // Rebuild BVH only when geometry changes
        if (bvhNeedsRebuild || cachedScene != scene)
        {
            RebuildBVH(scene);
            cachedScene = scene;
            bvhNeedsRebuild = false;
        }

        // Create or resize render target if needed
        if (targetTexture == null || targetTexture.width != width || targetTexture.height != height)
        {
            if (targetTexture != null) targetTexture.Release();
            targetTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            targetTexture.enableRandomWrite = true;
            targetTexture.Create();
        }

        // Bind shader resources
        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetTexture(kernel, "Result", targetTexture);
        computeShader.SetBuffer(kernel, "BVHNodes", bvhBuffer);
        computeShader.SetBuffer(kernel, "Triangles", triangleBuffer);
        SetupMaterialBuffer(scene, kernel);
        
        // Configure rendering parameters
        computeShader.SetInt("_MaxDepth", settings.MaxDepth);
        computeShader.SetInt("_DebugMode", 0);
        computeShader.SetInt("_EnableAmbient", settings.EnableAmbient ? 1 : 0);
        computeShader.SetInt("_EnableDiffuse", settings.EnableDiffuse ? 1 : 0);
        computeShader.SetInt("_EnableSpecular", settings.EnableSpecular ? 1 : 0);
        computeShader.SetInt("_EnableRefraction", settings.EnableRefraction ? 1 : 0);
        computeShader.SetFloat("_LightIntensity", settings.LightIntensityScale);
        
        // Background color
        Color bgColor = settings.BackgroundColorOverride ?? (scene.Image != null ? scene.Image.background : new Color(0.2f, 0.2f, 0.2f));
        computeShader.SetVector("_BackgroundColor", new Vector4(bgColor.r, bgColor.g, bgColor.b, 1));
        
        // Light position in Object Space
        Vector3 lightPos = Vector3.zero;
        if (scene.Lights != null && scene.Lights.Count > 0)
        {
            var light = scene.Lights[0];
            if (light.transformationIndex >= 0 && light.transformationIndex < scene.Transformations.Count)
            {
                Matrix4x4 lightMat = BuildComposite(scene.Transformations[light.transformationIndex]);
                lightPos = lightMat.GetColumn(3);
            }
        }
        computeShader.SetVector("_LightPosition", lightPos);

        // Camera projection parameters
        float fov = settings.CameraFovOverride.HasValue ? settings.CameraFovOverride.Value : (scene.Camera != null ? scene.Camera.verticalFovDeg : 50f);
        float cameraDistance = scene.Camera != null ? scene.Camera.distance : 30f;
        float aspect = (float)width / height;
        Matrix4x4 projection = Matrix4x4.Perspective(fov, aspect, 0.1f, 1000f);
        Matrix4x4 invProjection = projection.inverse;
        
        // Orthographic projection settings
        computeShader.SetInt("_IsOrthographic", settings.IsOrthographic ? 1 : 0);
        float orthoSize = cameraDistance * Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f);
        computeShader.SetFloat("_OrthoSize", orthoSize);

        // Pass ray transformation matrix to shader
        computeShader.SetMatrix("_CameraToWorld", cameraToObject);
        computeShader.SetMatrix("_CameraInverseProjection", invProjection);
        computeShader.SetFloat("_CameraDistance", cameraDistance);
        computeShader.SetFloat("_CameraFOV", fov);
        
        // Dispatch compute shader (8x8 thread groups)
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

        return targetTexture;
    }

    /// <summary>
    /// Renders the scene asynchronously using GPU compute shaders.
    /// </summary>
    /// <param name="scene">Scene data containing geometry, materials, lights, and camera</param>
    /// <param name="settings">Render settings from UI (resolution, toggles, camera overrides)</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0)</param>
    /// <param name="token">Cancellation token for aborting the render</param>
    /// <returns>Rendered image as Texture2D, or null if cancelled/failed</returns>
    public async Task<Texture2D> RenderAsync(ObjectData scene, RenderSettings settings, IProgress<float> progress, CancellationToken token)
    {
        if (computeShader == null)
        {
            Debug.LogError("ComputeShader not assigned to RayTracer!");
            return null;
        }

        // Determine output resolution (UI override takes precedence over scene file)
        int width = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.x : Mathf.Max(1, scene.Image != null ? scene.Image.horizontal : 256);
        int height = settings.ResolutionOverride.HasValue ? settings.ResolutionOverride.Value.y : Mathf.Max(1, scene.Image != null ? scene.Image.vertical : 256);

        // =============================================================================
        // CAMERA TRANSFORMATION STRATEGY
        // 
        // Scene file semantics (per specification):
        //   - Camera is FIXED at (0, 0, distance) in Camera Space, looking toward -Z
        //   - M_scene (camera transformation) would normally be applied to GEOMETRY
        // 
        // Our optimization (mathematically equivalent):
        //   - Keep geometry in Object Space -> BVH stays static
        //   - Transform RAYS by M_scene^(-1) instead of transforming geometry
        //   - Result: BVH never needs rebuilding when camera moves
        // =============================================================================
        
        // Build M_scene from scene file's camera transformation
        Matrix4x4 M_scene = Matrix4x4.identity;
        
        if (scene.Camera != null && scene.Camera.transformationIndex >= 0 && scene.Camera.transformationIndex < scene.Transformations.Count)
        {
            M_scene = BuildComposite(scene.Transformations[scene.Camera.transformationIndex]);
        }

        // Compute the ray transformation matrix (Camera Space -> Object Space)
        // This "undoes" what M_scene would have done to geometry
        Matrix4x4 cameraToObject;
        
        bool usingOverrides = settings.CameraPositionOverride.HasValue || settings.CameraRotationOverride.HasValue;
        
        if (usingOverrides)
        {
            // UI camera overrides: user directly specifies camera position/rotation
            // Build a view matrix from these parameters
            Vector3 pos = settings.CameraPositionOverride ?? Vector3.zero;
            Vector3 rot = settings.CameraRotationOverride ?? Vector3.zero;
            
            // TRS gives Camera->World; we need the inverse for ray transformation
            Matrix4x4 cameraTransform = Matrix4x4.TRS(pos, Quaternion.Euler(rot), Vector3.one);
            cameraToObject = cameraTransform.inverse;
        }
        else
        {
            // Scene file camera: M_scene transforms Object->Camera
            // We need M_scene^(-1) to transform rays from Camera->Object
            cameraToObject = M_scene.inverse;
        }

        // Rebuild BVH only when geometry changes (not on camera movement)
        progress?.Report(0.05f);
        await Task.Yield();
        
        if (bvhNeedsRebuild || cachedScene != scene)
        {
            RebuildBVH(scene);
            cachedScene = scene;
            bvhNeedsRebuild = false;
        }

        progress?.Report(0.5f);
        await Task.Yield();
        
        if (token.IsCancellationRequested) return null;

        // Create or resize render target if needed
        if (targetTexture == null || targetTexture.width != width || targetTexture.height != height)
        {
            if (targetTexture != null) targetTexture.Release();
            targetTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            targetTexture.enableRandomWrite = true;
            targetTexture.Create();
        }

        // Bind shader resources
        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetTexture(kernel, "Result", targetTexture);
        computeShader.SetBuffer(kernel, "BVHNodes", bvhBuffer);
        computeShader.SetBuffer(kernel, "Triangles", triangleBuffer);
        SetupMaterialBuffer(scene, kernel);
        
        // Configure rendering parameters
        computeShader.SetInt("_MaxDepth", settings.MaxDepth);
        computeShader.SetInt("_DebugMode", 0); // 0=normal, 3=hit visualization
        computeShader.SetInt("_EnableAmbient", settings.EnableAmbient ? 1 : 0);
        computeShader.SetInt("_EnableDiffuse", settings.EnableDiffuse ? 1 : 0);
        computeShader.SetInt("_EnableSpecular", settings.EnableSpecular ? 1 : 0);
        computeShader.SetInt("_EnableRefraction", settings.EnableRefraction ? 1 : 0);
        computeShader.SetFloat("_LightIntensity", settings.LightIntensityScale);
        
        // Background color (UI override or scene default)
        Color bgColor = settings.BackgroundColorOverride ?? (scene.Image != null ? scene.Image.background : new Color(0.2f, 0.2f, 0.2f));
        computeShader.SetVector("_BackgroundColor", new Vector4(bgColor.r, bgColor.g, bgColor.b, 1));
        
        // Light position in Object Space (extracted from light's transformation)
        Vector3 lightPos = Vector3.zero;
        if (scene.Lights != null && scene.Lights.Count > 0)
        {
            var light = scene.Lights[0];
            if (light.transformationIndex >= 0 && light.transformationIndex < scene.Transformations.Count)
            {
                Matrix4x4 lightMat = BuildComposite(scene.Transformations[light.transformationIndex]);
                lightPos = lightMat.GetColumn(3); // Extract translation component
            }
        }
        computeShader.SetVector("_LightPosition", lightPos);

        // Camera projection parameters
        float fov = settings.CameraFovOverride.HasValue ? settings.CameraFovOverride.Value : (scene.Camera != null ? scene.Camera.verticalFovDeg : 50f);
        float cameraDistance = scene.Camera != null ? scene.Camera.distance : 30f;
        float aspect = (float)width / height;
        Matrix4x4 projection = Matrix4x4.Perspective(fov, aspect, 0.1f, 1000f);
        Matrix4x4 invProjection = projection.inverse;
        
        // Orthographic projection settings (size matches perspective frustum at camera distance)
        computeShader.SetInt("_IsOrthographic", settings.IsOrthographic ? 1 : 0);
        float orthoSize = cameraDistance * Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f);
        computeShader.SetFloat("_OrthoSize", orthoSize);

        // Pass ray transformation matrix to shader
        // Shader uses this to transform rays from Camera Space to Object Space
        computeShader.SetMatrix("_CameraToWorld", cameraToObject);
        computeShader.SetMatrix("_CameraInverseProjection", invProjection);
        computeShader.SetFloat("_CameraDistance", cameraDistance);
        computeShader.SetFloat("_CameraFOV", fov);
        
        // Dispatch compute shader (8x8 thread groups)
        int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
        
        progress?.Report(0.8f);
        await Task.Yield();
        
        computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

        progress?.Report(0.9f);
        await Task.Yield();

        // Read back results from GPU to CPU texture
        // Note: ReadPixels is synchronous; for large images consider AsyncGPUReadback
        Texture2D resultTex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        RenderTexture.active = targetTexture;
        resultTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        resultTex.Apply();
        RenderTexture.active = null;
        
        progress?.Report(1.0f);
        
        return resultTex;
    }

    /// <summary>
    /// Rebuilds the BVH acceleration structure from scene geometry.
    /// Geometry is extracted in Object Space (no camera transform applied).
    /// </summary>
    private void RebuildBVH(ObjectData scene)
    {
        // Extract triangles in Object Space (object transforms only, no camera)
        var gpuTriangles = SceneGeometryConverter.ExtractTriangles(scene);
        
        // Build BVH using median-split algorithm
        BVHBuilder builder = new BVHBuilder();
        var bvh = builder.Build(gpuTriangles);
        
        // Upload BVH nodes to GPU (32 bytes per node: 2x Vector3 + 2x int)
        if (bvhBuffer != null) bvhBuffer.Release();
        bvhBuffer = new ComputeBuffer(bvh.nodes.Length, 32); 
        bvhBuffer.SetData(bvh.nodes);
        
        // Upload triangles to GPU (88 bytes per triangle: 7x Vector3 + 1x int)
        if (triangleBuffer != null) triangleBuffer.Release();
        triangleBuffer = new ComputeBuffer(bvh.triangles.Length, 88); 
        triangleBuffer.SetData(bvh.triangles);
    }

    /// <summary>
    /// Builds a 4x4 transformation matrix from a sequence of transform elements.
    /// Applies transforms in order: T (translate), S (scale), Rx/Ry/Rz (rotate).
    /// </summary>
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
            M = M * transform; // Concatenate transforms left-to-right
        }
        return M;
    }

    /// <summary>
    /// GPU-compatible material structure (32 bytes, cache-aligned).
    /// </summary>
    private struct GPUMaterial
    {
        public Vector3 color;      // Base color (RGB)
        public float ambient;      // Ambient reflection coefficient
        public float diffuse;      // Diffuse reflection coefficient (Lambertian)
        public float specular;     // Specular reflection coefficient (mirror-like)
        public float refraction;   // Refraction coefficient (transparency)
        public float ior;          // Index of refraction (1.0 = air, 1.5 = glass)
    }
    
    /// <summary>
    /// Uploads material data to the GPU. Creates a default white material if none exist.
    /// </summary>
    private void SetupMaterialBuffer(ObjectData scene, int kernel)
    {
        if (scene.Materials == null || scene.Materials.Count == 0)
        {
            // Fallback: create a default diffuse white material
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
            materialBuffer = new ComputeBuffer(1, 32);
            materialBuffer.SetData(defaultMats);
        }
        else
        {
            // Convert scene materials to GPU format
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

    /// <summary>
    /// Saves a texture to disk as PNG. Creates the directory if it doesn't exist.
    /// </summary>
    public static void SaveTexture(Texture2D tex, string path)
    {
        byte[] png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);
    }
}
