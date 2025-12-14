using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A node in the CPU-based BVH tree for recursive ray tracing.
/// Each node either contains two children (internal) or references a hittable object (leaf).
/// This is used for CPU-side traversal; see BVHBuilder for GPU-compatible flat arrays.
/// </summary>
public class BVHNode : IHittable
{
    /// <summary>Left child node or leaf object.</summary>
    public IHittable left;
    
    /// <summary>Right child node or leaf object.</summary>
    public IHittable right;
    
    /// <summary>Bounding box enclosing both children.</summary>
    public AABB box;

    /// <summary>
    /// Constructs a BVH from a list of hittable objects.
    /// </summary>
    public BVHNode(List<IHittable> objects) : this(objects, 0, objects.Count) { }

    /// <summary>
    /// Recursively constructs a BVH node from a range of objects.
    /// Uses random axis partitioning for balanced tree construction.
    /// </summary>
    /// <param name="srcObjects">Source list of hittable objects</param>
    /// <param name="start">Start index in the list (inclusive)</param>
    /// <param name="end">End index in the list (exclusive)</param>
    public BVHNode(List<IHittable> srcObjects, int start, int end)
    {
        var objects = srcObjects;

        int objectSpan = end - start;

        // Choose random axis for partitioning (simple but effective)
        // Alternative: choose the longest axis of the bounding box
        int axis = Random.Range(0, 3);

        // Comparator for sorting objects along the chosen axis
        int Comparator(IHittable a, IHittable b)
        {
            float ac = a.GetBoundingBox().min[axis];
            float bc = b.GetBoundingBox().min[axis];
            return ac.CompareTo(bc);
        }

        if (objectSpan == 1)
        {
            // Only one object: both children point to it
            left = right = objects[start];
        }
        else if (objectSpan == 2)
        {
            // Two objects: sort and assign to children
            if (Comparator(objects[start], objects[start + 1]) < 0)
            {
                left = objects[start];
                right = objects[start + 1];
            }
            else
            {
                left = objects[start + 1];
                right = objects[start];
            }
        }
        else
        {
            // Many objects: sort and recursively partition
            objects.Sort(start, objectSpan, Comparer<IHittable>.Create(Comparator));

            int mid = start + objectSpan / 2;
            left = new BVHNode(objects, start, mid);
            right = new BVHNode(objects, mid, end);
        }

        // Compute bounding box that encloses both children
        AABB boxLeft = left.GetBoundingBox();
        AABB boxRight = right.GetBoundingBox();

        box = AABB.Empty;
        box.Encapsulate(boxLeft);
        box.Encapsulate(boxRight);
    }

    /// <summary>
    /// Tests ray intersection by first testing the bounding box,
    /// then recursively testing children if the box is hit.
    /// </summary>
    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        
        // Early exit if ray misses bounding box
        if (!box.Intersect(ray, tMin, tMax)) return false;

        // Test left child
        bool hitLeft = left.Hit(ray, tMin, tMax, out HitRecord leftRec);
        
        // Test right child (narrow tMax if left hit was found)
        bool hitRight = right.Hit(ray, tMin, hitLeft ? leftRec.t : tMax, out HitRecord rightRec);

        // Return the nearest hit
        if (hitLeft && hitRight)
        {
            rec = leftRec.t < rightRec.t ? leftRec : rightRec;
            return true;
        }
        else if (hitLeft)
        {
            rec = leftRec;
            return true;
        }
        else if (hitRight)
        {
            rec = rightRec;
            return true;
        }

        return false;
    }

    /// <summary>Returns the bounding box of this node.</summary>
    public AABB GetBoundingBox() => box;
}
