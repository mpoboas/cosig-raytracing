using UnityEngine;

public struct AABB
{
    public Vector3 min;
    public Vector3 max;

    public AABB(Vector3 min, Vector3 max)
    {
        this.min = min;
        this.max = max;
    }

    public static AABB Empty => new AABB(Vector3.positiveInfinity, Vector3.negativeInfinity);

    public void Encapsulate(Vector3 point)
    {
        min = Vector3.Min(min, point);
        max = Vector3.Max(max, point);
    }

    public void Encapsulate(AABB other)
    {
        min = Vector3.Min(min, other.min);
        max = Vector3.Max(max, other.max);
    }

    public bool Intersect(Ray ray, float tMin, float tMax)
    {
        for (int a = 0; a < 3; a++)
        {
            float invD = 1.0f / ray.direction[a];
            float t0 = (min[a] - ray.origin[a]) * invD;
            float t1 = (max[a] - ray.origin[a]) * invD;

            if (invD < 0.0f)
            {
                float temp = t0;
                t0 = t1;
                t1 = temp;
            }

            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;

            if (tMax <= tMin) return false;
        }
        return true;
    }
    
    public Vector3 Center => (min + max) * 0.5f;
}
