# COSIG Ray Tracing Project Overview

This document summarizes the current project structure, scene models, parsing logic, **GPU-accelerated ray tracing pipeline**, distributed ray tracing effects, and UI features. It is intended to onboard contributors (human or LLM) quickly and capture decisions made during development.

## Goals

- Parse a brace-based scene description file and render an image via **GPU compute shader ray tracing**.
- Support composite transformations, materials, light(s), spheres, boxes, and triangle meshes.
- Achieve real-time or near-real-time rendering performance through GPU acceleration.
- Support full recursive ray tracing: shadows, reflections, and refractions.
- Implement **Distributed Ray Tracing** effects: soft shadows, glossy reflections, and motion blur.
- Provide a comprehensive UI with real-time and static rendering modes.

## Architecture Overview

The project uses a **hybrid CPU-GPU pipeline**:

1. **CPU**: Scene parsing, BVH construction, UI management, and preset serialization
2. **GPU**: Ray tracing (intersection, shading, shadows, reflections, refractions, DRT effects)

```
Scene File → SceneService → ObjectData → SceneGeometryConverter → GPU Triangles
                                ↓
                           BVHBuilder → GPU BVH Nodes
                                ↓
                        RayTracer (Compute Shader) → Rendered Texture
                                ↓
                           UI Display / GIF Export / PNG Save
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
    - **Anti-Aliasing**: AASamples (1, 2, 4, 8)
    - **Distributed Ray Tracing**:
      - EnableSoftShadows, LightSize (area light radius)
      - EnableGlossy, SurfaceRoughness (reflection blur)
      - EnableMotionBlur, ShutterSpeed (camera shake intensity)

- `Assets/Models/ScenePreset.cs`
  - Serializable preset for saving/loading complete scene configurations:
    - Scene file path and reference image path
    - All render settings (resolution, colors, camera, lighting toggles)
    - Top bar settings (AA, shadows, glossy, blur modes)
    - Preset metadata (name, save timestamp)

### Services

- `Assets/Services/SceneService.cs`
  - Parses scene description files (e.g., `Assets/Resources/Scenes/eval_scene.txt`)
  - Segments (order-agnostic): Image, Transformation, Camera, Light, Material, Triangles, Sphere, Box
  - `LoadScene(filePath)` returns a populated `ObjectData`

- `Assets/Services/RayTracer.cs` ⭐ **GPU Ray Tracer**
  - **GPU-accelerated ray tracer** using Unity Compute Shaders
  - **Static BVH in Object Space**: Built once and cached until geometry changes
  - Key responsibilities:
    - Setup GPU buffers (BVH nodes, triangles, materials)
    - Configure shader uniforms (camera, lighting, toggles, DRT parameters)
    - Dispatch compute shader and read back results
  - `RenderAsync(scene, settings, progress, token)`: High-quality async render for static mode
  - `RenderToTexture(scene, settings)`: Fast synchronous render for real-time mode
  - `SetComputeShader(shader)`: Assigns the compute shader at runtime
  - `RebuildBVH(scene)`: Triggers BVH rebuild in **Object Space** (only when geometry changes)
  - `ClearRenderTarget()`: Clears cached render texture for mode transitions
  - `SetupMaterialBuffer(scene, kernel)`: Uploads material data to GPU

- `Assets/Services/SceneGeometryConverter.cs`
  - Converts scene primitives to GPU-compatible triangle format
  - `ExtractTriangles(scene)`: Returns `List<GPUTriangle>` in **Object Space** (object transforms only, no camera transform)
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
  - Uses median split on longest axis for balanced tree

- `Assets/Services/BVH/` (Supporting components)
  - `AABB.cs`: Axis-aligned bounding box with slab intersection
  - `IHittable.cs`: Interface for ray-intersectable objects
  - `BVHNode.cs`: CPU recursive BVH node implementation
  - `HittableObjects.cs`: Sphere, Box, Triangle primitive implementations

### Shaders

- `Assets/Shaders/BVHRayTracing.compute` ⭐ **Main Compute Shader**
  - **Kernel**: `CSMain` (8×8 thread groups)
  - **Core Features**:
    - Iterative BVH traversal (stack-based, max depth 32)
    - Möller-Trumbore triangle intersection
    - **Smooth normal interpolation** using barycentric coordinates
    - **Shadow rays** with epsilon bias (1e-2) for acne prevention
    - **Recursive reflections** (specular materials)
    - **Recursive refractions** with Snell's law and total internal reflection
    - **Blinn-Phong specular highlights**
    - Perspective and **orthographic** projection modes
    - Debug visualization modes (depth, normals, hit/miss)
  - **Anti-Aliasing**:
    - Stratified jittered sampling (N×N grid per pixel)
    - Configurable sample count (1, 2, 4, 8)
  - **Distributed Ray Tracing Effects**:
    - **Soft Shadows**: Jittered area light sampling with configurable light size
    - **Glossy Reflections**: Perturbed reflection vectors based on surface roughness
    - **Motion Blur**: Camera shake simulation via jittered ray origins
  - **Helper Functions**:
    - `Hash22`, `Hash33`: Pseudo-random number generation for jittering
    - `RandomUnitVector`: Uniform sphere sampling for DRT effects
  - **Uniforms**:
    - Camera: `_CameraDistance`, `_CameraFOV`, `_CameraToWorld`, `_CameraInverseProjection`
    - Rendering: `_MaxDepth`, `_DebugMode`, `_AASamples`
    - Lighting: `_EnableAmbient`, `_EnableDiffuse`, `_EnableSpecular`, `_EnableRefraction`
    - Light: `_LightIntensity`, `_LightPosition`
    - Background: `_BackgroundColor`
    - Projection: `_IsOrthographic`, `_OrthoSize`
    - DRT: `_EnableSoftShadows`, `_LightSize`, `_EnableGlossy`, `_SurfaceRoughness`, `_EnableMotionBlur`, `_ShutterSpeed`

### UI

- `Assets/SceneBuilder.cs`
  - Main MonoBehaviour entry point
  - Wires UI controls to `RenderSettings`
  - Handles scene loading, render triggering, mode switching, and GIF generation
  - Manages progress bar, elapsed time display, and toast notifications
  - Supports **Save/Load Scene Presets** (JSON serialization)
  - Implements **Real-time Mode** with live preview updates

- `Assets/GUIs/gui_raytracing.uxml`
  - UI Toolkit layout with controls for all render settings
  - Top menu bar with action buttons and DRT effect toggles

- `Assets/GUIs/ui_style_raytracing.uss`
  - Custom styling including Space Grotesk font integration

## Rendering Pipeline

### 1. Scene Loading
```
SceneService.LoadScene() → ObjectData
```

### 2. Geometry Preparation (CPU) - **OBJECT SPACE**
```
SceneGeometryConverter.ExtractTriangles(scene)
  ├── Transform geometry to Object Space (object transforms only, no camera)
  ├── Tessellate spheres with smooth vertex normals
  ├── Convert boxes to triangles
  └── Return List<GPUTriangle> in Object Space (enables static BVH)
```

### 3. BVH Construction (CPU) - **STATIC & CACHED**
```
BVHBuilder.Build(triangles)
  ├── Sort by center position
  ├── Recursive median split on longest axis
  ├── Return flattened node array + triangle array
  └── BVH is CACHED (NOT rebuilt when camera moves!)
```

### 4. GPU Upload
```
RayTracer.RebuildBVH() (only when geometry changes)
  ├── ComputeBuffer bvhBuffer (32 bytes/node)
  ├── ComputeBuffer triangleBuffer (88 bytes/tri)
  └── ComputeBuffer materialBuffer (32 bytes/mat)
```

### 5. Ray Tracing (GPU) - **SCENE FILE SEMANTICS RESPECTED**
```
SCENE FILE SEMANTICS:
  - Camera is FIXED at (0, 0, distance), looking toward -Z
  - M_scene (camera transformation) would be applied to GEOMETRY

IMPLEMENTED OPTIMIZATION (mathematically equivalent):
  - Geometry in Object Space (no M_scene) → Static BVH
  - Rays transformed by M_scene^(-1) → Camera Space → Object Space
  
BVHRayTracing.compute CSMain:
  ├── For each AA sample:
  │   ├── Generate ray with jittered offset (stratified sampling)
  │   ├── Apply Motion Blur (jitter camera position)
  │   ├── Transform ray from Camera Space to Object Space
  │   ├── For each depth level:
  │   │   ├── Traverse BVH iteratively (Object Space)
  │   │   ├── Find closest intersection (Object Space)
  │   │   ├── Calculate local shading (ambient + diffuse + specular)
  │   │   ├── Cast shadow ray with Soft Shadow jitter
  │   │   └── Spawn reflection ray with Glossy perturbation
  │   └── Accumulate sample color
  └── Write averaged color to RenderTexture
```

### 6. Readback / Display
```
Real-time Mode: Direct RenderTexture display in UI
Static Mode: Texture2D.ReadPixels() → Display in UI / Save to file
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

### Anti-Aliasing
- ✅ **Stratified Jittered Sampling**: Reduces aliasing with sub-pixel ray distribution
- ✅ **Configurable Sample Count**: 1x (off), 2x, 4x, 8x via toolbar button

### Distributed Ray Tracing
- ✅ **Soft Shadows**: Area light simulation with jittered shadow rays
  - Configurable light sizes: Hard, 5.0, 10.0, 20.0
  - Creates realistic penumbra effects
- ✅ **Glossy Reflections**: Rough surface simulation
  - Perturbed reflection vectors based on surface roughness (0.05)
  - Creates frosted/blurry reflections
- ✅ **Motion Blur**: Camera shake simulation
  - Configurable shutter speeds: Off, 0.5, 1.0, 2.0
  - Jittered camera origin per sample

### Smooth Shading
- Spheres use **per-vertex normals** (vertex position normalized)
- Triangle intersection **interpolates normals** using barycentric coordinates
- Proper normal transformation for non-uniform scaling: `(M⁻¹)ᵀ * n`

### Performance Optimizations
- **GPU Compute Shader**: Massively parallel ray tracing
- **BVH Acceleration**: O(log N) intersection vs O(N) linear search
- **Static BVH Caching**: No rebuild when only camera changes
- **Cache-aligned structs**: 32-byte GPU nodes for optimal memory access
- **Parallel GIF encoding**: Multi-threaded LZW compression

### Rendering Modes
- ✅ **Static Mode**: High-quality single-frame rendering with progress indication
- ✅ **Real-time Mode**: Live preview with FPS counter, instant UI feedback

### UI Controls

#### Top Menu Bar
- **File I/O**: Load Data, Save Image, Load Image
- **Scene Presets**: Save Scene, Load Scene (JSON with all settings)
- **Actions**: Start Ray Tracing (static render), Exit
- **Mode Toggle**: Static/Realtime mode switch
- **GIF**: Generate 360° rotation animation
- **About**: Credits popup

#### Quality Settings (Top Bar Buttons)
- **AA**: Anti-aliasing samples (1x, 2x, 4x, 8x)
- **Shadows**: Shadow mode (Hard, 5.0, 10.0, 20.0 soft)
- **Glossy**: Glossy reflections toggle (On/Off)
- **Blur**: Motion blur intensity (Off, 0.5, 1.0, 2.0)

#### Side Panel Controls
- **Resolution**: Output size (X, Y)
- **Background Color**: RGB sliders (0-100) with text field sync
- **Light Intensity**: Slider (0-5 scale) with text field sync
- **Lighting Toggles**: Ambient, Diffuse, Specular, Refraction
- **Camera Position**: X, Y, Z text fields
- **Camera Rotation**: X, Y, Z sliders (0-360°) with text field sync
- **Camera FOV**: Field of view (degrees)
- **Orthographic**: Projection mode toggle button
- **Recursion Depth**: Max ray bounces

#### Progress Indicators
- Progress bar with percentage
- Elapsed time display (or FPS in real-time mode)
- Toast notifications for user feedback

## Scene Preset System

Scene presets save the complete application state to JSON files:

```json
{
  "SceneFilePath": "Assets/Resources/Scenes/scene.txt",
  "ReferenceImagePath": null,
  "ResolutionX": 512,
  "ResolutionY": 512,
  "BackgroundColor": [0.2, 0.2, 0.2],
  "LightIntensity": 1.0,
  "CameraPosition": [0, 0, 30],
  "CameraRotation": [0, 45, 0],
  "CameraFov": 50,
  "IsOrthographic": false,
  "RecursionDepth": 3,
  "EnableAmbient": true,
  "EnableDiffuse": true,
  "EnableSpecular": true,
  "EnableRefraction": true,
  "AASamples": 4,
  "ShadowMode": 2,
  "EnableGlossy": true,
  "BlurMode": 0,
  "PresetName": "my_preset",
  "SavedAt": "2024-12-14 17:00:00"
}
```

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
5. Click **Start Ray Tracing** to render (or enable **Realtime** mode for live preview)
6. View the result in the preview panel
7. Click **Save Image** to export (PNG or GIF)

### GIF Generation
1. Configure desired settings
2. Click the **GIF** button in the menu bar
3. Wait for 36 frames to render and encode
4. GIF is saved to `Assets/Output/rotation_[timestamp].gif`

### Using Scene Presets
1. Configure your desired render settings
2. Click **Save Scene** to export a JSON preset
3. Later, click **Load Scene** to restore all settings
4. The preset includes the scene file path, camera settings, and all top bar options

## Code Documentation

All source code is fully documented in English with:

- **XML documentation** for all public classes, methods, and properties
- **Inline comments** explaining algorithms and design decisions
- **Section headers** organizing code into logical blocks

### Core Files Documentation:

| File | Purpose |
|------|---------|
| `RayTracer.cs` | GPU ray tracer orchestration, BVH caching, camera transformations, DRT uniforms |
| `BVHRayTracing.compute` | Compute shader with BVH traversal, AA, DRT effects, shading |
| `SceneGeometryConverter.cs` | Converts primitives to GPU triangles with smooth normals |
| `BVHBuilder.cs` | Median-split BVH construction with flattening for GPU |
| `GifGenerator.cs` | Animated GIF export with parallel LZW compression |
| `SceneService.cs` | Scene file parsing |
| `ObjectData.cs` | Scene data structures |
| `RenderSettings.cs` | UI-to-renderer settings container (includes DRT settings) |
| `ScenePreset.cs` | JSON-serializable preset for save/load functionality |
| `SceneBuilder.cs` | Main UI controller, mode management, preset I/O |

## Dependencies

- **Unity 2021+** with Compute Shader support
- **UI Toolkit** for the interface
- **Space Grotesk** font (included)
- No external packages required