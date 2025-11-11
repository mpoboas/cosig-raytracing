using UnityEngine;
// Class to define the properties of a material, such as color, shininess, and metallic factor 
[System.Serializable]
public class MaterialProperties
{
    public Color color = Color.white; // Base color of the material 
    public float shininess = 0.5f; // Reflective quality of the material 
    public float metallic = 0.5f; // Metal-like quality of the material 
}