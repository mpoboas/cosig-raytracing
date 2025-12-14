using System.Collections.Generic;
using UnityEngine;

// Sphere primitive with transform support for CPU ray tracing
// Represents a unit sphere in object space, transformed to world space
public class SphereInstance : IHittable
{
    public Matrix4x4 objectToWorld;   // Object-to-world transformation
    public Matrix4x4 worldToObject;   // Cached inverse for ray transformation
    public int materialIndex;          // Material reference
    public AABB bbox;                   // World-space bounding box

    // Creates a sphere instance with the given transformation
    // The unit sphere (radius 1 at origin) is transformed to world space
    public SphereInstance(int materialIndex, Matrix4x4 objectToWorld)
    {
        this.materialIndex = materialIndex;
        this.objectToWorld = objectToWorld;
        this.worldToObject = objectToWorld.inverse;
        
        // Calculate world-space AABB by transforming unit sphere corners
        // Unit sphere fits in [-1, 1] cube
        bbox = AABB.Empty;
        Vector3[] corners = new Vector3[]
        {
            new Vector3(-1, -1, -1),
            new Vector3( 1, -1, -1),
            new Vector3(-1,  1, -1),
            new Vector3( 1,  1, -1),
            new Vector3(-1, -1,  1),
            new Vector3( 1, -1,  1),
            new Vector3(-1,  1,  1),
            new Vector3( 1,  1,  1)
        };
        for (int i = 0; i < 8; i++)
        {
            bbox.Encapsulate(objectToWorld.MultiplyPoint3x4(corners[i]));
        }
    }

    // Tests ray-sphere intersection by transforming the ray to object space,
    // intersecting with a unit sphere, then transforming results back
    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        
        // Transform ray to object space
        Vector3 o = worldToObject.MultiplyPoint3x4(ray.origin);
        Vector3 d = worldToObject.MultiplyVector(ray.direction);
        
        // Normalize direction in object space
        // This changes the t-parameter scale, so we convert back to world space later
        Ray rOS = new Ray(o, d.normalized);
        
        if (IntersectUnitSphere(rOS, out float tOS))
        {
            // Calculate hit point in object space, then transform to world space
            Vector3 pOS = rOS.origin + tOS * rOS.direction;
            Vector3 pWS = objectToWorld.MultiplyPoint3x4(pOS);
            float tWS = (pWS - ray.origin).magnitude;

            if (tWS < tMax && tWS > tMin)
            {
                rec.t = tWS;
                rec.positionWS = pWS;
                
                // For unit sphere, normal at surface point = normalized position
                // Transform normal using inverse-transpose for correct handling of non-uniform scale
                Vector3 nOS = pOS.normalized;
                rec.normalWS = (worldToObject.transpose.MultiplyVector(nOS)).normalized;
                rec.materialIndex = materialIndex;
                rec.hit = true;
                return true;
            }
        }
        
        return false;
    }

    public AABB GetBoundingBox() => bbox;

    // Intersects a ray with a unit sphere at origin using the quadratic formula
    private bool IntersectUnitSphere(Ray r, out float t)
    {
        // Ray: P = O + t*D
        // Sphere: |P|^2 = 1
        // Substitute: |O + t*D|^2 = 1
        // Expand: t^2(D·D) + 2t(O·D) + (O·O - 1) = 0
        
        float a = Vector3.Dot(r.direction, r.direction);
        float b = 2f * Vector3.Dot(r.origin, r.direction);
        float c = Vector3.Dot(r.origin, r.origin) - 1f;
        float disc = b * b - 4f * a * c;
        
        if (disc < 0f) { t = 0f; return false; }
        
        float s = Mathf.Sqrt(disc);
        float t0 = (-b - s) / (2f * a);  // Near intersection
        float t1 = (-b + s) / (2f * a);  // Far intersection
        
        const float kEpsilon = 1e-3f;
        
        // Return nearest positive intersection
        if (t0 > kEpsilon) t = t0;
        else t = t1;
        
        return t > kEpsilon;
    }
}

// Box primitive with transform support for CPU ray tracing
// Represents a unit cube (-0.5 to +0.5) in object space, transformed to world space
public class BoxInstance : IHittable
{
    public Matrix4x4 objectToWorld;
    public Matrix4x4 worldToObject;
    public int materialIndex;
    public AABB bbox;

    // Creates a box instance with the given transformation.
    // The unit cube is transformed to world space
    public BoxInstance(int materialIndex, Matrix4x4 objectToWorld)
    {
        this.materialIndex = materialIndex;
        this.objectToWorld = objectToWorld;
        this.worldToObject = objectToWorld.inverse;

        // Calculate world-space AABB from unit cube corners
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

    // Tests ray-box intersection by transforming the ray to object space,
    // intersecting with a unit cube, then transforming results back
    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        
        // Transform ray to object space
        Vector3 o = worldToObject.MultiplyPoint3x4(ray.origin);
        Vector3 d = worldToObject.MultiplyVector(ray.direction).normalized;
        Ray rOS = new Ray(o, d);

        if (IntersectUnitBox(rOS, out float tOS, out Vector3 nOS))
        {
            // Convert hit back to world space
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

    // Intersects a ray with a unit cube using the slab method
    // Returns the intersection distance and face normal
    private bool IntersectUnitBox(Ray r, out float t, out Vector3 n)
    {
        Vector3 min = new Vector3(-0.5f, -0.5f, -0.5f);
        Vector3 max = new Vector3(0.5f, 0.5f, 0.5f);
        t = 0f; n = Vector3.zero;
        float tmin = -1e20f, tmax = 1e20f;
        Vector3 nmin = Vector3.zero, nmax = Vector3.zero;
        
        const float kEpsilon = 1e-3f;

        // Test intersection with each slab (pair of parallel planes)
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? r.origin.x : axis == 1 ? r.origin.y : r.origin.z;
            float d = axis == 0 ? r.direction.x : axis == 1 ? r.direction.y : r.direction.z;
            float invD = Mathf.Abs(d) > 1e-8f ? 1f / d : float.PositiveInfinity;
            
            float t1 = ((axis == 0 ? min.x : axis == 1 ? min.y : min.z) - o) * invD;
            float t2 = ((axis == 0 ? max.x : axis == 1 ? max.y : max.z) - o) * invD;
            
            // Face normals for this axis
            Vector3 n1 = Vector3.zero, n2 = Vector3.zero;
            if (axis == 0) { n1 = new Vector3(-1, 0, 0); n2 = new Vector3(1, 0, 0); }
            else if (axis == 1) { n1 = new Vector3(0, -1, 0); n2 = new Vector3(0, 1, 0); }
            else { n1 = new Vector3(0, 0, -1); n2 = new Vector3(0, 0, 1); }
            
            // Ensure t1 < t2
            if (t1 > t2) { (t1, t2) = (t2, t1); (n1, n2) = (n2, n1); }
            
            // Narrow the [tmin, tmax] interval
            if (t1 > tmin) { tmin = t1; nmin = n1; }
            if (t2 < tmax) { tmax = t2; nmax = n2; }
            
            // No intersection if interval is empty
            if (tmin > tmax) return false;
            if (tmax < kEpsilon) return false;
        }
        
        // Return nearest positive intersection
        t = tmin >= kEpsilon ? tmin : tmax;
        n = t == tmin ? nmin : nmax;
        return t >= kEpsilon;
    }
}

// Triangle primitive for CPU ray tracing
// Stores pre-transformed vertices in world space
public class TriangleInstance : IHittable
{
    public Vector3 v0;           // First vertex (world space)
    public Vector3 v1;           // Second vertex (world space)
    public Vector3 v2;           // Third vertex (world space)
    public int materialIndex;
    public AABB bbox;
    public Vector3 normal;       // Pre-computed face normal

    // Creates a triangle from three vertices in world space
    public TriangleInstance(int materialIndex, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        this.materialIndex = materialIndex;
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
        
        // Compute bounding box
        bbox = AABB.Empty;
        bbox.Encapsulate(v0);
        bbox.Encapsulate(v1);
        bbox.Encapsulate(v2);
        
        // Pre-compute face normal
        normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
    }

    // Tests ray-triangle intersection using the Möller-Trumbore algorithm
    public bool Hit(Ray ray, float tMin, float tMax, out HitRecord rec)
    {
        rec = new HitRecord();
        
        if (IntersectTriangleWS(ray, v0, v1, v2, out float t, out Vector3 bary))
        {
            if (t < tMax && t > tMin)
            {
                rec.t = t;
                // Compute hit position using barycentric interpolation
                rec.positionWS = bary.x * v0 + bary.y * v1 + bary.z * v2;
                rec.normalWS = normal;
                rec.materialIndex = materialIndex;
                rec.hit = true;
                return true;
            }
        }
        return false;
    }

    public AABB GetBoundingBox() => bbox;

    // Möller-Trumbore ray-triangle intersection algorithm
    // Returns distance t and barycentric coordinates (alpha, beta, gamma) where
    // hit_point = alpha*v0 + beta*v1 + gamma*v2
    private bool IntersectTriangleWS(Ray r, Vector3 aWS, Vector3 bWS, Vector3 cWS, out float t, out Vector3 bary)
    {
        t = 0f; bary = Vector3.zero;
        const float kEpsilon = 1e-3f;

        Vector3 e1 = bWS - aWS;  // Edge 1
        Vector3 e2 = cWS - aWS;  // Edge 2
        Vector3 p = Vector3.Cross(r.direction, e2);
        float det = Vector3.Dot(e1, p);
        
        // Ray parallel to triangle (det ≈ 0)
        if (Mathf.Abs(det) < kEpsilon) return false;
        
        float invDet = 1f / det;
        Vector3 s = r.origin - aWS;
        float beta = Vector3.Dot(s, p) * invDet;
        
        // Check beta barycentric coordinate bounds
        if (beta < -kEpsilon || beta > 1.0f + kEpsilon) return false;
        
        Vector3 q = Vector3.Cross(s, e1);
        float gamma = Vector3.Dot(r.direction, q) * invDet;
        
        // Check gamma and combined bounds
        if (gamma < -kEpsilon || beta + gamma > 1.0f + kEpsilon) return false;
        
        float tt = Vector3.Dot(e2, q) * invDet;
        if (tt < kEpsilon) return false;
        
        t = tt;
        bary = new Vector3(1f - beta - gamma, beta, gamma);  // (alpha, beta, gamma)
        return true;
    }
}
