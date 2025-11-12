using System.Collections.Generic;
using UnityEngine;

// Data structures mirroring the scene description format (Image, Transformation, Camera, Light, Material, Triangles, Sphere, Box)
[System.Serializable]
public class ObjectData
{
    // One and only one Image segment
    public ImageSettings Image;

    // Zero or more composite transformations, referenced by index elsewhere
    public List<CompositeTransformation> Transformations = new List<CompositeTransformation>();

    // One and only one Camera segment
    public CameraSettings Camera;

    // Zero or more Light segments
    public List<LightSource> Lights = new List<LightSource>();

    // Zero or more Material segments, referenced by index elsewhere
    public List<MaterialDescription> Materials = new List<MaterialDescription>();

    // Zero or more triangle meshes
    public List<TrianglesMesh> TriangleMeshes = new List<TrianglesMesh>();

    // Zero or more spheres
    public List<SphereDescription> Spheres = new List<SphereDescription>();

    // Zero or more boxes
    public List<BoxDescription> Boxes = new List<BoxDescription>();
}

[System.Serializable]
public class ImageSettings
{
    // Horizontal and vertical resolution in pixels (integer > 0)
    public int horizontal = 0;
    public int vertical = 0;

    // Background color components (0.0 .. 1.0)
    public Color background = Color.black;
}

// A composite transformation is an ordered list of elementary operations
[System.Serializable]
public class CompositeTransformation
{
    public List<TransformElement> Elements = new List<TransformElement>();
}

public enum TransformType
{
    T,   // Translation
    Rx,  // Rotation about X in degrees
    Ry,  // Rotation about Y in degrees
    Rz,  // Rotation about Z in degrees
    S    // Scale
}

[System.Serializable]
public struct TransformElement
{
    public TransformType Type;

    // Parameters:
    // - For T and S: use XYZ as vector components
    // - For Rx / Ry / Rz: use AngleDeg (XYZ ignored)
    public Vector3 XYZ;
    public float AngleDeg;

    public static TransformElement Translation(Vector3 t)
    {
        return new TransformElement { Type = TransformType.T, XYZ = t, AngleDeg = 0f };
    }

    public static TransformElement Scale(Vector3 s)
    {
        return new TransformElement { Type = TransformType.S, XYZ = s, AngleDeg = 0f };
    }

    public static TransformElement RotationX(float angleDeg)
    {
        return new TransformElement { Type = TransformType.Rx, XYZ = Vector3.zero, AngleDeg = angleDeg };
    }

    public static TransformElement RotationY(float angleDeg)
    {
        return new TransformElement { Type = TransformType.Ry, XYZ = Vector3.zero, AngleDeg = angleDeg };
    }

    public static TransformElement RotationZ(float angleDeg)
    {
        return new TransformElement { Type = TransformType.Rz, XYZ = Vector3.zero, AngleDeg = angleDeg };
    }
}

[System.Serializable]
public class CameraSettings
{
    // Index of composite transformation (integer >= 0)
    public int transformationIndex = 0;

    // Distance to projection plane z=0 (double > 0.0)
    public float distance = 1.0f;

    // Vertical field of view in degrees (double > 0.0)
    public float verticalFovDeg = 60.0f;
}

[System.Serializable]
public class LightSource
{
    // Index of composite transformation (integer >= 0)
    public int transformationIndex = 0;

    // Light color/intensity (0.0 .. 1.0)
    public Color rgb = Color.white;
}

[System.Serializable]
public class MaterialDescription
{
    // Base color (0.0 .. 1.0)
    public Color color = Color.white;

    // Coefficients (0.0 .. 1.0)
    public float ambient = 0.0f;
    public float diffuse = 0.0f;
    public float specular = 0.0f;
    public float refraction = 0.0f;

    // Index of refraction (double >= 1.0)
    public float ior = 1.0f;
}

[System.Serializable]
public class TrianglesMesh
{
    // Index of composite transformation applied to all triangles
    public int transformationIndex = 0;

    public List<Triangle> Triangles = new List<Triangle>();
}

[System.Serializable]
public struct Triangle
{
    // Material index for this triangle
    public int materialIndex;

    public Vector3 v0;
    public Vector3 v1;
    public Vector3 v2;

    public Triangle(int material, Vector3 a, Vector3 b, Vector3 c)
    {
        materialIndex = material;
        v0 = a; v1 = b; v2 = c;
    }
}

[System.Serializable]
public class SphereDescription
{
    public int transformationIndex = 0;
    public int materialIndex = 0;
}

[System.Serializable]
public class BoxDescription
{
    public int transformationIndex = 0;
    public int materialIndex = 0;
}

