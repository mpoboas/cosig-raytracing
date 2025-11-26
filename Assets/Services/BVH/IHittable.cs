using UnityEngine;

// Need to define HitRecord here or in a separate file. 
// Since RayTracer.cs had a 'Hit' struct, let's define a compatible one here to be used by the BVH system.
// We will likely need to update RayTracer.cs to use this one or map between them.
// For now, let's define a clean HitRecord.

public struct HitRecord
{
    public float t;
    public Vector3 positionWS;
    public Vector3 normalWS;
    public int materialIndex;
    public bool hit;
}

public interface IHittable
{
    bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec);
    AABB GetBoundingBox();
}
