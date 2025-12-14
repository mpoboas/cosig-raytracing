using UnityEngine;

/// <summary>
/// Encapsulates all render settings passed from the UI to the ray tracer.
/// Nullable fields indicate optional overrides; when null, scene file defaults are used.
/// </summary>
public struct RenderSettings
{
    // ===== Output Settings =====
    
    /// <summary>Custom output resolution. If null, uses scene file resolution.</summary>
    public Vector2Int? ResolutionOverride;
    
    /// <summary>Custom background color. If null, uses scene file background.</summary>
    public Color? BackgroundColorOverride;
    
    /// <summary>Light intensity multiplier (default: 1.0).</summary>
    public float LightIntensityScale;
    
    // ===== Camera Overrides =====
    
    /// <summary>Custom camera position. If null, uses scene file camera transform.</summary>
    public Vector3? CameraPositionOverride;
    
    /// <summary>Custom camera rotation (Euler angles). If null, uses scene file camera transform.</summary>
    public Vector3? CameraRotationOverride;
    
    /// <summary>Custom field of view in degrees. If null, uses scene file FOV.</summary>
    public float? CameraFovOverride;

    // ===== Renderer Settings =====
    
    /// <summary>Maximum ray recursion depth for reflections/refractions.</summary>
    public int MaxDepth;
    
    // ===== Lighting Component Toggles =====
    
    /// <summary>Enable ambient lighting contribution.</summary>
    public bool EnableAmbient;
    
    /// <summary>Enable diffuse (Lambertian) lighting.</summary>
    public bool EnableDiffuse;
    
    /// <summary>Enable specular reflections (mirror-like surfaces).</summary>
    public bool EnableSpecular;
    
    /// <summary>Enable refraction (transparency/glass effects).</summary>
    public bool EnableRefraction;
    
    // ===== Projection Mode =====
    
    /// <summary>If true, use orthographic projection. If false, use perspective.</summary>
    public bool IsOrthographic;

    // ===== Quality Settings =====
    
    /// <summary>Number of Anti-Aliasing samples per pixel (1=off, 2=2x, 4=4x, etc).</summary>
    public int AASamples;
}
