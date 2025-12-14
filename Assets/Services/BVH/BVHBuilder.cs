using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// GPU-compatible triangle structure (88 bytes total).
/// Contains vertex positions, per-vertex normals for smooth shading,
/// a pre-computed center for BVH partitioning, and material index.
/// </summary>
public struct GPUTriangle
{
    public Vector3 v0;           // Vertex 0 position (12 bytes)
    public Vector3 v1;           // Vertex 1 position (12 bytes)
    public Vector3 v2;           // Vertex 2 position (12 bytes)
    public Vector3 n0;           // Normal at v0 for smooth shading (12 bytes)
    public Vector3 n1;           // Normal at v1 for smooth shading (12 bytes)
    public Vector3 n2;           // Normal at v2 for smooth shading (12 bytes)
    public Vector3 center;       // Triangle centroid for BVH partitioning (12 bytes)
    public int materialIndex;    // Index into material buffer (4 bytes)
    // Total: 7 * 12 + 4 = 88 bytes
}

/// <summary>
/// GPU-compatible BVH node structure (32 bytes, cache-aligned).
/// Uses a compact representation where internal nodes and leaf nodes share the same struct.
/// </summary>
public struct GPUBVHNode
{
    public Vector3 min;          // AABB minimum corner (12 bytes)
    public int leftOrFirst;      // Internal: left child index | Leaf: first triangle index (4 bytes)
    public Vector3 max;          // AABB maximum corner (12 bytes)
    public int count;            // Internal: 0 | Leaf: triangle count (4 bytes)
    // Total: 32 bytes (optimal GPU cache alignment)
}

/// <summary>
/// Builds a Bounding Volume Hierarchy (BVH) for efficient ray-triangle intersection.
/// Uses median-split partitioning on the longest axis for balanced tree construction.
/// The resulting structure is flattened into arrays suitable for GPU traversal.
/// </summary>
public class BVHBuilder
{
    /// <summary>
    /// Internal tree node used during construction (before flattening).
    /// </summary>
    private class Node
    {
        public AABB bounds;      // Bounding box enclosing all triangles
        public Node left;        // Left child (null for leaves)
        public Node right;       // Right child (null for leaves)
        public int start;        // Index into triangle index array
        public int count;        // Number of triangles (0 for internal nodes)
    }

    private List<GPUTriangle> allTriangles;
    private List<int> currentIndices;    // Working list for in-place partitioning

    private const int MAX_TRIANGLES_PER_LEAF = 4;  // Leaf creation threshold

    /// <summary>
    /// Result of BVH construction containing flattened arrays for GPU upload.
    /// </summary>
    public struct BVHResult
    {
        public GPUBVHNode[] nodes;       // Flattened BVH nodes
        public GPUTriangle[] triangles;  // Triangles reordered to match leaf references
        public int[] triangleIndices;    // Original triangle indices (for debugging)
    }

    /// <summary>
    /// Builds a BVH from a list of triangles using median-split partitioning.
    /// Returns flattened arrays ready for GPU upload.
    /// </summary>
    /// <param name="inputTriangles">List of triangles to build BVH from</param>
    /// <returns>BVHResult containing nodes and reordered triangles</returns>
    public BVHResult Build(List<GPUTriangle> inputTriangles)
    {
        allTriangles = inputTriangles;
        currentIndices = Enumerable.Range(0, inputTriangles.Count).ToList();

        // Build tree recursively
        Node root = BuildRecursive(0, currentIndices.Count);

        // Flatten tree to arrays for GPU
        List<GPUBVHNode> linearNodes = new List<GPUBVHNode>();
        List<GPUTriangle> reorderedTriangles = new List<GPUTriangle>();

        Flatten(root, linearNodes, reorderedTriangles);

        return new BVHResult
        {
            nodes = linearNodes.ToArray(),
            triangles = reorderedTriangles.ToArray()
        };
    }

    /// <summary>
    /// Recursively builds the BVH tree using median-split on the longest axis.
    /// </summary>
    /// <param name="start">Start index in the currentIndices array</param>
    /// <param name="count">Number of triangles to include</param>
    /// <returns>Root node of the subtree</returns>
    private Node BuildRecursive(int start, int count)
    {
        Node node = new Node();
        
        // Calculate bounding box for all triangles in this range
        AABB bounds = AABB.Empty;
        for (int i = 0; i < count; i++)
        {
            int triIdx = currentIndices[start + i];
            Vector3 v0 = allTriangles[triIdx].v0;
            Vector3 v1 = allTriangles[triIdx].v1;
            Vector3 v2 = allTriangles[triIdx].v2;
            
            bounds.Encapsulate(v0);
            bounds.Encapsulate(v1);
            bounds.Encapsulate(v2);
        }
        node.bounds = bounds;
        node.start = start;
        node.count = count;

        // Create leaf if triangle count is below threshold
        if (count <= MAX_TRIANGLES_PER_LEAF)
        {
            return node;
        }

        // Find the longest axis for splitting
        Vector3 size = bounds.max - bounds.min;
        int axis = 0;
        if (size.y > size.x) axis = 1;
        if (size.z > size[axis]) axis = 2;

        float splitPos = bounds.Center[axis];

        // Partition triangles around the split position
        int mid = Partition(start, count, axis, splitPos);

        // If partition failed (all triangles on one side), create a leaf
        if (mid == start || mid == start + count)
        {
            return node;
        }

        // Recursively build children
        node.left = BuildRecursive(start, mid - start);
        node.right = BuildRecursive(mid, (start + count) - mid);
        node.count = 0; // Mark as internal node

        return node;
    }

    /// <summary>
    /// Partitions triangle indices in-place around a pivot value.
    /// Uses a two-pointer approach similar to quicksort partitioning.
    /// </summary>
    /// <returns>Index of the partition point</returns>
    private int Partition(int start, int count, int axis, float pivot)
    {
        int i = start;
        int j = start + count - 1;

        while (i <= j)
        {
            float center = allTriangles[currentIndices[i]].center[axis];
            
            if (center < pivot)
            {
                i++;
            }
            else
            {
                // Swap indices to move this triangle to the right partition
                int temp = currentIndices[i];
                currentIndices[i] = currentIndices[j];
                currentIndices[j] = temp;
                j--;
            }
        }
        return i;
    }

    /// <summary>
    /// Flattens the tree structure into contiguous arrays for GPU traversal.
    /// Uses BFS order so that child nodes are allocated contiguously (left, right).
    /// </summary>
    private void Flatten(Node node, List<GPUBVHNode> nodes, List<GPUTriangle> tris)
    {
        Queue<Node> queue = new Queue<Node>();
        Queue<int> indexQueue = new Queue<int>();

        // Initialize with root node
        nodes.Add(new GPUBVHNode());
        queue.Enqueue(node);
        indexQueue.Enqueue(0);

        while (queue.Count > 0)
        {
            Node n = queue.Dequeue();
            int idx = indexQueue.Dequeue();

            GPUBVHNode gpuNode = new GPUBVHNode();
            gpuNode.min = n.bounds.min;
            gpuNode.max = n.bounds.max;

            if (n.count > 0) // Leaf node
            {
                gpuNode.count = n.count;
                gpuNode.leftOrFirst = tris.Count; // Index of first triangle
                
                // Copy triangles to output array
                for (int k = 0; k < n.count; k++)
                    tris.Add(allTriangles[currentIndices[n.start + k]]);
            }
            else // Internal node
            {
                gpuNode.count = 0;
                
                // Allocate contiguous slots for both children
                int leftIdx = nodes.Count;
                nodes.Add(new GPUBVHNode()); // Left child slot
                nodes.Add(new GPUBVHNode()); // Right child slot
                
                gpuNode.leftOrFirst = leftIdx; // Right child is implicitly at leftIdx + 1

                // Queue children for processing
                queue.Enqueue(n.left);
                indexQueue.Enqueue(leftIdx);
                
                queue.Enqueue(n.right);
                indexQueue.Enqueue(leftIdx + 1);
            }

            nodes[idx] = gpuNode;
        }
    }
}
