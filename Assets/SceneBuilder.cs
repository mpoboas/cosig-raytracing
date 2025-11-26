using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using System.IO;
// Unity component responsible for constructing and rendering the scene based on loaded data 
public class SceneBuilder : MonoBehaviour
{
    public Material baseMaterial; // Base material used as a template for object materials 
    private SceneService sceneService = new SceneService(); // Service instance to load scene data
    private RayTracer rayTracer = new RayTracer();
    private ObjectData scene; // Parsed scene 
    void Start()
    {
        string filePath = "Assets/Resources/Scenes/test_scene_2.txt"; // Path to the scene description file 
        scene = sceneService.LoadScene(filePath); // Parse scene
        LogSceneSummary(scene); // Debug: show parser output
        // Render via ray tracer (no Unity primitives)
        var tex = rayTracer.Render(scene);
        // Save PNG to project for inspection
        string outPath = "Assets/Output/render.png";
        RayTracer.SaveTexture(tex, outPath);
        Debug.Log($"Ray tracing complete. Saved to {outPath}");
        // Display on UI Toolkit VisualElement if present
        var uiDocument = FindFirstObjectByType<UIDocument>();
        if (uiDocument != null)
        {
            // Get the container for the ray-traced image
            var container = uiDocument.rootVisualElement.Q<VisualElement>("ray-traced-image");
            if (container != null)
            {
                // Clear any existing content
                container.Clear();
                
                // Create a new UI Toolkit Image element
                var image = new UnityEngine.UIElements.Image();
                
                // Convert the Texture2D to a Sprite
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                image.sprite = sprite;
                
                // Set image to scale to fit the container while maintaining aspect ratio
                image.style.width = new StyleLength(Length.Percent(100));
                image.style.height = new StyleLength(Length.Percent(100));
                image.scaleMode = ScaleMode.ScaleToFit;
                
                // Add the image to the container
                container.Add(image);
                
                Debug.Log("Displayed render on UI Toolkit VisualElement.");
            }
            else
            {
                Debug.LogWarning("Could not find 'ray-traced-image' VisualElement in the UI Document.");
            }
        }
        else
        {
            Debug.LogWarning("No UIDocument found in the scene to display the rendered image.");
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
