using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
// Unity component responsible for constructing and rendering the scene based on loaded data 
public class SceneBuilder : MonoBehaviour
{
    public Material baseMaterial; // Base material used as a template for object materials 
    private SceneService sceneService = new SceneService(); // Service instance to load scene data
    private RayTracer rayTracer = new RayTracer();
    private ObjectData scene; // Parsed scene 
    void Start()
    {
        string filePath = "Assets/Resources/Scenes/test_scene_1.txt"; // Path to the scene description file 
        scene = sceneService.LoadScene(filePath); // Parse scene
        LogSceneSummary(scene); // Debug: show parser output
        // Render via ray tracer (no Unity primitives)
        var tex = rayTracer.Render(scene);
        // Save PNG to project for inspection
        string outPath = "Assets/Output/render.png";
        RayTracer.SaveTexture(tex, outPath);
        Debug.Log($"Ray tracing complete. Saved to {outPath}");
        // Optionally display on a UI RawImage if present in the scene
        var rawImage = FindObjectOfType<RawImage>();
        if (rawImage != null)
        {
            rawImage.texture = tex;
            rawImage.rectTransform.sizeDelta = new Vector2(tex.width, tex.height);
            Debug.Log("Displayed render on RawImage.");
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
}
