using System;
using UnityEngine;

/// <summary>
/// Serializable scene preset that stores all render settings and file paths.
/// Used to save/load complete scene configurations as JSON files.
/// </summary>
[Serializable]
public class ScenePreset
{
    // ===== File Paths =====
    
    /// <summary>Path to the scene data file (.txt)</summary>
    public string SceneFilePath;
    
    /// <summary>Path to the loaded 3D reference image (optional)</summary>
    public string ReferenceImagePath;
    
    // ===== Output Settings =====
    
    /// <summary>Output resolution X (width in pixels)</summary>
    public int ResolutionX = 256;
    
    /// <summary>Output resolution Y (height in pixels)</summary>
    public int ResolutionY = 256;
    
    /// <summary>Background color RGB (0-1 range)</summary>
    public float[] BackgroundColor = { 0.2f, 0.2f, 0.2f };
    
    /// <summary>Light intensity multiplier</summary>
    public float LightIntensity = 1.0f;
    
    // ===== Camera Settings =====
    
    /// <summary>Camera position XYZ</summary>
    public float[] CameraPosition = { 0, 0, 0 };
    
    /// <summary>Camera rotation XYZ (Euler angles in degrees)</summary>
    public float[] CameraRotation = { 0, 0, 0 };
    
    /// <summary>Camera field of view in degrees</summary>
    public float CameraFov = 50f;
    
    /// <summary>Use orthographic projection</summary>
    public bool IsOrthographic = false;
    
    // ===== Renderer Settings =====
    
    /// <summary>Maximum ray recursion depth</summary>
    public int RecursionDepth = 2;
    
    // ===== Lighting Toggles =====
    
    /// <summary>Enable ambient lighting</summary>
    public bool EnableAmbient = true;
    
    /// <summary>Enable diffuse lighting</summary>
    public bool EnableDiffuse = true;
    
    /// <summary>Enable specular reflections</summary>
    public bool EnableSpecular = true;
    
    /// <summary>Enable refraction</summary>
    public bool EnableRefraction = true;
    
    // ===== Metadata =====
    
    /// <summary>Preset name/description</summary>
    public string PresetName = "Untitled";
    
    /// <summary>Date/time when preset was saved</summary>
    public string SavedAt;
    
    /// <summary>
    /// Creates a ScenePreset from current UI values.
    /// </summary>
    public static ScenePreset FromRenderSettings(RenderSettings settings, string sceneFilePath, string refImagePath = null)
    {
        var preset = new ScenePreset
        {
            SceneFilePath = sceneFilePath,
            ReferenceImagePath = refImagePath,
            SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        
        if (settings.ResolutionOverride.HasValue)
        {
            preset.ResolutionX = settings.ResolutionOverride.Value.x;
            preset.ResolutionY = settings.ResolutionOverride.Value.y;
        }
        
        if (settings.BackgroundColorOverride.HasValue)
        {
            Color bg = settings.BackgroundColorOverride.Value;
            preset.BackgroundColor = new float[] { bg.r, bg.g, bg.b };
        }
        
        preset.LightIntensity = settings.LightIntensityScale;
        
        if (settings.CameraPositionOverride.HasValue)
        {
            Vector3 pos = settings.CameraPositionOverride.Value;
            preset.CameraPosition = new float[] { pos.x, pos.y, pos.z };
        }
        
        if (settings.CameraRotationOverride.HasValue)
        {
            Vector3 rot = settings.CameraRotationOverride.Value;
            preset.CameraRotation = new float[] { rot.x, rot.y, rot.z };
        }
        
        if (settings.CameraFovOverride.HasValue)
        {
            preset.CameraFov = settings.CameraFovOverride.Value;
        }
        
        preset.IsOrthographic = settings.IsOrthographic;
        preset.RecursionDepth = settings.MaxDepth;
        preset.EnableAmbient = settings.EnableAmbient;
        preset.EnableDiffuse = settings.EnableDiffuse;
        preset.EnableSpecular = settings.EnableSpecular;
        preset.EnableRefraction = settings.EnableRefraction;
        
        return preset;
    }
}
