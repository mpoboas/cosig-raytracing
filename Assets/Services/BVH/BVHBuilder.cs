using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// GPU-compatible structs
public struct GPUTriangle
{
    public Vector3 v0;
    public Vector3 v1;
    public Vector3 v2;
    public Vector3 n0; // Vertex normal at v0
    public Vector3 n1; // Vertex normal at v1
    public Vector3 n2; // Vertex normal at v2
    public Vector3 center; // Pre-calculated for BVH build
    public int materialIndex;
    // Total: 12*7 + 4 = 88 bytes
}

public struct GPUBVHNode
{
    // AABB Min (12 bytes)
    public Vector3 min;
    // Index of Left Child (if internal) OR Index of First Triangle (if leaf) (4 bytes)
    public int leftOrFirst;

    // AABB Max (12 bytes)
    public Vector3 max;
    // Count of Triangles (if leaf) OR 0 (if internal) (4 bytes)
    public int count; 
    
    // Total: 32 bytes (perfect alignment)
}

public class BVHBuilder
{
    // Temporary node for construction
    private class Node
    {
        public AABB bounds;
        public Node left;
        public Node right;
        public int start; // Index in the triangle list
        public int count; // Number of triangles
    }

    private List<GPUTriangle> allTriangles;
    private List<int> currentIndices; // Working list of indices to sort/split

    private const int MAX_TRIANGLES_PER_LEAF = 4;

    public struct BVHResult
    {
        public GPUBVHNode[] nodes;
        public GPUTriangle[] triangles; // Re-ordered
        public int[] triangleIndices; 
    }

    public BVHResult Build(List<GPUTriangle> inputTriangles)
    {
        allTriangles = inputTriangles;
        currentIndices = Enumerable.Range(0, inputTriangles.Count).ToList();

        // 1. Build Recursively
        Node root = BuildRecursive(0, currentIndices.Count);

        // 2. Flatten to Array
        List<GPUBVHNode> linearNodes = new List<GPUBVHNode>();
        List<GPUTriangle> reorderedTriangles = new List<GPUTriangle>();

        Flatten(root, linearNodes, reorderedTriangles);

        return new BVHResult
        {
            nodes = linearNodes.ToArray(),
            triangles = reorderedTriangles.ToArray()
        };
    }

    private Node BuildRecursive(int start, int count)
    {
        Node node = new Node();
        
        // Caculate Bounds
        AABB bounds = AABB.Empty;
        for (int i = 0; i < count; i++)
        {
            int triIdx = currentIndices[start + i];
            Vector3 v0 = allTriangles[triIdx].v0;
            Vector3 v1 = allTriangles[triIdx].v1;
            Vector3 v2 = allTriangles[triIdx].v2;
            
            // Encapsulate all vertices
            bounds.Encapsulate(v0);
            bounds.Encapsulate(v1);
            bounds.Encapsulate(v2);
        }
        node.bounds = bounds;
        node.start = start;
        node.count = count;

        // Leaf Criteria
        if (count <= MAX_TRIANGLES_PER_LEAF)
        {
            return node;
        }

        // Split Heuristic: Median Split on Longest Axis
        Vector3 size = bounds.max - bounds.min;
        int axis = 0;
        if (size.y > size.x) axis = 1;
        if (size.z > size[axis]) axis = 2;

        float splitPos = bounds.Center[axis];

        // Partition the current range of indices in-place
        // We want to pivot around the splitPos based on triangle centers
        int mid = Partition(start, count, axis, splitPos);

        // If partition failed to separate (e.g. all centers same pos), make leaf
        if (mid == start || mid == start + count)
        {
            return node;
        }

        node.left = BuildRecursive(start, mid - start);
        node.right = BuildRecursive(mid, (start + count) - mid);
        node.count = 0; // Mark as internal

        return node;
    }

    private int Partition(int start, int count, int axis, float pivot)
    {
        int i = start;
        int j = start + count - 1;

        while (i <= j)
        {
            // Center of triangle at index i
            float c = allTriangles[currentIndices[i]].center[axis];
            
            if (c < pivot)
            {
                i++;
            }
            else
            {
                // Swap indices[i] and indices[j]
                int temp = currentIndices[i];
                currentIndices[i] = currentIndices[j];
                currentIndices[j] = temp;
                j--;
            }
        }
        return i; // Split point
    }

    // Iterative Flatten
    private void Flatten(Node node, List<GPUBVHNode> nodes, List<GPUTriangle> tris)
    {
        Queue<Node> queue = new Queue<Node>();
        Queue<int> indexQueue = new Queue<int>();

        // Add root
        nodes.Add(new GPUBVHNode()); // Root is at 0
        queue.Enqueue(node);
        indexQueue.Enqueue(0);

        while (queue.Count > 0)
        {
            Node n = queue.Dequeue();
            int idx = indexQueue.Dequeue();

            GPUBVHNode gpuNode = new GPUBVHNode();
            gpuNode.min = n.bounds.min;
            gpuNode.max = n.bounds.max;

            if (n.count > 0) // Leaf
            {
                gpuNode.count = n.count;
                gpuNode.leftOrFirst = tris.Count;
                
                for (int k = 0; k < n.count; k++)
                    tris.Add(allTriangles[currentIndices[n.start + k]]);
            }
            else // Internal
            {
                gpuNode.count = 0;
                
                // Allocate 2 children slots contiguously
                int leftIdx = nodes.Count;
                nodes.Add(new GPUBVHNode()); // Slot for left
                nodes.Add(new GPUBVHNode()); // Slot for right
                
                gpuNode.leftOrFirst = leftIdx; // Point to left. Right is implicit (left + 1)

                // Enqueue children to be processed
                queue.Enqueue(n.left);
                indexQueue.Enqueue(leftIdx);
                
                queue.Enqueue(n.right);
                indexQueue.Enqueue(leftIdx + 1);
            }

            nodes[idx] = gpuNode;
        }
    }
}
