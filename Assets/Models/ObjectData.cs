using System;
using UnityEngine;

namespace Models
{
    /// <summary>
    /// Serializable container for scene object properties used by the raytracer/systems.
    /// Kept minimal and engine-friendly (uses UnityEngine types) so it can be serialized
    /// in ScriptableObjects or JSON if needed.
    /// </summary>
    [Serializable]
    public class ObjectData
    {
        /// <summary>
        /// Unique identifier for this object (optional).
        /// </summary>
        public string Id;

        /// <summary>
        /// Friendly name for editors and debugging.
        /// </summary>
        public string Name;

        /// <summary>
        /// Local position in the scene.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// Local rotation as quaternion.
        /// </summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>
        /// Local scale.
        /// </summary>
        public Vector3 Scale = Vector3.one;

        /// <summary>
        /// The shape type for this object. Adjust in raytracing/systems to support.
        /// </summary>
        public ShapeType Shape = ShapeType.Mesh;

        /// <summary>
        /// Base color / albedo.
        /// </summary>
        public Color Albedo = Color.grey;

        /// <summary>
        /// Emission color (used if object is a light or emissive material).
        /// </summary>
        public Color Emission = Color.black;

        /// <summary>
        /// When true this object contributes as an emissive light source.
        /// </summary>
        public bool IsLight = false;

        /// <summary>
        /// Intensity multiplier when used as a light.
        /// </summary>
        public float Intensity = 1f;

        /// <summary>
        /// Generic metallic factor (0..1) for simple PBR-like material.
        /// </summary>
        [Range(0f, 1f)]
        public float Metallic = 0f;

        /// <summary>
        /// Generic smoothness/roughness factor (0..1).
        /// </summary>
        [Range(0f, 1f)]
        public float Smoothness = 0.5f;

        /// <summary>
        /// Optional path or GUID to a Mesh asset when Shape == Mesh.
        /// Keep empty if the runtime supplies geometry differently.
        /// </summary>
        public string MeshAssetPath;

        /// <summary>
        /// Optional user data or metadata in JSON form.
        /// </summary>
        public string Metadata;

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ObjectData() { }

        /// <summary>
        /// Convenience constructor to set core transform values.
        /// </summary>
        public ObjectData(string id, string name, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Id = id;
            Name = name;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    /// <summary>
    /// Fundamental supported shape types.
    /// Add more as the systems require (e.g. Sphere/Plane/Triangle).
    /// </summary>
    public enum ShapeType
    {
        Mesh = 0,
        Sphere = 1,
        Plane = 2,
        Triangle = 3,
        Custom = 100
    }
}

