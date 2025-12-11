using UnityEngine;

// Data container for render settings passed from UI to RayTracer
public struct RenderSettings
{
    public Vector2Int? ResolutionOverride;
    public Color? BackgroundColorOverride;
    public float LightIntensityScale;
    
    // Camera Overrides
    public Vector3? CameraPositionOverride;
    public Vector3? CameraRotationOverride;
    public float? CameraFovOverride;

    // Renderer Settings
    public int MaxDepth;
    
    // Toggles
    public bool EnableAmbient;
    public bool EnableDiffuse;
    public bool EnableSpecular; // Reflection
    public bool EnableRefraction;
}
