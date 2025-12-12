# COSIG Ray Tracing Project Overview

This document summarizes the current project structure, scene models, parsing logic, **GPU-accelerated ray tracing pipeline**, and constraints. It is intended to onboard contributors (human or LLM) quickly and capture decisions made during development.

## Goals

- Parse a brace-based scene description file and render an image via **GPU compute shader ray tracing**.
- Support composite transformations, materials, light(s), spheres, boxes, and triangle meshes.
- Achieve real-time or near-real-time rendering performance through GPU acceleration.
- Support full recursive ray tracing: shadows, reflections, and refractions.

## Architecture Overview

The project uses a **hybrid CPU-GPU pipeline**:

1. **CPU**: Scene parsing, BVH construction, and UI management
2. **GPU**: Ray tracing (intersection, shading, shadows, reflections, refractions)

```
Scene File → SceneService → ObjectData → SceneGeometryConverter → GPU Triangles
                                ↓
                           BVHBuilder → GPU BVH Nodes
                                ↓
                        RayTracer (Compute Shader) → Rendered Texture
```

## Project Structure (Key Files)

### Models

- `Assets/Models/ObjectData.cs`
  - Defines the scene model used throughout parsing and rendering:
    - `ObjectData`: Root container with Image, Transformations, Camera, Lights, Materials, TriangleMeshes, Spheres, Boxes
    - `ImageSettings`: Resolution (horizontal, vertical) and background `Color`
    - `CompositeTransformation`: Ordered list of `TransformElement` operations
    - `TransformType` enum: `T` (translate), `Rx`, `Ry`, `Rz` (rotate), `S` (scale)
    - `CameraSettings`: transformationIndex, distance, verticalFovDeg
    - `LightSource`: transformationIndex, RGB color
    - `MaterialDescription`: color, ambient, diffuse, specular, refraction, ior
    - `TrianglesMesh`: transformationIndex, list of triangles
    - `Triangle`: materialIndex, v0, v1, v2
    - `SphereDescription`, `BoxDescription`: transformationIndex, materialIndex

- `Assets/Models/RenderSettings.cs`
  - Settings struct passed from UI to renderer:
    - Resolution, Background Color, Light Intensity
    - Camera Position/Rotation/FOV overrides
    - MaxDepth (recursion limit)
    - EnableAmbient, EnableDiffuse, EnableSpecular, EnableRefraction toggles
    - IsOrthographic projection mode

### Services

- `Assets/Services/SceneService.cs`
  - Parses scene description files (e.g., `Assets/Resources/Scenes/eval_scene.txt`)
  - Segments (order-agnostic): Image, Transformation, Camera, Light, Material, Triangles, Sphere, Box
  - `LoadScene(filePath)` returns a populated `ObjectData`

- `Assets/Services/RayTracer.cs` ⭐ **GPU Ray Tracer**
  - **GPU-accelerated ray tracer** using Unity Compute Shaders
  - Key responsibilities:
    - Setup GPU buffers (BVH nodes, triangles, materials)
    - Configure shader uniforms (camera, lighting, toggles)
    - Dispatch compute shader and read back results
  - `RenderAsync(scene, settings, progress, token)`: Main async render method
  - `SetComputeShader(shader)`: Assigns the compute shader at runtime
  - `RebuildBVH(scene, sceneMat)`: Triggers BVH rebuild with camera transform
  - `SetupMaterialBuffer(scene, kernel)`: Uploads material data to GPU

- `Assets/Services/SceneGeometryConverter.cs`
  - Converts scene primitives to GPU-compatible triangle format
  - `ExtractTriangles(scene, sceneMat)`: Returns `List<GPUTriangle>` in camera space
  - Handles spheres (tessellated UV sphere, 24×16 resolution with **smooth vertex normals**)
  - Handles boxes (6 faces, 12 triangles)
  - Handles triangle meshes from scene data

- `Assets/Services/GifGenerator.cs`
  - Generates 360° rotation GIF animations
  - `GenerateRotationFrames(settings, progress, token)`: Renders 36 frames (10° increments)
  - `SaveGifAsync(frames, path, progress, delay)`: **Parallel LZW compression** for fast encoding
  - Uses multi-threaded pixel conversion and compression

### BVH System

- `Assets/Services/BVH/BVHBuilder.cs`
  - **GPU BVH Data Structures**:
    - `GPUTriangle`: v0, v1, v2, n0, n1, n2 (vertex normals for smooth shading), center, materialIndex (88 bytes)
    - `GPUBVHNode`: AABB min/max, leftOrFirst, count (32 bytes, cache-aligned)
    - `GPUMaterial`: color, ambient, diffuse, specular, refraction, ior (32 bytes)
  - `Build(triangles)`: Constructs flattened BVH from triangle list
  - Uses median split on random axis for balanced tree

- `Assets/Services/BVH/` (Legacy CPU components, retained for reference)
  - `AABB.cs`, `IHittable.cs`, `BVHNode.cs`, `HittableObjects.cs`

### Shaders

- `Assets/Shaders/BVHRayTracing.compute` ⭐ **Main Compute Shader**
  - **Kernel**: `CSMain` (8×8 thread groups)
  - **Features**:
    - Iterative BVH traversal (stack-based, max depth 64)
    - Möller-Trumbore triangle intersection
    - **Smooth normal interpolation** using barycentric coordinates
    - **Shadow rays** with epsilon bias (1e-2) for acne prevention
    - **Recursive reflections** (specular materials)
    - **Recursive refractions** with Snell's law and total internal reflection
    - **Blinn-Phong specular highlights**
    - Perspective and **orthographic** projection modes
    - Debug visualization modes (depth, normals, hit/miss)
  - **Uniforms**:
    - `_CameraDistance`, `_CameraFOV`, `_CameraToWorld`, `_CameraInverseProjection`
    - `_MaxDepth`, `_DebugMode`
    - `_EnableAmbient`, `_EnableDiffuse`, `_EnableSpecular`, `_EnableRefraction`
    - `_LightIntensity`, `_LightPosition`
    - `_BackgroundColor`
    - `_IsOrthographic`, `_OrthoSize`

### UI

- `Assets/SceneBuilder.cs`
  - Main MonoBehaviour entry point
  - Wires UI controls to `RenderSettings`
  - Handles scene loading, render triggering, and GIF generation
  - Manages progress bar and elapsed time display

- `Assets/GUIs/gui_raytracing.uxml`
  - UI Toolkit layout with controls for all render settings

## Rendering Pipeline

### 1. Scene Loading
```
SceneService.LoadScene() → ObjectData
```

### 2. Geometry Preparation (CPU)
```
SceneGeometryConverter.ExtractTriangles(scene, cameraMatrix)
  ├── Transform all geometry into camera space
  ├── Tessellate spheres with smooth vertex normals
  ├── Convert boxes to triangles
  └── Return List<GPUTriangle>
```

### 3. BVH Construction (CPU)
```
BVHBuilder.Build(triangles)
  ├── Sort by center position
  ├── Recursive median split
  └── Return flattened node array + triangle array
```

### 4. GPU Upload
```
RayTracer.RebuildBVH()
  ├── ComputeBuffer bvhBuffer (32 bytes/node)
  ├── ComputeBuffer triangleBuffer (88 bytes/tri)
  └── ComputeBuffer materialBuffer (32 bytes/mat)
```

### 5. Ray Tracing (GPU)
```
BVHRayTracing.compute CSMain
  ├── Generate ray (perspective or orthographic)
  ├── For each depth level:
  │   ├── Traverse BVH iteratively
  │   ├── Find closest intersection
  │   ├── Calculate local shading (ambient + diffuse + specular)
  │   ├── Cast shadow ray
  │   └── Spawn reflection/refraction ray
  └── Write final color to RenderTexture
```

### 6. Readback (CPU)
```
Texture2D.ReadPixels() → Display in UI / Save to file
```

## Key Features

### Lighting Model
- ✅ **Ambient reflection**: `color * kAmbient`
- ✅ **Diffuse reflection**: Lambert model with shadow testing
- ✅ **Specular highlights**: Blinn-Phong model
- ✅ **Shadows**: Shadow rays with epsilon bias to prevent self-shadowing
- ✅ **Recursive reflections**: Mirror-like reflections for specular materials
- ✅ **Recursive refractions**: Snell's law with IOR, handles total internal reflection

### Projection Modes
- ✅ **Perspective**: Standard pinhole camera model
- ✅ **Orthographic**: Parallel rays, constant viewing direction

### Smooth Shading
- Spheres use **per-vertex normals** (vertex position normalized)
- Triangle intersection **interpolates normals** using barycentric coordinates
- Proper normal transformation for non-uniform scaling: `(M⁻¹)ᵀ * n`

### Performance Optimizations
- **GPU Compute Shader**: Massively parallel ray tracing
- **BVH Acceleration**: O(log N) intersection vs O(N) linear search
- **Cache-aligned structs**: 32-byte GPU nodes for optimal memory access
- **Parallel GIF encoding**: Multi-threaded LZW compression

### UI Controls
- Resolution (X, Y)
- Background Color (RGB sliders)
- Light Intensity (0-5 scale)
- Lighting Toggles (Ambient, Diffuse, Specular, Refraction)
- Camera Position (X, Y, Z)
- Camera Rotation (X, Y, Z sliders, 0-360°)
- Camera FOV (default 50°)
- Recursion Depth
- Orthographic projection toggle
- Progress bar with elapsed time

## Scene File Format

```
Image { width height; r g b }
Transformation { T x y z | Rx angle | Ry angle | Rz angle | S x y z ... }
Camera { transformIndex; distance; fov }
Light { transformIndex; r g b }
Material { r g b; ambient diffuse specular refraction ior }
Triangles { transformIndex; [materialIndex; v0x v0y v0z; v1x v1y v1z; v2x v2y v2z]... }
Sphere { transformIndex; materialIndex }
Box { transformIndex; materialIndex }
```

- Segment order is arbitrary
- Indices are 0-based
- Triangle winding is CCW for front faces
- Colors are 0-1 range

## Quick Start

1. Open the Unity project
2. Enter Play Mode
3. Click **Load Data** to parse a scene file
4. Adjust settings in the UI panel
5. Click **Start Ray Tracing** to render
6. View the result in the preview panel
7. Find saved images in `Assets/Output/`

### GIF Generation
1. Configure desired settings
2. Click the **GIF** button in the menu bar
3. Wait for 36 frames to render and encode
4. GIF is saved to `Assets/Output/rotation_[timestamp].gif`

## Dependencies

- **Unity 2021+** with Compute Shader support
- **UI Toolkit** for the interface
- No external packages required