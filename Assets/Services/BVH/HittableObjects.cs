using System.Collections.Generic;
using UnityEngine;

public class SphereInstance : IHittable
{
    public Matrix4x4 objectToWorld;
    public Matrix4x4 worldToObject;
    public int materialIndex;
    public AABB bbox;

    public SphereInstance(int materialIndex, Matrix4x4 objectToWorld)
    {
        this.materialIndex = materialIndex;
        this.objectToWorld = objectToWorld;
        this.worldToObject = objectToWorld.inverse;
        
        // Calculate AABB in world space
        // Sphere is unit sphere in object space [-1, 1]
        bbox = AABB.Empty;
        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(-1, -1, -1);
        corners[1] = new Vector3( 1, -1, -1);
        corners[2] = new Vector3(-1,  1, -1);
        corners[3] = new Vector3( 1,  1, -1);
        corners[4] = new Vector3(-1, -1,  1);
        corners[5] = new Vector3( 1, -1,  1);
        corners[6] = new Vector3(-1,  1,  1);
        corners[7] = new Vector3( 1,  1,  1);

        for (int i = 0; i < 8; i++)
        {
            bbox.Encapsulate(objectToWorld.MultiplyPoint3x4(corners[i]));
        }
    }

    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        
        // Transform ray to object space
        Vector3 o = worldToObject.MultiplyPoint3x4(ray.origin);
        Vector3 d = worldToObject.MultiplyVector(ray.direction); // No normalize here to preserve t scaling? 
        // Wait, if we don't normalize, t will be in object space scale?
        // RayTracer.cs normalized the direction: Vector3 d = (M.MultiplyVector(r.dir)).normalized;
        // If we normalize d, the t we get is in object space distance. We need to convert t back to world space?
        // RayTracer.cs logic:
        // Ray rOS = TransformRay(inv, rayWS); // rOS.dir is normalized
        // IntersectUnitSphere(rOS, out tOS)
        // pOS = rOS.origin + tOS * rOS.dir
        // pWS = om.MultiplyPoint3x4(pOS)
        // tWS = (pWS - rayWS.origin).magnitude
        
        // Let's replicate that exactly.
        
        Ray rOS = new Ray(o, d.normalized);
        
        if (IntersectUnitSphere(rOS, out float tOS))
        {
            Vector3 pOS = rOS.origin + tOS * rOS.direction;
            Vector3 pWS = objectToWorld.MultiplyPoint3x4(pOS);
            float tWS = (pWS - ray.origin).magnitude;

            if (tWS < tMax && tWS > tMin)
            {
                rec.t = tWS;
                rec.positionWS = pWS;
                // Normal handling
                Vector3 nOS = pOS.normalized; // Unit sphere normal is just position
                rec.normalWS = (worldToObject.transpose.MultiplyVector(nOS)).normalized;
                rec.materialIndex = materialIndex;
                rec.hit = true;
                return true;
            }
        }
        
        return false;
    }

    public AABB GetBoundingBox() => bbox;

    private bool IntersectUnitSphere(Ray r, out float t)
    {
        float a = Vector3.Dot(r.direction, r.direction);
        float b = 2f * Vector3.Dot(r.origin, r.direction);
        float c = Vector3.Dot(r.origin, r.origin) - 1f;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) { t = 0f; return false; }
        float s = Mathf.Sqrt(disc);
        float t0 = (-b - s) / (2f * a);
        float t1 = (-b + s) / (2f * a);
        t = t0;
        if (t < 1e-4f) t = t1;
        return t >= 1e-4f;
    }
}

public class BoxInstance : IHittable
{
    public Matrix4x4 objectToWorld;
    public Matrix4x4 worldToObject;
    public int materialIndex;
    public AABB bbox;

    public BoxInstance(int materialIndex, Matrix4x4 objectToWorld)
    {
        this.materialIndex = materialIndex;
        this.objectToWorld = objectToWorld;
        this.worldToObject = objectToWorld.inverse;

        // Box is unit cube [-0.5, 0.5]
        bbox = AABB.Empty;
        Vector3[] corners = new Vector3[8];
        float h = 0.5f;
        corners[0] = new Vector3(-h, -h, -h);
        corners[1] = new Vector3( h, -h, -h);
        corners[2] = new Vector3(-h,  h, -h);
        corners[3] = new Vector3( h,  h, -h);
        corners[4] = new Vector3(-h, -h,  h);
        corners[5] = new Vector3( h, -h,  h);
        corners[6] = new Vector3(-h,  h,  h);
        corners[7] = new Vector3( h,  h,  h);

        for (int i = 0; i < 8; i++)
        {
            bbox.Encapsulate(objectToWorld.MultiplyPoint3x4(corners[i]));
        }
    }

    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        Vector3 o = worldToObject.MultiplyPoint3x4(ray.origin);
        Vector3 d = worldToObject.MultiplyVector(ray.direction).normalized;
        Ray rOS = new Ray(o, d);

        if (IntersectUnitBox(rOS, out float tOS, out Vector3 nOS))
        {
            Vector3 pOS = rOS.origin + tOS * rOS.direction;
            Vector3 pWS = objectToWorld.MultiplyPoint3x4(pOS);
            float tWS = (pWS - ray.origin).magnitude;

            if (tWS < tMax && tWS > tMin)
            {
                rec.t = tWS;
                rec.positionWS = pWS;
                rec.normalWS = (worldToObject.transpose.MultiplyVector(nOS)).normalized;
                rec.materialIndex = materialIndex;
                rec.hit = true;
                return true;
            }
        }
        return false;
    }

    public AABB GetBoundingBox() => bbox;

    private bool IntersectUnitBox(Ray r, out float t, out Vector3 n)
    {
        Vector3 min = new Vector3(-0.5f, -0.5f, -0.5f);
        Vector3 max = new Vector3(0.5f, 0.5f, 0.5f);
        t = 0f; n = Vector3.zero;
        float tmin = -1e20f, tmax = 1e20f;
        Vector3 nmin = Vector3.zero, nmax = Vector3.zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? r.origin.x : axis == 1 ? r.origin.y : r.origin.z;
            float d = axis == 0 ? r.direction.x : axis == 1 ? r.direction.y : r.direction.z;
            float invD = Mathf.Abs(d) > 1e-8f ? 1f / d : float.PositiveInfinity;
            float t1 = ( (axis==0?min.x:axis==1?min.y:min.z) - o) * invD;
            float t2 = ( (axis==0?max.x:axis==1?max.y:max.z) - o) * invD;
            Vector3 n1 = Vector3.zero; Vector3 n2 = Vector3.zero;
            if (axis == 0) { n1 = new Vector3(-1,0,0); n2 = new Vector3(1,0,0); }
            else if (axis == 1) { n1 = new Vector3(0,-1,0); n2 = new Vector3(0,1,0); }
            else { n1 = new Vector3(0,0,-1); n2 = new Vector3(0,0,1); }
            if (t1 > t2) { (t1, t2) = (t2, t1); (n1, n2) = (n2, n1); }
            if (t1 > tmin) { tmin = t1; nmin = n1; }
            if (t2 < tmax) { tmax = t2; nmax = n2; }
            if (tmin > tmax) return false;
            if (tmax < 1e-4f) return false;
        }
        t = tmin >= 1e-4f ? tmin : tmax;
        n = t == tmin ? nmin : nmax;
        return t >= 1e-4f;
    }
}

public class TriangleInstance : IHittable
{
    public Vector3 v0;
    public Vector3 v1;
    public Vector3 v2;
    public int materialIndex;
    public AABB bbox;
    public Vector3 normal;

    public TriangleInstance(int materialIndex, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        this.materialIndex = materialIndex;
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
        
        bbox = AABB.Empty;
        bbox.Encapsulate(v0);
        bbox.Encapsulate(v1);
        bbox.Encapsulate(v2);
        
        // Precompute normal
        normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
    }

    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        if (IntersectTriangleWS(ray, v0, v1, v2, out float t, out _))
        {
            if (t < tMax && t > tMin)
            {
                rec.t = t;
                rec.positionWS = ray.origin + t * ray.direction;
                rec.normalWS = normal;
                rec.materialIndex = materialIndex;
                rec.hit = true;
                return true;
            }
        }
        return false;
    }

    public AABB GetBoundingBox() => bbox;

    private bool IntersectTriangleWS(Ray r, Vector3 aWS, Vector3 bWS, Vector3 cWS, out float t, out Vector3 bary)
    {
        t = 0f; bary = Vector3.zero;
        Vector3 e1 = bWS - aWS;
        Vector3 e2 = cWS - aWS;
        Vector3 p = Vector3.Cross(r.direction, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-8f) return false;
        float invDet = 1f / det;
        Vector3 s = r.origin - aWS;
        float u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(r.direction, q) * invDet;
        if (v < 0f || u + v > 1f) return false;
        float tt = Vector3.Dot(e2, q) * invDet;
        if (tt < 1e-4f) return false;
        t = tt;
        bary = new Vector3(1f - u - v, u, v);
        return true;
    }
}
