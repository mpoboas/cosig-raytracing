using UnityEngine;

/// <summary>
/// Axis-Aligned Bounding Box (AABB) for spatial acceleration structures.
/// Used for BVH construction and fast ray-box intersection tests.
/// </summary>
public struct AABB
{
    /// <summary>Minimum corner of the bounding box.</summary>
    public Vector3 min;
    
    /// <summary>Maximum corner of the bounding box.</summary>
    public Vector3 max;

    /// <summary>Creates an AABB from explicit min/max corners.</summary>
    public AABB(Vector3 min, Vector3 max)
    {
        this.min = min;
        this.max = max;
    }

    /// <summary>
    /// Returns an empty AABB (inverted bounds) that can be expanded.
    /// Any point will cause expansion when passed to Encapsulate().
    /// </summary>
    public static AABB Empty => new AABB(Vector3.positiveInfinity, Vector3.negativeInfinity);

    /// <summary>
    /// Expands the bounding box to include a point.
    /// </summary>
    public void Encapsulate(Vector3 point)
    {
        min = Vector3.Min(min, point);
        max = Vector3.Max(max, point);
    }

    /// <summary>
    /// Expands the bounding box to include another AABB.
    /// </summary>
    public void Encapsulate(AABB other)
    {
        min = Vector3.Min(min, other.min);
        max = Vector3.Max(max, other.max);
    }

    /// <summary>
    /// Tests ray-AABB intersection using the slab method.
    /// Returns true if the ray intersects the box within [tMin, tMax].
    /// </summary>
    /// <param name="ray">The ray to test</param>
    /// <param name="tMin">Minimum distance (typically 0 or small epsilon)</param>
    /// <param name="tMax">Maximum distance (or current closest hit)</param>
    public bool Intersect(Ray ray, float tMin, float tMax)
    {
        // Test intersection with each axis-aligned slab
        for (int a = 0; a < 3; a++)
        {
            float invD = 1.0f / ray.direction[a];
            float t0 = (min[a] - ray.origin[a]) * invD;
            float t1 = (max[a] - ray.origin[a]) * invD;

            // Handle negative direction (swap t0/t1)
            if (invD < 0.0f)
            {
                float temp = t0;
                t0 = t1;
                t1 = temp;
            }

            // Narrow the valid interval
            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;

            // No intersection if interval becomes invalid
            if (tMax <= tMin) return false;
        }
        return true;
    }
    
    /// <summary>Center point of the bounding box.</summary>
    public Vector3 Center => (min + max) * 0.5f;
}
