using System.Collections.Generic;
using UnityEngine;

public class BVHNode : IHittable
{
    public IHittable left;
    public IHittable right;
    public AABB box;

    public BVHNode(List<IHittable> objects) : this(objects, 0, objects.Count) { }

    // Constructor that takes a slice of the list
    public BVHNode(List<IHittable> srcObjects, int start, int end)
    {
        var objects = srcObjects; // Reference to the list (we will sort it in place, which is fine)

        int objectSpan = end - start;

        // Choose axis to split based on random or round robin. 
        // Better: choose longest axis of the bounding box of the centroids?
        // For simplicity, let's pick a random axis or just cycle depth % 3. 
        // Since we don't track depth here easily without passing it, let's just use Random.
        int axis = Random.Range(0, 3);

        // Comparator
        int Comparator(IHittable a, IHittable b)
        {
            float ac = a.GetBoundingBox().min[axis];
            float bc = b.GetBoundingBox().min[axis];
            return ac.CompareTo(bc);
        }

        if (objectSpan == 1)
        {
            left = right = objects[start];
        }
        else if (objectSpan == 2)
        {
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
            // Sort the sub-list
            objects.Sort(start, objectSpan, Comparer<IHittable>.Create(Comparator));

            int mid = start + objectSpan / 2;
            left = new BVHNode(objects, start, mid);
            right = new BVHNode(objects, mid, end);
        }

        AABB boxLeft = left.GetBoundingBox();
        AABB boxRight = right.GetBoundingBox();

        box = AABB.Empty;
        box.Encapsulate(boxLeft);
        box.Encapsulate(boxRight);
    }

    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        if (!box.Intersect(ray, tMin, tMax)) return false;

        bool hitLeft = left.Hit(ray, tMin, tMax, out HitRecord leftRec);
        bool hitRight = right.Hit(ray, tMin, hitLeft ? leftRec.t : tMax, out HitRecord rightRec);

        if (hitLeft && hitRight)
        {
            if (leftRec.t < rightRec.t)
            {
                rec = leftRec;
            }
            else
            {
                rec = rightRec;
            }
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

    public AABB GetBoundingBox() => box;
}
