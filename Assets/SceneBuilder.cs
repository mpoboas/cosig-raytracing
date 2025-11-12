using System.Collections.Generic;
using UnityEngine;
// Unity component responsible for constructing and rendering the scene based on loaded data 
public class SceneBuilder : MonoBehaviour
{
    public Material baseMaterial; // Base material used as a template for object materials 
    private SceneService sceneService = new SceneService(); // Service instance to load scene data
    private ObjectData scene; // Parsed scene 
    void Start()
    {
        string filePath = "Assets/Resources/Scenes/test_scene_1.txt"; // Path to the scene description file 
        scene = sceneService.LoadScene(filePath); // Parse scene
        LogSceneSummary(scene); // Debug: show parser output
        BuildScene(scene); // Build and display the scene 
    }
    // Build a minimal visualization for the parsed scene 
    void BuildScene(ObjectData data)
    {
        // Camera info is logged; for runtime camera control, you'd map it to Unity Camera here.

        // Create Lights as simple visual markers (no actual Unity Light binding here)
        for (int i = 0; i < data.Lights.Count; i++)
        {
            var lightInfo = data.Lights[i];
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Light_{i}";
            ApplyCompositeTransformationByIndex(go, data, lightInfo.transformationIndex);
            ApplySolidColor(go, lightInfo.rgb);
            go.transform.localScale *= 0.5f; // smaller marker
        }

        // Create Spheres
        for (int i = 0; i < data.Spheres.Count; i++)
        {
            var s = data.Spheres[i];
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Sphere_{i}_mat{s.materialIndex}_t{s.transformationIndex}";
            ApplyCompositeTransformationByIndex(go, data, s.transformationIndex);
            ApplyMaterialByIndex(go, data, s.materialIndex);
        }

        // Create Boxes
        for (int i = 0; i < data.Boxes.Count; i++)
        {
            var b = data.Boxes[i];
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Box_{i}_mat{b.materialIndex}_t{b.transformationIndex}";
            ApplyCompositeTransformationByIndex(go, data, b.transformationIndex);
            ApplyMaterialByIndex(go, data, b.materialIndex);
        }

        // Placeholder for triangle meshes (not constructing actual Mesh here)
        for (int i = 0; i < data.TriangleMeshes.Count; i++)
        {
            var m = data.TriangleMeshes[i];
            var go = new GameObject($"TrianglesMesh_{i}_tris{m.Triangles.Count}_t{m.transformationIndex}");
            ApplyCompositeTransformationByIndex(go, data, m.transformationIndex);
        }
    }

    // Debug logging of parsed content
    void LogSceneSummary(ObjectData data)
    {
        Debug.Log($"Image: {data.Image?.horizontal}x{data.Image?.vertical} bg={data.Image?.background}");
        Debug.Log($"Transformations: {data.Transformations.Count}");
        if (data.Transformations.Count > 0)
        {
            var first = data.Transformations[0];
            Debug.Log($"  T[0] elements: {first.Elements.Count}");
        }
        if (data.Camera != null)
        {
            Debug.Log($"Camera: t={data.Camera.transformationIndex} dist={data.Camera.distance} vfov={data.Camera.verticalFovDeg}");
        }
        Debug.Log($"Lights: {data.Lights.Count}");
        Debug.Log($"Materials: {data.Materials.Count}");
        Debug.Log($"TriangleMeshes: {data.TriangleMeshes.Count}");
        Debug.Log($"Spheres: {data.Spheres.Count}");
        Debug.Log($"Boxes: {data.Boxes.Count}");
    }

    // Apply a composite transformation by index from the scene's list
    void ApplyCompositeTransformationByIndex(GameObject obj, ObjectData data, int index)
    {
        if (index < 0 || index >= data.Transformations.Count) return;
        ApplyCompositeTransformation(obj, data.Transformations[index]);
    }

    // Apply the ordered list of elementary ops
    void ApplyCompositeTransformation(GameObject obj, CompositeTransformation comp)
    {
        // Start with neutral scale
        Vector3 accumScale = Vector3.one;
        foreach (var e in comp.Elements)
        {
            switch (e.Type)
            {
                case TransformType.T:
                    obj.transform.Translate(e.XYZ, Space.World);
                    break;
                case TransformType.S:
                    accumScale = Vector3.Scale(accumScale, e.XYZ);
                    break;
                case TransformType.Rx:
                    obj.transform.Rotate(Vector3.right, e.AngleDeg, Space.World);
                    break;
                case TransformType.Ry:
                    obj.transform.Rotate(Vector3.up, e.AngleDeg, Space.World);
                    break;
                case TransformType.Rz:
                    obj.transform.Rotate(Vector3.forward, e.AngleDeg, Space.World);
                    break;
            }
        }
        obj.transform.localScale = Vector3.Scale(obj.transform.localScale, accumScale);
    }

    // Apply material by index from the scene material list
    void ApplyMaterialByIndex(GameObject obj, ObjectData data, int materialIndex)
    {
        if (materialIndex < 0 || materialIndex >= data.Materials.Count)
        {
            return;
        }
        var m = data.Materials[materialIndex];
        var mat = CreateMaterialInstance();
        mat.color = m.color; // map base color
        // Map some coefficients to Unity standard shader heuristically
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Mathf.Clamp01(m.specular));
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", Mathf.Clamp01(m.diffuse));
        obj.GetComponent<Renderer>().material = mat;
    }

    // Apply a solid color material (for light markers)
    void ApplySolidColor(GameObject obj, Color c)
    {
        var mat = CreateMaterialInstance();
        mat.color = c;
        obj.GetComponent<Renderer>().material = mat;
    }

    // Safely create a material instance from baseMaterial; fall back to Standard shader if not assigned
    Material CreateMaterialInstance()
    {
        Material source = baseMaterial;
        if (source == null)
        {
            var fallbackShader = Shader.Find("Standard");
            if (fallbackShader == null)
            {
                Debug.LogWarning("Standard shader not found. Creating a default material.");
                return new Material(Shader.Find("Diffuse"));
            }
            Debug.LogWarning("baseMaterial not assigned in SceneBuilder. Using Standard shader as fallback.");
            source = new Material(fallbackShader);
        }
        return new Material(source);
    }
}
