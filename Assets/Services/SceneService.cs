using System.Collections.Generic;
using System.IO;
using UnityEngine;
// Service responsible for loading and interpreting data from a scene configuration file 
public class SceneService
{
    // Method to load scene objects from a given configuration file path 
    public List<ObjectData> LoadSceneObjects(string filePath)
    {
        List<ObjectData> sceneObjects = new List<ObjectData>();
        // Check if the file exists before proceeding 
        if (!File.Exists(filePath))
        {
            Page 4 of 7
        Debug.LogError($"File not found at {filePath}");
            return sceneObjects;
        }
        // Read all lines from the configuration file 
        string[] lines = File.ReadAllLines(filePath);
        ObjectData currentObject = null;

        // Process each line to populate sceneObjects list 
        foreach (string line in lines)
        {
            // Start of a new object definition 
            if (line.StartsWith("Object"))
            {
                if (currentObject != null)
                    sceneObjects.Add(currentObject); // Add the previous object to the list 
                currentObject = new ObjectData(); // Initialize a new object 
            }
            // Parse transformations for the current object 
            else if (line.StartsWith("Transform"))
            {
                // Split values for translation, rotation, and scale 
                string[] values = line.Split(',');
                if (values.Length == 9)
                {
                    // Create a new Transformation based on parsed values 
                    Transformation transform = new Transformation
                    {
                        translation = new Vector3(float.Parse(values[0]),
                   float.Parse(values[1]), float.Parse(values[2])),
                        rotation = new Vector3(float.Parse(values[3]), float.Parse(values[4]),
                   float.Parse(values[5])),
                        scale = new Vector3(float.Parse(values[6]), float.Parse(values[7]),
                   float.Parse(values[8]))
                    };
                    currentObject.transformations.Add(transform); // Add the transformation to the current object 
                }
            }
            // Parse material properties for the current object 
            else if (line.StartsWith("Material"))
            {
                // Split values for color, shininess, and metallic 
                string[] values = line.Split(',');
                if (values.Length == 4)
                {
                    // Set the material properties based on parsed values 
                    currentObject.material = new MaterialProperties
                    {
                        color = new Color(float.Parse(values[0]), float.Parse(values[1]),
                   float.Parse(values[2])),
                        shininess = float.Parse(values[3]),
                        metallic = float.Parse(values[4])
                    };
                }
            }
        }
        // Add the last object to the list, if available 
        if (currentObject != null)
            sceneObjects.Add(currentObject);
        return sceneObjects;
    }
