using UnityEngine;

/// <summary>
/// Record of a ray-object intersection.
/// Populated by IHittable.Hit() when a ray hits an object.
/// </summary>
public struct HitRecord
{
    /// <summary>Distance from ray origin to hit point.</summary>
    public float t;
    
    /// <summary>Hit position in world space.</summary>
    public Vector3 positionWS;
    
    /// <summary>Surface normal at hit point in world space.</summary>
    public Vector3 normalWS;
    
    /// <summary>Index of the material at this hit point.</summary>
    public int materialIndex;
    
    /// <summary>True if a valid intersection was found.</summary>
    public bool hit;
}

/// <summary>
/// Interface for objects that can be intersected by rays.
/// Implemented by primitives (spheres, boxes, triangles) and BVH nodes.
/// </summary>
public interface IHittable
{
    /// <summary>
    /// Tests for ray intersection within the given distance range.
    /// </summary>
    /// <param name="ray">Ray to test</param>
    /// <param name="tMin">Minimum valid distance (excludes self-intersection)</param>
    /// <param name="tMax">Maximum valid distance (or current closest hit)</param>
    /// <param name="rec">Output hit record if intersection found</param>
    /// <returns>True if a valid intersection was found</returns>
    bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec);
    
    /// <summary>
    /// Returns the axis-aligned bounding box for this object.
    /// Used by BVH construction for spatial partitioning.
    /// </summary>
    AABB GetBoundingBox();
}
