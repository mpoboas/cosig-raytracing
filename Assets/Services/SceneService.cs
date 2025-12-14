using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Parses scene description files into ObjectData structures.
/// 
/// The file format uses brace-delimited segments for each scene element:
/// - Image: resolution and background color
/// - Transformation: composite transforms (T, S, Rx, Ry, Rz)
/// - Camera: transformation index, distance, and FOV
/// - Light: transformation index and RGB color
/// - Material: color and lighting coefficients
/// - Triangles: mesh of triangles with shared transformation
/// - Sphere/Box: primitive shapes with transformation and material
/// </summary>
public class SceneService
{
    /// <summary>
    /// Loads and parses a scene file into an ObjectData structure.
    /// </summary>
    /// <param name="filePath">Path to the scene description file</param>
    /// <returns>Parsed scene data, or empty ObjectData if file not found</returns>
    public ObjectData LoadScene(string filePath)
    {
        if (!File.Exists(filePath))
        {
            // If there is no file in the file path, returns empty scene
            Debug.LogError($"File not found at {filePath}");
            return new ObjectData();
        }

        var scene = new ObjectData(); // Creates a empty scene to recieve the objects
        var lines = File.ReadAllLines(filePath); // Reads the file and separates the lines into an array
        int i = 0;

        while (i < lines.Length)
        {
            string line = Clean(lines[i]);
            i++;
            if (string.IsNullOrEmpty(line)) continue; // Skips to next line when empty or null

            if (IsSegment(line, "Image"))
            {
                // Parse image settings: resolution (horizontal, vertical) and background color (r, g, b)
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with horizontal & vertical resolution values
                var res = ParseFloats(Clean(lines[i++]));
                // Reads line with background r g b values
                var bg = ParseFloats(Clean(lines[i++]));
                // Skips the line with "}"
                ExpectClosingBrace(lines, ref i);

                // Creates image in scene 
                scene.Image = new ImageSettings
                {
                    horizontal = (int)res[0],
                    vertical = (int)res[1],
                    background = new Color((float)bg[0], (float)bg[1], (float)bg[2])
                };
            }
            else if (IsSegment(line, "Transformation"))
            {
                // Parse composite transformation (sequence of elementary transforms)
                var comp = new CompositeTransformation();
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                
                while (i < lines.Length)
                {
                    string inner = Clean(lines[i]);
                    // Checks if line is "}", closing the transformations and breaks while 
                    if (IsClosingBrace(inner)) { i++; break; } 
                    if (string.IsNullOrEmpty(inner)) { i++; continue; }

                    // Parse format: T x y z | Rx angle | Ry angle | Rz angle | S x y z
                    var tokens = inner.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0) { i++; continue; }
                    
                    switch (tokens[0])
                    {
                        // Adds a translation to the transformation
                        case "T":
                            comp.Elements.Add(TransformElement.Translation(new Vector3(
                                (float)ParseDouble(tokens[1]),
                                (float)ParseDouble(tokens[2]),
                                (float)ParseDouble(tokens[3]))));
                            break;
                        // Adds a scale to the transformation
                        case "S":
                            comp.Elements.Add(TransformElement.Scale(new Vector3(
                                (float)ParseDouble(tokens[1]),
                                (float)ParseDouble(tokens[2]),
                                (float)ParseDouble(tokens[3]))));
                            break;
                        // Adds a rotation in x axis to the transformation
                        case "Rx":
                            comp.Elements.Add(TransformElement.RotationX((float)ParseDouble(tokens[1])));
                            break;
                        // Adds a rotation in y axis to the transformation
                        case "Ry":
                            comp.Elements.Add(TransformElement.RotationY((float)ParseDouble(tokens[1])));
                            break;
                        // Adds a rotation in z axis to the transformation
                        case "Rz":
                            comp.Elements.Add(TransformElement.RotationZ((float)ParseDouble(tokens[1])));
                            break;
                    }
                    i++;
                }
                // Adds the transformation to list of transformations in scene
                scene.Transformations.Add(comp);
            }
            else if (IsSegment(line, "Camera"))
            {
                // Parse camera: transformation index, distance to projection plane, vertical FOV
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with transformation index
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                // Reads line with distance value
                double distance = ParseDouble(Clean(lines[i++]));
                // Reads line with field of view degree
                double fov = ParseDouble(Clean(lines[i++]));
                // Skips the line with "}"
                ExpectClosingBrace(lines, ref i);

                // Creates camera in scene 
                scene.Camera = new CameraSettings
                {
                    transformationIndex = tIndex,
                    distance = (float)distance,
                    verticalFovDeg = (float)fov
                };
            }
            else if (IsSegment(line, "Light"))
            {
                // Parse point light: transformation index and RGB color/intensity
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with transformation index
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                // Reads line with rgb values
                var rgb = ParseFloats(Clean(lines[i++]));
                // Skips the line with "}"
                ExpectClosingBrace(lines, ref i);

                // Adds new light to list of lights in scene
                scene.Lights.Add(new LightSource
                {
                    transformationIndex = tIndex,
                    rgb = new Color((float)rgb[0], (float)rgb[1], (float)rgb[2])
                });
            }
            else if (IsSegment(line, "Material"))
            {
                // Parse material: base color and coefficients (ambient, diffuse, specular, refraction, ior)
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with rgb values
                var col = ParseFloats(Clean(lines[i++]));
                // Reads line with coefficients values
                var coeffs = ParseFloats(Clean(lines[i++]));
                // Skips the line with "}"
                ExpectClosingBrace(lines, ref i);

                // Adds new material to list of materials in scene
                scene.Materials.Add(new MaterialDescription
                {
                    color = new Color((float)col[0], (float)col[1], (float)col[2]),
                    ambient = (float)coeffs[0],
                    diffuse = (float)coeffs[1],
                    specular = (float)coeffs[2],
                    refraction = (float)coeffs[3],
                    ior = (float)coeffs[4]
                });
            }
            else if (IsSegment(line, "Triangles"))
            {
                // Parse triangle mesh: transformation index followed by triangles
                // Each triangle: material index, then 3 vertex lines (x y z each)
                var mesh = new TrianglesMesh();
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with transformation index
                mesh.transformationIndex = (int)ParseDouble(Clean(lines[i++]));

                while (i < lines.Length)
                {
                    string inner = Clean(lines[i]);
                    // Checks if line is "}", closing the transformations and breaks while
                    if (IsClosingBrace(inner)) { i++; break; }
                    if (string.IsNullOrEmpty(inner)) { i++; continue; }

                    // Reads line with material index
                    int mat = (int)ParseDouble(inner);
                    // Reads next 3 lines with vertices coordinates
                    var v0 = ParseVector3(Clean(lines[i + 1]));
                    var v1 = ParseVector3(Clean(lines[i + 2]));
                    var v2 = ParseVector3(Clean(lines[i + 3]));
                    // Adds new triangle to mesh
                    mesh.Triangles.Add(new Triangle(mat, v0, v1, v2));
                    i += 4;
                }
                // Adds triangles mesh to list of Trinagle Meshes in scene
                scene.TriangleMeshes.Add(mesh);
            }
            else if (IsSegment(line, "Sphere"))
            {
                // Parse sphere primitive: transformation index and material index
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with transformation index
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                // Reads line with material index
                int mIndex = (int)ParseDouble(Clean(lines[i++]));
                // Skips the line with "}"
                ExpectClosingBrace(lines, ref i);
                // Adds new sphere to list of spheres in scene
                scene.Spheres.Add(new SphereDescription { transformationIndex = tIndex, materialIndex = mIndex });
            }
            else if (IsSegment(line, "Box"))
            {
                // Parse box primitive: transformation index and material index
                // Skips the line with "{"
                ExpectOpeningBrace(lines, ref i);
                // Reads line with transformation index
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                // Reads line with material index
                int mIndex = (int)ParseDouble(Clean(lines[i++]));
                // Skips the line with "}"
                ExpectClosingBrace(lines, ref i);
                // Adds new box to list of boxes in scene
                scene.Boxes.Add(new BoxDescription { transformationIndex = tIndex, materialIndex = mIndex });
            }
        }

        return scene;
    }

    /// <summary>
    /// Legacy wrapper that returns scene in a list for backward compatibility.
    /// </summary>
    public List<ObjectData> LoadSceneObjects(string filePath)
    {
        var scene = LoadScene(filePath);
        return new List<ObjectData> { scene };
    }

    #region Parsing Helpers
    
    /// <summary>
    /// Cleans a line by removing comments and trimming whitespace.
    /// </summary>
    private static string Clean(string line)
    {
        if (line == null) return string.Empty;
        
        // Remove everything after "//" comment marker
        int idx = line.IndexOf("//", StringComparison.Ordinal);
        if (idx >= 0) line = line.Substring(0, idx);
        
        return line.Trim();
    }

    /// <summary>
    /// Checks if a line matches a segment name (case-insensitive).
    /// </summary>
    private static bool IsSegment(string line, string name)
    {
        return string.Equals(line, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Advances to the next non-empty line and expects an opening brace.
    /// </summary>
    private static void ExpectOpeningBrace(string[] lines, ref int i)
    {
        while (i < lines.Length && string.IsNullOrEmpty(Clean(lines[i]))) i++;
        if (i >= lines.Length || Clean(lines[i]) != "{")
        {
            Debug.LogError("Expected '{' in scene file.");
        }
        i++;
    }

    /// <summary>
    /// Advances to the next non-empty line and expects a closing brace.
    /// </summary>
    private static void ExpectClosingBrace(string[] lines, ref int i)
    {
        while (i < lines.Length && string.IsNullOrEmpty(Clean(lines[i]))) i++;
        if (i >= lines.Length || Clean(lines[i]) != "}")
        {
            Debug.LogError("Expected '}' in scene file.");
        }
        i++;
    }

    private static bool IsClosingBrace(string line) => line == "}";

    /// <summary>
    /// Parses a numeric string using invariant culture (handles both . and scientific notation).
    /// </summary>
    private static double ParseDouble(string token)
    {
        return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a whitespace-separated list of numbers.
    /// </summary>
    private static List<double> ParseFloats(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<double>(parts.Length);
        foreach (var p in parts)
            list.Add(ParseDouble(p));
        return list;
    }

    /// <summary>
    /// Parses a line as a Vector3 (three space-separated floats).
    /// </summary>
    private static Vector3 ParseVector3(string line)
    {
        var v = ParseFloats(line);
        return new Vector3((float)v[0], (float)v[1], (float)v[2]);
    }
    
    #endregion
}
