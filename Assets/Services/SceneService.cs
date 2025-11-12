using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Service responsible for loading and interpreting data from a scene description file (brace-based format)
public class SceneService
{
    // New API: parse the full scene into an ObjectData instance
    public ObjectData LoadScene(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"File not found at {filePath}");
            return new ObjectData();
        }

        var scene = new ObjectData();
        var lines = File.ReadAllLines(filePath);
        int i = 0;

        while (i < lines.Length)
        {
            string line = Clean(lines[i]);
            i++;
            if (string.IsNullOrEmpty(line)) continue;

            if (IsSegment(line, "Image"))
            {
                ExpectOpeningBrace(lines, ref i);
                // horizontal vertical
                var res = ParseFloats(Clean(lines[i++]));
                // background r g b
                var bg = ParseFloats(Clean(lines[i++]));
                ExpectClosingBrace(lines, ref i);

                scene.Image = new ImageSettings
                {
                    horizontal = (int)res[0],
                    vertical = (int)res[1],
                    background = new Color((float)bg[0], (float)bg[1], (float)bg[2])
                };
            }
            else if (IsSegment(line, "Transformation"))
            {
                var comp = new CompositeTransformation();
                ExpectOpeningBrace(lines, ref i);
                while (i < lines.Length)
                {
                    string inner = Clean(lines[i]);
                    if (IsClosingBrace(inner)) { i++; break; }
                    if (string.IsNullOrEmpty(inner)) { i++; continue; }

                    // Parse elementary transform: T x y z | Rx a | Ry a | Rz a | S x y z
                    var tokens = inner.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0) { i++; continue; }
                    switch (tokens[0])
                    {
                        case "T":
                            comp.Elements.Add(TransformElement.Translation(new Vector3(
                                (float)ParseDouble(tokens[1]),
                                (float)ParseDouble(tokens[2]),
                                (float)ParseDouble(tokens[3]))));
                            break;
                        case "S":
                            comp.Elements.Add(TransformElement.Scale(new Vector3(
                                (float)ParseDouble(tokens[1]),
                                (float)ParseDouble(tokens[2]),
                                (float)ParseDouble(tokens[3]))));
                            break;
                        case "Rx":
                            comp.Elements.Add(TransformElement.RotationX((float)ParseDouble(tokens[1])));
                            break;
                        case "Ry":
                            comp.Elements.Add(TransformElement.RotationY((float)ParseDouble(tokens[1])));
                            break;
                        case "Rz":
                            comp.Elements.Add(TransformElement.RotationZ((float)ParseDouble(tokens[1])));
                            break;
                    }
                    i++;
                }
                scene.Transformations.Add(comp);
            }
            else if (IsSegment(line, "Camera"))
            {
                ExpectOpeningBrace(lines, ref i);
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                double distance = ParseDouble(Clean(lines[i++]));
                double fov = ParseDouble(Clean(lines[i++]));
                ExpectClosingBrace(lines, ref i);

                scene.Camera = new CameraSettings
                {
                    transformationIndex = tIndex,
                    distance = (float)distance,
                    verticalFovDeg = (float)fov
                };
            }
            else if (IsSegment(line, "Light"))
            {
                ExpectOpeningBrace(lines, ref i);
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                var rgb = ParseFloats(Clean(lines[i++]));
                ExpectClosingBrace(lines, ref i);

                scene.Lights.Add(new LightSource
                {
                    transformationIndex = tIndex,
                    rgb = new Color((float)rgb[0], (float)rgb[1], (float)rgb[2])
                });
            }
            else if (IsSegment(line, "Material"))
            {
                ExpectOpeningBrace(lines, ref i);
                var col = ParseFloats(Clean(lines[i++]));
                var coeffs = ParseFloats(Clean(lines[i++]));
                ExpectClosingBrace(lines, ref i);

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
                var mesh = new TrianglesMesh();
                ExpectOpeningBrace(lines, ref i);
                // transformation index
                mesh.transformationIndex = (int)ParseDouble(Clean(lines[i++]));

                while (i < lines.Length)
                {
                    string inner = Clean(lines[i]);
                    if (IsClosingBrace(inner)) { i++; break; }
                    if (string.IsNullOrEmpty(inner)) { i++; continue; }

                    // material index
                    int mat = (int)ParseDouble(inner);
                    // next 3 lines are vertices
                    var v0 = ParseVector3(Clean(lines[i + 1]));
                    var v1 = ParseVector3(Clean(lines[i + 2]));
                    var v2 = ParseVector3(Clean(lines[i + 3]));
                    mesh.Triangles.Add(new Triangle(mat, v0, v1, v2));
                    i += 4;
                }
                scene.TriangleMeshes.Add(mesh);
            }
            else if (IsSegment(line, "Sphere"))
            {
                ExpectOpeningBrace(lines, ref i);
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                int mIndex = (int)ParseDouble(Clean(lines[i++]));
                ExpectClosingBrace(lines, ref i);
                scene.Spheres.Add(new SphereDescription { transformationIndex = tIndex, materialIndex = mIndex });
            }
            else if (IsSegment(line, "Box"))
            {
                ExpectOpeningBrace(lines, ref i);
                int tIndex = (int)ParseDouble(Clean(lines[i++]));
                int mIndex = (int)ParseDouble(Clean(lines[i++]));
                ExpectClosingBrace(lines, ref i);
                scene.Boxes.Add(new BoxDescription { transformationIndex = tIndex, materialIndex = mIndex });
            }
        }

        return scene;
    }

    // Backward-compatible wrapper returning a single-element list
    public List<ObjectData> LoadSceneObjects(string filePath)
    {
        var scene = LoadScene(filePath);
        return new List<ObjectData> { scene };
    }

    // Helpers
    private static string Clean(string line)
    {
        if (line == null) return string.Empty;
        // Remove comments after //
        int idx = line.IndexOf("//", StringComparison.Ordinal);
        if (idx >= 0) line = line.Substring(0, idx);
        return line.Trim();
    }

    private static bool IsSegment(string line, string name)
    {
        return string.Equals(line, name, StringComparison.OrdinalIgnoreCase);
    }

    private static void ExpectOpeningBrace(string[] lines, ref int i)
    {
        // advance to next non-empty line and expect "{"
        while (i < lines.Length && string.IsNullOrEmpty(Clean(lines[i]))) i++;
        if (i >= lines.Length || Clean(lines[i]) != "{")
        {
            Debug.LogError("Expected '{' in scene file.");
        }
        i++;
    }

    private static void ExpectClosingBrace(string[] lines, ref int i)
    {
        // advance to next non-empty line and expect "}"
        while (i < lines.Length && string.IsNullOrEmpty(Clean(lines[i]))) i++;
        if (i >= lines.Length || Clean(lines[i]) != "}")
        {
            Debug.LogError("Expected '}' in scene file.");
        }
        i++;
    }

    private static bool IsClosingBrace(string line) => line == "}";

    private static double ParseDouble(string token)
    {
        return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static List<double> ParseFloats(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<double>(parts.Length);
        foreach (var p in parts)
            list.Add(ParseDouble(p));
        return list;
    }

    private static Vector3 ParseVector3(string line)
    {
        var v = ParseFloats(line);
        return new Vector3((float)v[0], (float)v[1], (float)v[2]);
    }
}
