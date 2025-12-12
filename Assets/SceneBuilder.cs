using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Unity component responsible for constructing and rendering the scene based on loaded data 
public class SceneBuilder : MonoBehaviour
{
    public Material baseMaterial; // Base material used as a template for object materials 
    private SceneService sceneService = new SceneService(); // Service instance to load scene data
    private RayTracer rayTracer = new RayTracer();
    private ObjectData scene; // Parsed scene 
    private UIDocument uiDocument;
    private Texture2D lastRenderedTexture;
    
    // UI Elements
    private Label lblElapsedTime;
    private ProgressBar progressBar;
    private Label lblProgressText;
    private Button btnStart;

    // --- UI Controls ---
    // Resolution
    private TextField txtResX, txtResY;
    // BG Color
    private Slider sldBgR, sldBgG, sldBgB;
    // Light
    private Slider sldLightIntensity;
    private TextField txtLightIntensity;
    // Toggles
    private Toggle togAmbient, togDiffuse, togSpecular, togRefraction;
    // Camera
    private Slider sldRotX, sldRotY, sldRotZ;
    private TextField txtRotX, txtRotY, txtRotZ; // Added for sync
    private TextField txtCamPosX, txtCamPosY, txtCamPosZ;
    private TextField txtCamFov;
    private Button btnOrthographic;
    // Renderer
    private TextField txtRecursionDepth;

    // State
    private bool isRendering = false;
    private bool isOrthographicMode = false; // For orthographic projection toggle
    private float sceneFov = 60f; // Store original FOV from scene for restoration
    private System.Diagnostics.Stopwatch stopwatch;
    private CancellationTokenSource cancellationTokenSource;
    private Coroutine gifPlaybackCoroutine;
    private List<Texture2D> gifFrames;

    public ComputeShader rayTracingShader;

    void OnEnable()
    {
        // initialize ray tracer shader
        if (rayTracingShader != null)
        {
             rayTracer.SetComputeShader(rayTracingShader);
        }
        uiDocument = FindFirstObjectByType<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("No UIDocument found in the scene.");
            return;
        }

        var root = uiDocument.rootVisualElement;

        btnStart = root.Q<Button>("btn-start-raytracing");
        if (btnStart != null) btnStart.clicked += OnStartRayTracingClicked;

        var btnLoad = root.Q<Button>("btn-load-data");
        if (btnLoad != null) btnLoad.clicked += OnLoadDataClicked;

        var btnSave = root.Q<Button>("btn-save-image");
        if (btnSave != null) btnSave.clicked += OnSaveImageClicked;

        var btnExit = root.Q<Button>("btn-exit");
        if (btnExit != null) btnExit.clicked += OnExitClicked;

        // Orthographic Projection Button
        btnOrthographic = root.Q<Button>("cam_orto");
        if (btnOrthographic != null) btnOrthographic.clicked += OnOrthographicClicked;

        // GIF Generator Button
        var btnGif = root.Q<Button>("gif");
        if (btnGif != null) btnGif.clicked += OnGifClicked;

        lblElapsedTime = root.Q<Label>("lbl-elapsed-time");
        progressBar = root.Q<ProgressBar>("progress-bar");
        lblProgressText = root.Q<Label>("lbl-progress-text");

        // --- Bind UI Elements ---
        
        // Resolution (Container name "Resolution")
        var resContainer = root.Q<VisualElement>("Resolution");
        if (resContainer != null)
        {
            var textFields = resContainer.Query<TextField>().ToList();
            if (textFields.Count >= 2)
            {
                txtResX = textFields[0]; // First one is X
                txtResY = textFields[1]; // Second one is Y
            }
        }

        // BG Color Sliders (Container "BG_Color")
        var bgContainer = root.Q<VisualElement>("BG_Color");
        if (bgContainer != null)
        {
            var sliders = bgContainer.Query<Slider>().ToList();
            if (sliders.Count >= 3)
            {
                sldBgR = sliders[0];
                sldBgG = sliders[1];
                sldBgB = sliders[2];
            }
        }
        
        // RGB Text Fields (Container "RGB") - sync with BG Color sliders
        var rgbContainer = root.Q<VisualElement>("RGB");
        TextField txtBgR = null, txtBgG = null, txtBgB = null;
        if (rgbContainer != null)
        {
            var textFields = rgbContainer.Query<TextField>().ToList();
            if (textFields.Count >= 3)
            {
                txtBgR = textFields[0];
                txtBgG = textFields[1];
                txtBgB = textFields[2];
            }
        }
        
        // Sync BG Color sliders <-> RGB text fields
        if (sldBgR != null && txtBgR != null)
        {
            sldBgR.RegisterValueChangedCallback(evt => txtBgR.value = Mathf.RoundToInt(evt.newValue * 2.55f).ToString());
            txtBgR.RegisterValueChangedCallback(evt => { if (int.TryParse(evt.newValue, out int v)) sldBgR.SetValueWithoutNotify(v / 2.55f); });
        }
        if (sldBgG != null && txtBgG != null)
        {
            sldBgG.RegisterValueChangedCallback(evt => txtBgG.value = Mathf.RoundToInt(evt.newValue * 2.55f).ToString());
            txtBgG.RegisterValueChangedCallback(evt => { if (int.TryParse(evt.newValue, out int v)) sldBgG.SetValueWithoutNotify(v / 2.55f); });
        }
        if (sldBgB != null && txtBgB != null)
        {
            sldBgB.RegisterValueChangedCallback(evt => txtBgB.value = Mathf.RoundToInt(evt.newValue * 2.55f).ToString());
            txtBgB.RegisterValueChangedCallback(evt => { if (int.TryParse(evt.newValue, out int v)) sldBgB.SetValueWithoutNotify(v / 2.55f); });
        }

        // Lighting Toggles ("Reflections" container)
        var reflectionsContainer = root.Q<VisualElement>("Reflections");
        if (reflectionsContainer != null)
        {
            // Order in UXML: Ambient, Diffuse, Specular, Refraction
            var toggles = reflectionsContainer.Query<Toggle>().ToList();
            if (toggles.Count >= 4)
            {
                togAmbient = toggles[0];
                togDiffuse = toggles[1];
                togSpecular = toggles[2];
                togRefraction = toggles[3];
            }
        }

        // Light Intensity ("Intensity" container)
        var intensityContainer = root.Q<VisualElement>("Intensity");
        if (intensityContainer != null)
        {
            sldLightIntensity = intensityContainer.Q<Slider>();
            txtLightIntensity = intensityContainer.Q<TextField>("Light_Intensity");
            
            // Sync light intensity slider <-> text field (slider 0-100, text 0-5)
            if (sldLightIntensity != null && txtLightIntensity != null)
            {
                sldLightIntensity.RegisterValueChangedCallback(evt => txtLightIntensity.value = (evt.newValue / 20f).ToString("F1"));
                txtLightIntensity.RegisterValueChangedCallback(evt => { if (float.TryParse(evt.newValue, out float v)) sldLightIntensity.SetValueWithoutNotify(v * 20f); });
            }
        }

        // Camera Axis Rotation ("Axis_Rotation")
        // Sliders for Rotation
        var axisRotContainer = root.Q<VisualElement>("Axis_Rotation");
        if (axisRotContainer != null)
        {
            var sliders = axisRotContainer.Query<Slider>().ToList();
            if (sliders.Count >= 3)
            {
                sldRotX = sliders[0];
                sldRotY = sliders[1];
                sldRotZ = sliders[2];
                
                // Set high-value to 360 for degrees
                sldRotX.highValue = 360;
                sldRotY.highValue = 360;
                sldRotZ.highValue = 360;
            }
        }

        // Camera Rotation Text Fields (in "Pos" container - confusing name in UXML but contains axis_text-x/y/z)
        // We will misuse the existing txtCamPosX/Y/Z variables for POSITION, so let's create new ones for ROTATION?
        // Wait, I need to declare them first. I'll add them to the class.
        // For now, let's assume I can add them here or I should add specific variables.
        // Let's declare them in the class structure in a separate edit if needed, or misuse existing if possible? No, bad practice.
        // I will use local variables for binding and then store them in class fields. I need to simple add definitions.
        
        var posContainer = root.Q<VisualElement>("Pos");
        if (posContainer != null)
        {
            var textFields = posContainer.Query<TextField>().ToList();
            if (textFields.Count >= 3)
            {
                txtRotX = textFields[0]; // axis_text-x
                txtRotY = textFields[1]; // axis_text-y
                txtRotZ = textFields[2]; // axis_text-z
            }
        }

        // Camera FOV and POSITION (in "FOV" container)
        var fovContainer = root.Q<VisualElement>("FOV");
        if (fovContainer != null)
        {
             var textFields = fovContainer.Query<TextField>().ToList();
             // 0: Fov, 1: Pos X, 2: Pos Y, 3: Pos Z
             if (textFields.Count >= 1) txtCamFov = textFields[0];
             if (textFields.Count >= 4)
             {
                 txtCamPosX = textFields[1];
                 txtCamPosY = textFields[2];
                 txtCamPosZ = textFields[3];
             }
        }
        
        // Sync Rotation Sliders <-> Text Fields
        if (sldRotX != null && txtRotX != null)
        {
            sldRotX.RegisterValueChangedCallback(evt => txtRotX.value = evt.newValue.ToString("F0"));
            txtRotX.RegisterValueChangedCallback(evt => { if (float.TryParse(evt.newValue, out float v)) sldRotX.SetValueWithoutNotify(v); });
        }
         if (sldRotY != null && txtRotY != null)
        {
            sldRotY.RegisterValueChangedCallback(evt => txtRotY.value = evt.newValue.ToString("F0"));
            txtRotY.RegisterValueChangedCallback(evt => { if (float.TryParse(evt.newValue, out float v)) sldRotY.SetValueWithoutNotify(v); });
        }
         if (sldRotZ != null && txtRotZ != null)
        {
            sldRotZ.RegisterValueChangedCallback(evt => txtRotZ.value = evt.newValue.ToString("F0"));
            txtRotZ.RegisterValueChangedCallback(evt => { if (float.TryParse(evt.newValue, out float v)) sldRotZ.SetValueWithoutNotify(v); });
        }

        // Renderer ("Renderer" container)
        var rendererContainer = root.Q<VisualElement>("Renderer");
        if (rendererContainer != null)
        {
            txtRecursionDepth = rendererContainer.Q<TextField>();
        }
    }

    // Populate UI from Scene Data
    private void UpdateUIFromScene(ObjectData data)
    {
        if (data == null) return;

        // Resolution
        if (txtResX != null && data.Image != null) txtResX.value = data.Image.horizontal.ToString();
        if (txtResY != null && data.Image != null) txtResY.value = data.Image.vertical.ToString();

        // BG Color (0-100 sliders)
        if (data.Image != null)
        {
            Color bg = data.Image.background;
            if (sldBgR != null) sldBgR.value = bg.r * 100f;
            if (sldBgG != null) sldBgG.value = bg.g * 100f;
            if (sldBgB != null) sldBgB.value = bg.b * 100f;
        }

        // Toggles - Enable all by default
        if (togAmbient != null) togAmbient.value = true;
        if (togDiffuse != null) togDiffuse.value = true;
        if (togSpecular != null) togSpecular.value = true;
        if (togRefraction != null) togRefraction.value = true;

        // Light Intensity - Default 1.0
        // Slider 0-100 maps to intensity 0-5, so slider=20 means intensity=1.0
        if (sldLightIntensity != null) sldLightIntensity.value = 20f;
        if (txtLightIntensity != null) txtLightIntensity.value = "1.0";

        // Camera Pos/Rot
        // Initialize with default 0
        float initRotX = 0, initRotY = 0, initRotZ = 0;
        float initPosX = 0, initPosY = 0, initPosZ = 0;

        // Try to calculate initial transform from scene camera transformation
        if (data.Camera != null && data.Transformations != null && data.Camera.transformationIndex >= 0)
        {
             var comp = data.Transformations[data.Camera.transformationIndex];
             Matrix4x4 mat = Matrix4x4.identity;
             foreach (var el in comp.Elements)
             {
                 switch (el.Type)
                 {
                     case TransformType.T: mat *= Matrix4x4.Translate(el.XYZ); break;
                     case TransformType.Rx: mat *= Matrix4x4.Rotate(Quaternion.Euler(el.AngleDeg, 0, 0)); break;
                     case TransformType.Ry: mat *= Matrix4x4.Rotate(Quaternion.Euler(0, el.AngleDeg, 0)); break;
                     case TransformType.Rz: mat *= Matrix4x4.Rotate(Quaternion.Euler(0, 0, el.AngleDeg)); break;
                     case TransformType.S: mat *= Matrix4x4.Scale(el.XYZ); break;
                 }
             }
             
             // Extract Rotation
             Vector3 euler = mat.rotation.eulerAngles;
             initRotX = euler.x;
             initRotY = euler.y;
             initRotZ = euler.z;
             
             // Extract Position
             Vector3 pos = mat.GetColumn(3);
             initPosX = pos.x;
             initPosY = pos.y;
             initPosZ = pos.z;
        }

        // Populate Position Text Fields
        if (txtCamPosX != null) txtCamPosX.value = initPosX.ToString("F1");
        if (txtCamPosY != null) txtCamPosY.value = initPosY.ToString("F1");
        if (txtCamPosZ != null) txtCamPosZ.value = initPosZ.ToString("F1");

        // Populate Rotation Controls
        if (sldRotX != null) sldRotX.value = initRotX;
        if (sldRotY != null) sldRotY.value = initRotY;
        if (sldRotZ != null) sldRotZ.value = initRotZ;
        if (txtRotX != null) txtRotX.value = initRotX.ToString("F0");
        if (txtRotY != null) txtRotY.value = initRotY.ToString("F0");
        if (txtRotZ != null) txtRotZ.value = initRotZ.ToString("F0");
        
        // FOV - populate from scene so user can tweak
        if (data.Camera != null)
        {
            sceneFov = data.Camera.verticalFovDeg; // Store for restoration
            if (txtCamFov != null) txtCamFov.value = sceneFov.ToString("F1");
        }

        // Recursion Depth - Default 2
        if (txtRecursionDepth != null) txtRecursionDepth.value = "2";
    }

    private RenderSettings GetRenderSettingsFromUI()
    {
        RenderSettings settings = new RenderSettings();

        // Resolution
        if (int.TryParse(txtResX?.value, out int rx) && int.TryParse(txtResY?.value, out int ry))
        {
            settings.ResolutionOverride = new Vector2Int(rx, ry);
        }

        // BG Color
        if (sldBgR != null && sldBgG != null && sldBgB != null)
        {
            settings.BackgroundColorOverride = new Color(sldBgR.value / 100f, sldBgG.value / 100f, sldBgB.value / 100f);
        }

        // Intensity
        // Prefer Text Field if valid, else Slider
        if (float.TryParse(txtLightIntensity?.value, out float li))
        {
            settings.LightIntensityScale = li;
        }
        else if (sldLightIntensity != null)
        {
             // Map slider 0-100 to 0-5?
             settings.LightIntensityScale = sldLightIntensity.value / 20f; // 100 -> 5
        }
        else settings.LightIntensityScale = 1.0f;

        // Toggles
        // Recursion
        if (int.TryParse(txtRecursionDepth?.value, out int depth))
        {
            settings.MaxDepth = depth;
        }
        else settings.MaxDepth = 2; // Default
        
        // Lighting toggles (default to true if not found)
        settings.EnableAmbient = togAmbient?.value ?? true;
        settings.EnableDiffuse = togDiffuse?.value ?? true;
        settings.EnableSpecular = togSpecular?.value ?? true;
        settings.EnableRefraction = togRefraction?.value ?? true;

        // Camera Position Overrideck if any value is set/valid to trigger override
        if (float.TryParse(txtCamPosX?.value, out float cx) && 
            float.TryParse(txtCamPosY?.value, out float cy) && 
            float.TryParse(txtCamPosZ?.value, out float cz))
        {
            settings.CameraPositionOverride = new Vector3(cx, cy, cz);
        }

        // Rotations 
        // Use values directly (0-360)
        // Check if any slider is non-zero
        if (sldRotX != null || sldRotY != null || sldRotZ != null)
        {
            float rotX = sldRotX != null ? sldRotX.value : 0;
            float rotY = sldRotY != null ? sldRotY.value : 0;
            float rotZ = sldRotZ != null ? sldRotZ.value : 0;
            
            if (rotX != 0 || rotY != 0 || rotZ != 0)
            {
                settings.CameraRotationOverride = new Vector3(rotX, rotY, rotZ);
            }
        }

        // FOV
        if (float.TryParse(txtCamFov?.value, out float fov))
        {
            settings.CameraFovOverride = fov;
        }

        // Projection mode
        settings.IsOrthographic = isOrthographicMode;

        return settings;
    }

    void Start()
    {
        // Load default scene initially but do not render
        string filePath = "Assets/Resources/Scenes/test_scene_1.txt"; 
        if (File.Exists(filePath))
        {
            scene = sceneService.LoadScene(filePath);
            LogSceneSummary(scene);
            UpdateUIFromScene(scene); // Populate UI initially
        }
    }

    void Update()
    {
        if (isRendering && stopwatch != null)
        {
            TimeSpan ts = stopwatch.Elapsed;
            if (lblElapsedTime != null)
            {
                if (ts.TotalSeconds < 10)
                    lblElapsedTime.text = $"Elapsed Time: {ts.TotalSeconds:F3}s";
                else if (ts.TotalMinutes < 1)
                    lblElapsedTime.text = $"Elapsed Time: {ts.Seconds}s";
                else if (ts.TotalHours < 1)
                    lblElapsedTime.text = $"Elapsed Time: {ts.Minutes}m {ts.Seconds}s";
                else
                    lblElapsedTime.text = $"Elapsed Time: {ts.Hours}H {ts.Minutes}m {ts.Seconds}s";
            }
        }
    }

    async void OnStartRayTracingClicked()
    {
        // Stop any existing GIF playback
        if (gifPlaybackCoroutine != null)
        {
            StopCoroutine(gifPlaybackCoroutine);
            gifPlaybackCoroutine = null;
        }

        if (isRendering)
        {
            // Cancel if already running? Or just ignore?
            // Let's implement cancel.
            cancellationTokenSource?.Cancel();
            return;
        }

        if (scene == null)
        {
            Debug.LogError("No scene loaded! Please load a scene first.");
            StartCoroutine(ShowToast("No scene loaded!"));
            return;
        }

        Debug.Log("Starting Ray Tracing...");
        isRendering = true;
        if (btnStart != null) btnStart.text = "Cancel";
        
        stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cancellationTokenSource = new CancellationTokenSource();

        // Progress reporter
        var progress = new Progress<float>(percent =>
        {
            if (progressBar != null) progressBar.value = percent * 100f;
            if (lblProgressText != null) lblProgressText.text = $"Progress {percent * 100f:F0}%";
        });

        try
        {
            // Gather Settings
            RenderSettings settings = GetRenderSettingsFromUI();
            
            var resultTexture = await rayTracer.RenderAsync(scene, settings, progress, cancellationTokenSource.Token);
            
            if (resultTexture == null)
            {
                Debug.LogWarning("Render returned null. Make sure the ComputeShader is assigned in the Inspector.");
                StartCoroutine(ShowToast("Render failed - check ComputeShader assignment!"));
                return;
            }
            
            lastRenderedTexture = resultTexture;
            
            DisplayTexture(lastRenderedTexture);
            Debug.Log("Ray tracing complete.");
            StartCoroutine(ShowToast("Rendering Complete!"));
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Ray tracing canceled.");
            StartCoroutine(ShowToast("Rendering Canceled."));
        }
        catch (Exception ex)
        {
            Debug.LogError($"Ray tracing failed: {ex}");
            StartCoroutine(ShowToast("Rendering Failed!"));
        }
        finally
        {
            isRendering = false;
            stopwatch.Stop();
            if (btnStart != null) btnStart.text = "Start Ray Tracing";
            cancellationTokenSource.Dispose();
            cancellationTokenSource = null;
        }
    }

    void OnLoadDataClicked()
    {
        if (isRendering) return;

#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Load Scene Data", "Assets/Resources/Scenes", "txt");
        if (!string.IsNullOrEmpty(path))
        {
            scene = sceneService.LoadScene(path);
            LogSceneSummary(scene);
            UpdateUIFromScene(scene); // Populate UI on Load
            Debug.Log($"Loaded scene from {path}");
            StartCoroutine(ShowToast($"Loaded: {Path.GetFileName(path)}"));
        }
#else
        Debug.LogWarning("File dialog is only supported in the Unity Editor.");
        StartCoroutine(ShowToast("File dialog not supported in build."));
#endif
    }

    void OnSaveImageClicked()
    {
        if (lastRenderedTexture == null)
        {
            Debug.LogWarning("No image to save. Render a scene first.");
            StartCoroutine(ShowToast("No image to save!"));
            return;
        }

        // Save to Output folder
        string fileName = $"render_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string outPath = Path.Combine("Assets/Output", fileName);
        
        RayTracer.SaveTexture(lastRenderedTexture, outPath);
        
        Debug.Log($"Saved image to {outPath}");
        StartCoroutine(ShowToast($"Image saved to {fileName}"));
        
#if UNITY_EDITOR
        AssetDatabase.Refresh(); // Refresh assets to show the new file
#endif
    }

    void OnOrthographicClicked()
    {
        // Toggle orthographic mode
        isOrthographicMode = !isOrthographicMode;
        
        // Update button appearance to show state
        if (btnOrthographic != null)
        {
            btnOrthographic.style.backgroundColor = isOrthographicMode 
                ? new StyleColor(new Color(0.3f, 0.7f, 0.3f)) // Green when active
                : new StyleColor(StyleKeyword.Initial);
        }
        
        // Update FOV: 120 for orthographic, restore scene FOV otherwise
        if (txtCamFov != null)
        {
            txtCamFov.value = isOrthographicMode ? "120" : sceneFov.ToString("F1");
        }
        
        Debug.Log($"[SceneBuilder] Orthographic Projection: {(isOrthographicMode ? "ON" : "OFF")}");
    }

    async void OnGifClicked()
    {
        if (scene == null)
        {
            Debug.LogWarning("[SceneBuilder] No scene loaded. Load data first.");
            StartCoroutine(ShowToast("No scene loaded!"));
            return;
        }

        if (isRendering)
        {
            Debug.LogWarning("[SceneBuilder] Already rendering.");
            return;
        }

        isRendering = true;
        btnStart.SetEnabled(false);
        
        cancellationTokenSource = new CancellationTokenSource();
        stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        // Get current render settings as base
        RenderSettings baseSettings = GetRenderSettingsFromUI();
        
        // Store original rotation text for later restoration
        string origRotZ = txtRotZ?.value ?? "0";

        try
        {
            Debug.Log("[SceneBuilder] Starting GIF generation (360° rotation)...");
            
            GifGenerator gifGen = new GifGenerator(rayTracer, scene);
            
            List<Texture2D> frames = await gifGen.GenerateRotationFrames(
                baseSettings,
                (progress, status) =>
                {
                    // Update UI on main thread
                    if (progressBar != null) progressBar.value = progress * 100f;
                    if (lblProgressText != null) lblProgressText.text = status;
                },
                cancellationTokenSource.Token
            );

            stopwatch.Stop();
            
            if (frames.Count > 0)
        {
            // Save GIF with progress reporting
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string gifPath = $"Assets/Output/rotation_{timestamp}.gif";
            
            // Ensure output directory exists
            if (!Directory.Exists("Assets/Output"))
            {
                Directory.CreateDirectory("Assets/Output");
            }
            
            // Use async version with progress for encoding phase
            await gifGen.SaveGifAsync(frames, gifPath, 
                (progress, status) =>
                {
                    if (progressBar != null) progressBar.value = progress * 100f;
                    if (lblProgressText != null) lblProgressText.text = status;
                }, 
                15); // 15 centiseconds per frame = ~6.7 fps
                
                // Display the last frame (or first frame)
                if (frames.Count > 0)
                {
                    gifFrames = frames;
                    lastRenderedTexture = frames[0];
                    DisplayTexture(lastRenderedTexture);
                    
                    // Start playback
                    if (gifPlaybackCoroutine != null) StopCoroutine(gifPlaybackCoroutine);
                    gifPlaybackCoroutine = StartCoroutine(PlayGifLoop(0.15f)); // 15cs delay
                }
                
                StartCoroutine(ShowToast($"GIF saved! {frames.Count} frames - {gifPath}"));
                Debug.Log($"[SceneBuilder] GIF generated: {frames.Count} frames in {stopwatch.Elapsed.TotalSeconds:F2}s");
                
                if (lblElapsedTime != null)
                {
                    lblElapsedTime.text = $"GIF: {stopwatch.Elapsed.TotalSeconds:F2}s ({frames.Count} frames)";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SceneBuilder] GIF generation failed: {ex.Message}");
            StartCoroutine(ShowToast("GIF generation failed!"));
        }
        finally
        {
            // Restore original rotation
            if (txtRotZ != null) txtRotZ.value = origRotZ;
            if (sldRotZ != null && float.TryParse(origRotZ, out float rotZ)) sldRotZ.value = rotZ;
            
            isRendering = false;
            btnStart.SetEnabled(true);
            if (progressBar != null) progressBar.value = 100f;
            if (lblProgressText != null) lblProgressText.text = "GIF Complete!";
        }
    }

    void OnExitClicked()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void DisplayTexture(Texture2D tex)
    {
        if (uiDocument == null) return;
        
        // Get the container for the ray-traced image
        var container = uiDocument.rootVisualElement.Q<VisualElement>("ray-traced-image");
        if (container != null)
        {
            // Check if we already have an Image element
            var image = container.Q<UnityEngine.UIElements.Image>();
            if (image == null)
            {
                // Create a new UI Toolkit Image element if none exists
                image = new UnityEngine.UIElements.Image();
                
                // Set image to scale to fit the container while maintaining aspect ratio
                image.style.width = new StyleLength(Length.Percent(100));
                image.style.height = new StyleLength(Length.Percent(100));
                image.scaleMode = ScaleMode.ScaleToFit;
                
                // Add the image to the container
                container.Add(image);
            }
            
            // Convert the Texture2D to a Sprite
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            image.sprite = sprite;
            
            // Debug.Log("Displayed render on UI Toolkit VisualElement."); 
        }
        else
        {
            Debug.LogWarning("Could not find 'ray-traced-image' VisualElement in the UI Document.");
        }
    }

    IEnumerator PlayGifLoop(float delay)
    {
        if (gifFrames == null || gifFrames.Count == 0) yield break;

        int index = 0;
        while (true)
        {
            if (gifFrames.Count == 0) break;
            
            index = (index + 1) % gifFrames.Count;
            DisplayTexture(gifFrames[index]);
            yield return new WaitForSeconds(delay);
        }
    }

    IEnumerator ShowToast(string message)
    {
        if (uiDocument == null) yield break;
        var root = uiDocument.rootVisualElement;
        
        var label = new Label(message);
        label.style.position = Position.Absolute;
        label.style.bottom = 50;
        label.style.alignSelf = Align.Center;
        label.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.9f));
        label.style.color = Color.white;
        label.style.paddingTop = 10;
        label.style.paddingBottom = 10;
        label.style.paddingLeft = 20;
        label.style.paddingRight = 20;
        label.style.borderTopLeftRadius = 8;
        label.style.borderTopRightRadius = 8;
        label.style.borderBottomLeftRadius = 8;
        label.style.borderBottomRightRadius = 8;
        label.style.fontSize = 14;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        
        root.Add(label);
        
        yield return new WaitForSeconds(3f);
        
        root.Remove(label);
    }

    // Debug logging of parsed content
    void LogSceneSummary(ObjectData data)
    {
        Debug.Log($"Image: {data.Image?.horizontal}x{data.Image?.vertical} bg={data.Image?.background}");
        Debug.Log($"Transformations: {data.Transformations.Count}");
        if (data.Transformations.Count > 0)
        {
            var first = data.Transformations[0];
            Debug.Log($"  T[0] elements: {first.Elements.Count}");
        }
        if (data.Camera != null)
        {
            Debug.Log($"Camera: t={data.Camera.transformationIndex} dist={data.Camera.distance} vfov={data.Camera.verticalFovDeg}");
        }
        Debug.Log($"Lights: {data.Lights.Count}");
        Debug.Log($"Materials: {data.Materials.Count}");
        Debug.Log($"TriangleMeshes: {data.TriangleMeshes.Count}");
        Debug.Log($"Spheres: {data.Spheres.Count}");
        Debug.Log($"Boxes: {data.Boxes.Count}");
    }
}
