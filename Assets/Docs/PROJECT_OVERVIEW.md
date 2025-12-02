# COSIG Ray Tracing Project Overview

This document summarizes the current project structure, scene models, parsing logic, ray tracing pipeline, and constraints. It is intended to onboard contributors (human or LLM) quickly and capture decisions made in this session.

## Goals

- Parse a brace-based scene description file and render an image via CPU ray tracing (no Unity primitive instantiation).
- Support composite transformations, materials, light(s), spheres, boxes, and triangle meshes.

## Project Structure (key files)

- `Assets/Models/ObjectData.cs`
  - Defines the scene model used throughout parsing and rendering:
    - `ObjectData`
      - `ImageSettings Image`
      - `List<CompositeTransformation> Transformations`
      - `CameraSettings Camera`
      - `List<LightSource> Lights`
      - `List<MaterialDescription> Materials`
      - `List<TrianglesMesh> TriangleMeshes`
      - `List<SphereDescription> Spheres`
      - `List<BoxDescription> Boxes`
    - `ImageSettings` (horizontal, vertical, background `Color`)
    - `CompositeTransformation` (ordered `List<TransformElement>`)
    - `TransformType` enum: `T`, `Rx`, `Ry`, `Rz`, `S`
    - `TransformElement` (either `XYZ` for `T`/`S` or `AngleDeg` for rotations)
    - `CameraSettings` (transformationIndex, distance, verticalFovDeg)
    - `LightSource` (transformationIndex, `Color` rgb)
    - `MaterialDescription` (color, ambient, diffuse, specular, refraction, ior)
    - `TrianglesMesh` (transformationIndex, `List<Triangle>`)
    - `Triangle` (materialIndex, v0, v1, v2)
    - `SphereDescription` (transformationIndex, materialIndex)
    - `BoxDescription` (transformationIndex, materialIndex)

- `Assets/Services/SceneService.cs`
  - Parses the scene description file (e.g., `Assets/Resources/Scenes/test_scene_1.txt`).
  - Segments (order-agnostic), each with braces:
    - `Image { horiz vert; r g b }`
    - `Transformation { T x y z | Rx a | Ry a | Rz a | S x y z ... }`
    - `Camera { transformIndex; distance; verticalFov }`
    - `Light { transformIndex; r g b }`
    - `Material { r g b; ambient diffuse specular refraction ior }`
    - `Triangles { transformIndex; [material; v0; v1; v2]... }`
    - `Sphere { transformIndex; material }`
    - `Box { transformIndex; material }`
  - `LoadScene(filePath)` returns a populated `ObjectData`.
  - `LoadSceneObjects(filePath)` retained as a thin compatibility wrapper (returns list with one `ObjectData`).

- `Assets/Services/RayTracer.cs`
  - CPU ray tracer producing a `Texture2D` (and optional PNG) from `ObjectData`.
  - Responsibilities:
    - Build matrices from `CompositeTransformation` in the listed order (pre-multiply each op).
    - Camera and image plane:
      - Camera is at `(0,0,distance)`, looking toward `-Z`, up `(0,1,0)`.
      - Vertical FOV determines plane size: `halfHeight = distance * tan(vFOV/2)`, `width = height * aspect`.
      - For pixel (x,y), map to camera-space ray direction `(u, v, -distance)`, then normalized.
    - **Acceleration Structure (BVH)**:
      - Before rendering, a **Bounding Volume Hierarchy (BVH)** is built from all scene objects.
      - This replaces the $O(N)$ linear search with an $O(\log N)$ hierarchical traversal.
      - The `BuildBVH` method converts `ObjectData` into a tree of `BVHNode`s containing `IHittable` objects (`SphereInstance`, `BoxInstance`, `TriangleInstance`).
    - Intersections:
      - All intersections are handled via the `IHittable` interface and the BVH tree.
      - `SphereInstance`: Handles object-space intersection for unit spheres using quadratic solution with epsilon checks.
      - `BoxInstance`: Handles object-space intersection for unit cubes using the Slabs algorithm with epsilon checks.
      - `TriangleInstance`: Handles world-space intersection for triangles using barycentric coordinates with watertightness checks (epsilon).
    - Shading (simple): ambient + diffuse (Lambert) + specular (Blinn–Phong). No shadows yet.
    - Output: returns `Texture2D`. Helper saves PNG via `RayTracer.SaveTexture()`.
    - Targeted debug logs for camera matrix, center-pixel ray, sample object positions, and closest hit distances by type.

- `Assets/Services/BVH/`
  - Contains the core components for the Bounding Volume Hierarchy system.
  - `AABB.cs`: Defines the Axis-Aligned Bounding Box struct with efficient ray-box intersection logic.
  - `IHittable.cs`: Interface for all objects that can be hit by a ray (`Hit` method and `GetBoundingBox`).
  - `BVHNode.cs`: Represents a node in the BVH tree. It recursively splits objects (currently random axis split) to build the hierarchy and traverses children during intersection.
  - `HittableObjects.cs`: Wrappers for scene primitives (`SphereInstance`, `BoxInstance`, `TriangleInstance`) that implement `IHittable`.

- `Assets/SceneBuilder.cs`
  - Entry point MonoBehaviour for running the pipeline.
  - On Start:
    - Loads scene via `SceneService.LoadScene()`.
    - Logs a summary of parsed content.
    - Calls `RayTracer.Render(scene)` and saves to `Assets/Output/render.png`.
    - If a `UIDocument` exists, displays the rendered texture on a VisualElement named `ray-traced-image`.
  - Note: Previously instantiated Unity primitives; now switched to purely ray-traced image output.

## Scene File Semantics (from spec and sample)

- Segment order is arbitrary.
- First index = 0 for materials and transformations.
- Triangle front faces are defined with CCW vertex order.
- Camera semantic: The spec states the camera transform affects the scene (the scene is positioned relative to a static camera located at `(0,0,distance)` facing `-Z`).

## Current Assumptions and Open Questions

- Camera transform usage:
  - We currently compute `sceneMat` from the camera composite (direct). Depending on the exact convention, the inverse may be required (i.e., placing the scene differently relative to the camera). Logging is in place to validate which interpretation puts geometry in front of the camera.
- Triangle intersection is performed in world-space (robust with non-uniform transforms). Sphere/box use object-space intersections.
- Materials: Only ambient/diffuse/specular used for lighting; refraction and ior are parsed but not yet used.
- Lights: Each `Light` uses its transform index; only point-light style diffuse/specular is used (no attenuation yet).

## Debugging Aids

- On render start, logs:
  - Camera transform index, distance, FOV.
  - Camera/scene matrix.
  - Sample object placements (Sphere[0], Box[0] centers).
  - Transformed z-values for the first triangle of the first mesh.
  - Center pixel ray and closest hit distances per type.
- These help decide whether to apply the camera composite directly or inversely and verify frustum coverage.

## Constraints and Practices

- Do not instantiate Unity primitives for final output; the project’s goal is to ray trace pixels.
- Keep scene parsing resilient to arbitrary segment order; tolerate empty transformations.
- Use Unity types (`Color`, `Vector3`, `Matrix4x4`, `Texture2D`) for convenience, but perform the rendering in CPU code.
- Keep logs concise and targeted to avoid console spam.

## Known Issues / Next Steps

- Determine the correct global camera transform usage (direct vs. inverse) based on frustum checks; the current code includes diagnostics to guide this.
- Add hard shadows: cast shadow rays toward each light and test for occlusion.
- Add support for multiple lights with attenuation.
- Implement reflection/refraction (use `specular`, `refraction`, `ior`).
  - **[COMPLETED]** Acceleration structures (BVH) for large triangle meshes.
  - **[COMPLETED]** Rigorous intersection logic (Barycentric, Quadratic, Slabs) with epsilon for precision.
  - **[COMPLETED]** Geometric transformations (World/Object space conversions) for rays and normals.
- Tone mapping / gamma correction if needed.

## Quick Start (Runtime)

1. Place a scene file at `Assets/Resources/Scenes/test_scene_1.txt` (sample is present).
2. Add `SceneBuilder` to a GameObject in a scene and press Play.
3. Inspect Unity Console for [RT] logs.
4. Find the rendered image at `Assets/Output/render.png`.
5. Optional: Add a `UIDocument` with a VisualElement named `ray-traced-image` to see the texture in Play Mode directly.

## Acceleration Structure Implementation (BVH)

To optimize rendering performance, especially for scenes with many triangles, a Bounding Volume Hierarchy (BVH) is implemented.

1.  **Primitives Wrapper**:
    - Raw data (`SphereDescription`, `BoxDescription`, `Triangle`) is wrapped into `SphereInstance`, `BoxInstance`, and `TriangleInstance`.
    - These classes implement `IHittable` and handle their own AABB calculation and intersection logic.
    - `SphereInstance` and `BoxInstance` store the inverse transformation matrix to perform intersection in object space (unit sphere/box), then transform the hit point and normal back to world space.
    - **Intersection Logic**:
        - **Triangles**: Uses barycentric coordinates ($\alpha, \beta, \gamma$) with $\epsilon = 1e-6$ to ensure watertightness ($\beta > -\epsilon, \gamma > -\epsilon, \beta+\gamma < 1+\epsilon$). Interpolates vertex positions for $P'$.
        - **Spheres**: Solves quadratic equation, selecting the closest positive root $> \epsilon$.
        - **Boxes**: Uses Slabs algorithm (axis-aligned planes) with $\epsilon$ checks.
    - **Transformations**:
        - Ray transformed to object space via $T^{-1}$.
        - $t$ distance calculated in world space to preserve depth ordering.
        - Normals transformed to world space via $(T^{-1})^T$.

2.  **Hierarchy Construction (`BVHNode`)**:
    - The tree is built recursively top-down.
    - At each node, the list of objects is split into two children (`left` and `right`).
    - **Splitting Strategy**: Currently, a random axis (X, Y, or Z) is chosen. The objects are sorted by their bounding box minimum coordinate along that axis, and the list is split in half (median split).
    - Leaf nodes contain 1 or 2 objects.
    - Each node stores an `AABB` that encapsulates all its children.

3.  **Traversal**:
    - The `Hit` method checks intersection with the node's AABB first.
    - If the box is hit, it recursively checks `left` and `right` children.
    - It returns the closest hit (smallest `t`) among children.

## Design Decisions Summary

- Models mirror the exact scene specification and are serializable.
- Parser populates a single `ObjectData` root representing the entire scene.
- Ray tracer operates in world space with explicit transforms; triangles are intersected in WS for safety under non-uniform transforms.
- **BVH Integration**: The ray tracer now builds a BVH tree before rendering. This abstracts the intersection logic behind the `IHittable` interface, allowing the main loop to simply call `root.Hit()`. This significantly improves performance for scenes with many objects.
- No Unity primitives are used in the final pipeline—only a generated image.
