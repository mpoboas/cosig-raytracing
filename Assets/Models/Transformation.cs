using UnityEngine;
// Class to represent transformations for an object, including position, rotation, and scale 
[System.Serializable]
public class Transformation
{
    public Vector3 translation = Vector3.zero; // Position offset for the object 
    public Vector3 rotation = Vector3.zero; // Rotation values for the object 
    public Vector3 scale = Vector3.one; // Scale multiplier for the object 
}