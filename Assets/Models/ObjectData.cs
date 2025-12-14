using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root data structure containing all scene elements.
/// Mirrors the scene description file format with typed collections for each element type.
/// </summary>
[System.Serializable]
public class ObjectData
{
    /// <summary>Output image settings (resolution and background color).</summary>
    public ImageSettings Image;

    /// <summary>Composite transformations, referenced by index from other elements.</summary>
    public List<CompositeTransformation> Transformations = new List<CompositeTransformation>();

    /// <summary>Camera configuration (position, distance, FOV).</summary>
    public CameraSettings Camera;

    /// <summary>Point lights in the scene.</summary>
    public List<LightSource> Lights = new List<LightSource>();

    /// <summary>Materials defining surface appearance, referenced by index.</summary>
    public List<MaterialDescription> Materials = new List<MaterialDescription>();

    /// <summary>Triangle meshes (groups of triangles sharing a transformation).</summary>
    public List<TrianglesMesh> TriangleMeshes = new List<TrianglesMesh>();

    /// <summary>Sphere primitives (unit spheres scaled by their transformation).</summary>
    public List<SphereDescription> Spheres = new List<SphereDescription>();

    /// <summary>Box primitives (unit cubes scaled by their transformation).</summary>
    public List<BoxDescription> Boxes = new List<BoxDescription>();
}

/// <summary>
/// Output image configuration.
/// </summary>
[System.Serializable]
public class ImageSettings
{
    /// <summary>Output width in pixels.</summary>
    public int horizontal = 0;
    
    /// <summary>Output height in pixels.</summary>
    public int vertical = 0;

    /// <summary>Background color for rays that miss all geometry.</summary>
    public Color background = Color.black;
}

/// <summary>
/// A composite transformation is an ordered sequence of elementary operations.
/// Transforms are applied in order (left-to-right matrix multiplication).
/// </summary>
[System.Serializable]
public class CompositeTransformation
{
    /// <summary>Ordered list of elementary transforms (T, S, Rx, Ry, Rz).</summary>
    public List<TransformElement> Elements = new List<TransformElement>();
}

/// <summary>
/// Types of elementary transformations supported by the scene format.
/// </summary>
public enum TransformType
{
    T,   // Translation (x, y, z)
    Rx,  // Rotation about X-axis (degrees)
    Ry,  // Rotation about Y-axis (degrees)
    Rz,  // Rotation about Z-axis (degrees)
    S    // Scale (x, y, z)
}

/// <summary>
/// A single elementary transformation.
/// Uses XYZ for translations and scales, AngleDeg for rotations.
/// </summary>
[System.Serializable]
public struct TransformElement
{
    /// <summary>Type of transformation (T, S, Rx, Ry, Rz).</summary>
    public TransformType Type;

    /// <summary>Vector parameters for T (translation) and S (scale).</summary>
    public Vector3 XYZ;
    
    /// <summary>Angle in degrees for rotations (Rx, Ry, Rz).</summary>
    public float AngleDeg;

    /// <summary>Creates a translation transform.</summary>
    public static TransformElement Translation(Vector3 t)
    {
        return new TransformElement { Type = TransformType.T, XYZ = t, AngleDeg = 0f };
    }

    /// <summary>Creates a scale transform.</summary>
    public static TransformElement Scale(Vector3 s)
    {
        return new TransformElement { Type = TransformType.S, XYZ = s, AngleDeg = 0f };
    }

    /// <summary>Creates a rotation about the X-axis.</summary>
    public static TransformElement RotationX(float angleDeg)
    {
        return new TransformElement { Type = TransformType.Rx, XYZ = Vector3.zero, AngleDeg = angleDeg };
    }

    /// <summary>Creates a rotation about the Y-axis.</summary>
    public static TransformElement RotationY(float angleDeg)
    {
        return new TransformElement { Type = TransformType.Ry, XYZ = Vector3.zero, AngleDeg = angleDeg };
    }

    /// <summary>Creates a rotation about the Z-axis.</summary>
    public static TransformElement RotationZ(float angleDeg)
    {
        return new TransformElement { Type = TransformType.Rz, XYZ = Vector3.zero, AngleDeg = angleDeg };
    }
}

/// <summary>
/// Camera configuration following the scene file semantics.
/// The camera is positioned at (0, 0, distance) in its local space,
/// looking toward -Z. The transformation moves the scene, not the camera.
/// </summary>
[System.Serializable]
public class CameraSettings
{
    /// <summary>Index of the transformation to apply to the scene (camera's view matrix).</summary>
    public int transformationIndex = 0;

    /// <summary>Distance from camera to the projection plane (affects FOV).</summary>
    public float distance = 1.0f;

    /// <summary>Vertical field of view in degrees.</summary>
    public float verticalFovDeg = 60.0f;
}

/// <summary>
/// Point light source in the scene.
/// </summary>
[System.Serializable]
public class LightSource
{
    /// <summary>Transformation index defining the light's position.</summary>
    public int transformationIndex = 0;

    /// <summary>Light color and intensity (can exceed 1.0 for HDR).</summary>
    public Color rgb = Color.white;
}

/// <summary>
/// Material properties for surface shading.
/// Uses a simple Phong-style lighting model with optional refraction.
/// </summary>
[System.Serializable]
public class MaterialDescription
{
    /// <summary>Base surface color (RGB, 0-1 range).</summary>
    public Color color = Color.white;

    /// <summary>Ambient light contribution (0-1).</summary>
    public float ambient = 0.0f;
    
    /// <summary>Diffuse (Lambertian) reflection coefficient (0-1).</summary>
    public float diffuse = 0.0f;
    
    /// <summary>Specular (mirror-like) reflection coefficient (0-1).</summary>
    public float specular = 0.0f;
    
    /// <summary>Refraction/transparency coefficient (0-1).</summary>
    public float refraction = 0.0f;

    /// <summary>Index of refraction (1.0=air, 1.33=water, 1.5=glass).</summary>
    public float ior = 1.0f;
}

/// <summary>
/// A mesh of triangles sharing a common transformation.
/// </summary>
[System.Serializable]
public class TrianglesMesh
{
    /// <summary>Transformation applied to all triangles in this mesh.</summary>
    public int transformationIndex = 0;

    /// <summary>List of triangles in this mesh.</summary>
    public List<Triangle> Triangles = new List<Triangle>();
}

/// <summary>
/// A single triangle with three vertices and a material reference.
/// </summary>
[System.Serializable]
public struct Triangle
{
    /// <summary>Index of the material for this triangle.</summary>
    public int materialIndex;

    /// <summary>First vertex position.</summary>
    public Vector3 v0;
    
    /// <summary>Second vertex position.</summary>
    public Vector3 v1;
    
    /// <summary>Third vertex position.</summary>
    public Vector3 v2;

    public Triangle(int material, Vector3 a, Vector3 b, Vector3 c)
    {
        materialIndex = material;
        v0 = a; v1 = b; v2 = c;
    }
}

/// <summary>
/// Sphere primitive (unit sphere at origin, sized/positioned by transformation).
/// </summary>
[System.Serializable]
public class SphereDescription
{
    /// <summary>Transformation index (defines position, rotation, scale).</summary>
    public int transformationIndex = 0;
    
    /// <summary>Material index for surface appearance.</summary>
    public int materialIndex = 0;
}

/// <summary>
/// Box primitive (unit cube at origin, sized/positioned by transformation).
/// </summary>
[System.Serializable]
public class BoxDescription
{
    /// <summary>Transformation index (defines position, rotation, scale).</summary>
    public int transformationIndex = 0;
    
    /// <summary>Material index for surface appearance.</summary>
    public int materialIndex = 0;
}
