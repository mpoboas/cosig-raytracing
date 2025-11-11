using System.Collections.Generic;
using UnityEngine;
using Models;
using Services;
// Unity component responsible for constructing and rendering the scene based on loaded data 
public class SceneBuilder : MonoBehaviour
{
    public Material baseMaterial; // Base material used as a template for object materials 
    private SceneService sceneService = new SceneService(); // Service instance to load scene data
    private List<ObjectData> sceneObjects = new List<ObjectData>(); // List of scene objects 
    void Start()
    {
        string filePath = "Assets/Resources/Config/scene_config.txt"; // Path to the configuration file 
        sceneObjects = sceneService.LoadSceneObjects(filePath); // Load objects from configuration
        BuildScene(); // Build and display the scene 
    }
    // Method to create each object in the scene based on loaded data 
    void BuildScene()
    {
        foreach (var objData in sceneObjects)
        {
            // Create a primitive object (using a cube as an example here) 
            GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ApplyTransformations(obj, objData.transformations); // Apply transformations to object
            ApplyMaterial(obj, objData.material); // Apply material properties to object 
        }
    }
    // Apply transformations to a given object based on the list of transformations 
    void ApplyTransformations(GameObject obj, List<Transformation> transformations)
    {
        foreach (var trans in transformations)
        {
            obj.transform.Translate(trans.translation, Space.World); // Apply position 
            obj.transform.Rotate(trans.rotation); // Apply rotation 
            obj.transform.localScale = trans.scale; // Apply scale 
        }
    }
    // Apply material properties to the given object 
    void ApplyMaterial(GameObject obj, MaterialProperties properties)
    {
        Material newMaterial = new Material(baseMaterial); // Create new material from base 
        newMaterial.color = properties.color; // Set color 
        newMaterial.SetFloat("_Shininess", properties.shininess); // Set shininess 
        newMaterial.SetFloat("_Metallic", properties.metallic); // Set metallic 
        obj.GetComponent<Renderer>().material = newMaterial; // Assign material to object 
    }
}
