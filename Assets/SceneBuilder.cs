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
    
    // DRT Buttons
    private Button btnShadows, btnGlossy, btnBlur;

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

    // Mode Toggle
    private Button btnModeToggle;
    private bool isRealtimeMode = false;

    // AA Toggle
    private Button btnAA;
    private int currentAASamples = 1;

    // DRT Toggles state

    // Shadows: 0=Hard, 1=5.0, 2=10.0, 3=20.0 (Increased for visibility)
    private int shadowMode = 0;
    private float[] shadowSizes = new float[] { 0f, 5.0f, 10.0f, 20.0f };

    // Glossy: boolean is fine for now
    private bool isGlossy = false;

    // Blur: 0=Off, 1=0.5, 2=1.0, 3=2.0
    private int blurMode = 0;
    private float[] blurSpeeds = new float[] { 0f, 0.5f, 1.0f, 2.0f };

    // State
    private bool isRendering = false;
    private bool isOrthographicMode = false; // For orthographic projection toggle
    private float sceneFov = 60f; // Store original FOV from scene for restoration
    private System.Diagnostics.Stopwatch stopwatch;
    private CancellationTokenSource cancellationTokenSource;
    private Coroutine gifPlaybackCoroutine;
    private List<Texture2D> gifFrames;

    // File paths for scene preset save/load
    private string loadedSceneFilePath = null;
    private string loadedReferenceImagePath = null;

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

        // Load Image Button
        var btnLoadImage = root.Q<Button>("btn-load-image");
        if (btnLoadImage != null) btnLoadImage.clicked += OnLoadImageClicked;

        // Orthographic Projection Button
        btnOrthographic = root.Q<Button>("cam_orto");
        if (btnOrthographic != null) btnOrthographic.clicked += OnOrthographicClicked;

        // Mode Toggle Button
        btnModeToggle = root.Q<Button>("btn-mode-toggle");
        if (btnModeToggle != null) btnModeToggle.clicked += OnModeToggleClicked;

        // Save/Load Scene Preset Buttons
        var btnSaveScene = root.Q<Button>("btn-save-scene");
        if (btnSaveScene != null) btnSaveScene.clicked += OnSaveSceneClicked;
        
        var btnLoadScene = root.Q<Button>("btn-load-scene");
        if (btnLoadScene != null) btnLoadScene.clicked += OnLoadSceneClicked;

        // GIF Generator Button
        var btnGif = root.Q<Button>("gif");
        if (btnGif != null) btnGif.clicked += OnGifClicked;

        // About Button
        var btnAbout = root.Q<Button>("about");
        if (btnAbout != null) btnAbout.clicked += OnAboutClicked;

        // AA Button
        btnAA = root.Q<Button>("btn-aa-toggle");
        if (btnAA != null) btnAA.clicked += OnAAToggleClicked;

        // DRT Buttons
        btnShadows = root.Q<Button>("btn-shadow-toggle");
        if (btnShadows != null) btnShadows.clicked += OnShadowToggleClicked;

        btnGlossy = root.Q<Button>("btn-glossy-toggle");
        if (btnGlossy != null) btnGlossy.clicked += OnGlossyToggleClicked;

        btnBlur = root.Q<Button>("btn-blur-toggle");
        if (btnBlur != null) btnBlur.clicked += OnBlurToggleClicked;

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

        // Lighting Toggles (by name)
        togAmbient = root.Q<Toggle>("tog-ambient");
        togDiffuse = root.Q<Toggle>("tog-diffuse");
        togSpecular = root.Q<Toggle>("tog-specular");
        togRefraction = root.Q<Toggle>("tog-refraction");

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

        // Camera Position Override - always apply if valid text fields
        if (float.TryParse(txtCamPosX?.value, out float cx) && 
            float.TryParse(txtCamPosY?.value, out float cy) && 
            float.TryParse(txtCamPosZ?.value, out float cz))
        {
            settings.CameraPositionOverride = new Vector3(cx, cy, cz);
        }

        // Camera Rotation Override - always apply if sliders exist (even for 0,0,0)
        if (sldRotX != null && sldRotY != null && sldRotZ != null)
        {
            float rotX = sldRotX.value;
            float rotY = sldRotY.value;
            float rotZ = sldRotZ.value;
            settings.CameraRotationOverride = new Vector3(rotX, rotY, rotZ);
        }

        // FOV
        if (float.TryParse(txtCamFov?.value, out float fov))
        {
            settings.CameraFovOverride = fov;
        }

        // Projection mode
        settings.IsOrthographic = isOrthographicMode;

        // Quality Settings
        settings.AASamples = currentAASamples;

        // DRT Settings
        settings.EnableSoftShadows = shadowMode > 0;
        settings.LightSize = shadowSizes[shadowMode];

        settings.EnableGlossy = isGlossy;
        settings.SurfaceRoughness = 0.05f;

        settings.EnableMotionBlur = blurMode > 0;
        settings.ShutterSpeed = blurSpeeds[blurMode];

        return settings;
    }

    void Start()
    {
        // Load default scene initially but do not render
        string filePath = "Assets/Resources/Scenes/test_scene_1.txt"; 
        if (File.Exists(filePath))
        {
            scene = sceneService.LoadScene(filePath);
            loadedSceneFilePath = filePath; // Track for scene preset saving
            UpdateUIFromScene(scene); // Populate UI initially
        }
    }

    void Update()
    {
        // Update elapsed time display during static rendering
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

        // Real-time rendering loop
        if (isRealtimeMode && scene != null && !isRendering)
        {
            RenderSettings settings = GetRenderSettingsFromUI();
            RenderTexture rt = rayTracer.RenderToTexture(scene, settings);
            
            if (rt != null)
            {
                DisplayRenderTexture(rt);
                
                // Update FPS display
                if (lblElapsedTime != null)
                {
                    float fps = 1f / Time.deltaTime;
                    lblElapsedTime.text = $"Realtime: {fps:F1} FPS";
                }
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
            StartCoroutine(ShowToast("Rendering Complete!"));
        }
        catch (OperationCanceledException)
        {
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
            loadedSceneFilePath = path; // Track for scene preset saving
            UpdateUIFromScene(scene); // Populate UI on Load
            StartCoroutine(ShowToast($"Loaded: {Path.GetFileName(path)}"));
        }
#else
        Debug.LogWarning("File dialog is only supported in the Unity Editor.");
        StartCoroutine(ShowToast("File dialog not supported in build."));
#endif
    }

    async void OnSaveImageClicked()
    {
        if (lastRenderedTexture == null && (gifFrames == null || gifFrames.Count == 0))
        {
            Debug.LogWarning("No image to save. Render a scene first.");
            StartCoroutine(ShowToast("No image to save!"));
            return;
        }

#if UNITY_EDITOR
        // Determine if we have GIF frames or just a single image
        bool hasGifFrames = gifFrames != null && gifFrames.Count > 1;
        
        string defaultName = hasGifFrames 
            ? $"animation_{System.DateTime.Now:yyyyMMdd_HHmmss}" 
            : $"render_{System.DateTime.Now:yyyyMMdd_HHmmss}";
        
        string filter = hasGifFrames ? "GIF file;*.gif;PNG file;*.png" : "PNG file;*.png;GIF file;*.gif";
        
        string path = EditorUtility.SaveFilePanel(
            "Save Image",
            "Assets/Output",
            defaultName,
            hasGifFrames ? "gif" : "png"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            string extension = Path.GetExtension(path).ToLower();
            
            if (extension == ".gif" && hasGifFrames)
            {
                // Save as GIF
                var gifGen = new GifGenerator(rayTracer, scene);
                
                await gifGen.SaveGifAsync(gifFrames, path, 
                    (progress, status) =>
                    {
                        if (progressBar != null) progressBar.value = progress * 100f;
                        if (lblProgressText != null) lblProgressText.text = status;
                    }, 
                    15); // 15 centiseconds per frame
                
                StartCoroutine(ShowToast($"GIF saved: {Path.GetFileName(path)}"));
            }
            else
            {
                // Save as PNG (single frame)
                Texture2D texToSave = lastRenderedTexture ?? (gifFrames?.Count > 0 ? gifFrames[0] : null);
                if (texToSave != null)
                {
                    // Ensure it ends with .png
                    if (extension != ".png")
                        path = Path.ChangeExtension(path, ".png");
                    
                    RayTracer.SaveTexture(texToSave, path);
                    StartCoroutine(ShowToast($"Image saved: {Path.GetFileName(path)}"));
                }
            }
        }
#else
        Debug.LogWarning("File dialog is only supported in the Unity Editor.");
        StartCoroutine(ShowToast("File dialog not supported in build."));
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
    }

    void OnModeToggleClicked()
    {
        isRealtimeMode = !isRealtimeMode;
        
        // Clear render target to ensure fresh state when switching modes
        rayTracer.ClearRenderTarget();
        
        // Update button text and appearance
        if (btnModeToggle != null)
        {
            btnModeToggle.text = isRealtimeMode ? "Mode: Realtime" : "Mode: Static";
            btnModeToggle.style.backgroundColor = isRealtimeMode 
                ? new StyleColor(new Color(0.2f, 0.6f, 0.9f)) // Blue when realtime
                : new StyleColor(StyleKeyword.Initial);
        }
        
        // Disable Start button in realtime mode (rendering is continuous)
        if (btnStart != null)
        {
            btnStart.SetEnabled(!isRealtimeMode);
            btnStart.style.opacity = isRealtimeMode ? 0.5f : 1f;
        }
        
        // Show toast notification
        if (isRealtimeMode && scene == null)
        {
            StartCoroutine(ShowToast("Load a scene first!"));
            isRealtimeMode = false;
            if (btnModeToggle != null)
            {
                btnModeToggle.text = "Mode: Static";
                btnModeToggle.style.backgroundColor = new StyleColor(StyleKeyword.Initial);
            }
            if (btnStart != null)
            {
                btnStart.SetEnabled(true);
                btnStart.style.opacity = 1f;
            }
            return;
        }
        
        // When switching to static mode, clear the displayed image to show it needs re-rendering
        if (!isRealtimeMode)
        {
            var container = uiDocument?.rootVisualElement?.Q<VisualElement>("ray-traced-image");
            if (container != null)
            {
                container.style.backgroundImage = StyleKeyword.None;
                container.MarkDirtyRepaint();
            }
        }
        
        StartCoroutine(ShowToast(isRealtimeMode ? "Realtime mode enabled" : "Static mode enabled"));
    }

    void OnAAToggleClicked()
    {
        // Cycle AA samples: 1 -> 2 -> 4 -> 8 -> 1
        if (currentAASamples == 1) currentAASamples = 2;
        else if (currentAASamples == 2) currentAASamples = 4;
        else if (currentAASamples == 4) currentAASamples = 8;
        else currentAASamples = 1;

        if (btnAA != null) btnAA.text = $"AA: {currentAASamples}x";
        
        // If in realtime mode, clearing render target helps visualize the change immediately if we were accumulating
        // But here we do per-frame sampling, so it just updates naturally.
        // However, a toast is nice.
        StartCoroutine(ShowToast($"Anti-Aliasing: {currentAASamples}x"));
    }

    void OnShadowToggleClicked()
    {
        shadowMode = (shadowMode + 1) % shadowSizes.Length;
        float val = shadowSizes[shadowMode];
        
        if (btnShadows != null) 
            btnShadows.text = shadowMode == 0 ? "Shadows: Hard" : $"Shadows: {val:0.0}";
            
        StartCoroutine(ShowToast(shadowMode == 0 ? "Hard Shadows" : $"Soft Shadow Size: {val}"));
    }

    void OnGlossyToggleClicked()
    {
        isGlossy = !isGlossy;
        if (btnGlossy != null) btnGlossy.text = isGlossy ? "Glossy: On" : "Glossy: Off";
        StartCoroutine(ShowToast(isGlossy ? "Glossy Reflections Enabled" : "Glossy Reflections Disabled"));
    }

    void OnBlurToggleClicked()
    {
        blurMode = (blurMode + 1) % blurSpeeds.Length;
        float val = blurSpeeds[blurMode];
        
        if (btnBlur != null) 
            btnBlur.text = blurMode == 0 ? "Blur: Off" : $"Blur: {val:0.0}";
            
        StartCoroutine(ShowToast(blurMode == 0 ? "Motion Blur Disabled" : $"Shutter Speed: {val}"));
    }

    /// <summary>
    /// Displays a RenderTexture directly in the UI (no CPU copy needed).
    /// Used by real-time rendering for maximum performance.
    /// </summary>
    void DisplayRenderTexture(RenderTexture rt)
    {
        if (uiDocument == null || rt == null) return;
        
        var container = uiDocument.rootVisualElement.Q<VisualElement>("ray-traced-image");
        if (container != null)
        {
            // Set the RenderTexture as background image
            container.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            container.style.unityBackgroundImageTintColor = Color.white;
            
            // ScaleToFit maintains aspect ratio
            #pragma warning disable 0618
            container.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            #pragma warning restore 0618
            
            container.style.flexGrow = 1;
            container.style.width = StyleKeyword.Auto;
            container.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
        }
    }

    void OnAboutClicked()
    {
        var root = uiDocument.rootVisualElement;
        
        // Check if popup already exists
        var existingPopup = root.Q<VisualElement>("about-popup");
        if (existingPopup != null)
        {
            root.Remove(existingPopup);
            return;
        }
        
        // Create overlay background
        var overlay = new VisualElement();
        overlay.name = "about-popup";
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.right = 0;
        overlay.style.top = 0;
        overlay.style.bottom = 0;
        overlay.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.7f));
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;
        
        // Create popup card
        var popup = new VisualElement();
        popup.style.backgroundColor = new StyleColor(new Color(0.16f, 0.18f, 0.22f));
        popup.style.borderTopLeftRadius = 12;
        popup.style.borderTopRightRadius = 12;
        popup.style.borderBottomLeftRadius = 12;
        popup.style.borderBottomRightRadius = 12;
        popup.style.paddingTop = 30;
        popup.style.paddingBottom = 30;
        popup.style.paddingLeft = 40;
        popup.style.paddingRight = 40;
        popup.style.alignItems = Align.Center;
        popup.style.minWidth = 300;
        
        // Title
        var title = new Label("COSIG Ray Tracer");
        title.style.fontSize = 24;
        title.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.95f));
        title.style.marginBottom = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        popup.Add(title);
        
        // Subtitle
        var subtitle = new Label("GPU-Accelerated BVH Ray Tracing");
        subtitle.style.fontSize = 12;
        subtitle.style.color = new StyleColor(new Color(0.6f, 0.65f, 0.7f));
        subtitle.style.marginBottom = 25;
        popup.Add(subtitle);
        
        // Developers label
        var devLabel = new Label("Developed by");
        devLabel.style.fontSize = 11;
        devLabel.style.color = new StyleColor(new Color(0.5f, 0.55f, 0.6f));
        devLabel.style.marginBottom = 10;
        popup.Add(devLabel);
        
        // Developer names
        var dev1 = new Label("1201716 - Miguel Póvoas");
        dev1.style.fontSize = 16;
        dev1.style.color = new StyleColor(new Color(0.29f, 0.56f, 0.85f));
        dev1.style.marginBottom = 5;
        popup.Add(dev1);
        
        var dev2 = new Label("1211008 - Guilherme Melo");
        dev2.style.fontSize = 16;
        dev2.style.color = new StyleColor(new Color(0.29f, 0.56f, 0.85f));
        dev2.style.marginBottom = 25;
        popup.Add(dev2);
        
        // Year
        var year = new Label("© 2025 ISEP");
        year.style.fontSize = 10;
        year.style.color = new StyleColor(new Color(0.4f, 0.45f, 0.5f));
        year.style.marginBottom = 20;
        popup.Add(year);
        
        // Close button
        var closeBtn = new Button(() => root.Remove(overlay));
        closeBtn.text = "Close";
        closeBtn.style.backgroundColor = new StyleColor(new Color(0.29f, 0.56f, 0.85f));
        closeBtn.style.color = Color.white;
        closeBtn.style.borderTopWidth = 0;
        closeBtn.style.borderBottomWidth = 0;
        closeBtn.style.borderLeftWidth = 0;
        closeBtn.style.borderRightWidth = 0;
        closeBtn.style.borderTopLeftRadius = 6;
        closeBtn.style.borderTopRightRadius = 6;
        closeBtn.style.borderBottomLeftRadius = 6;
        closeBtn.style.borderBottomRightRadius = 6;
        closeBtn.style.paddingTop = 10;
        closeBtn.style.paddingBottom = 10;
        closeBtn.style.paddingLeft = 30;
        closeBtn.style.paddingRight = 30;
        popup.Add(closeBtn);
        
        overlay.Add(popup);
        
        // Close when clicking overlay background
        overlay.RegisterCallback<ClickEvent>(evt => 
        {
            if (evt.target == overlay)
                root.Remove(overlay);
        });
        
        root.Add(overlay);
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
                // Store frames for later saving (user will click Save Image)
                gifFrames = frames;
                lastRenderedTexture = frames[0];
                DisplayTexture(lastRenderedTexture);
                
                // Start playback
                if (gifPlaybackCoroutine != null) StopCoroutine(gifPlaybackCoroutine);
                gifPlaybackCoroutine = StartCoroutine(PlayGifLoop(0.15f)); // 15cs delay
                
                StartCoroutine(ShowToast($"GIF ready! {frames.Count} frames."));
                
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

    void OnSaveSceneClicked()
    {
        if (scene == null)
        {
            StartCoroutine(ShowToast("No scene loaded to save!"));
            return;
        }

#if UNITY_EDITOR
        string defaultName = $"preset_{DateTime.Now:yyyyMMdd_HHmmss}";
        string path = EditorUtility.SaveFilePanel(
            "Save Scene Preset",
            "Assets/Output",
            defaultName,
            "json"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                // Create preset from current UI settings
                RenderSettings settings = GetRenderSettingsFromUI();
                ScenePreset preset = ScenePreset.FromRenderSettings(settings, loadedSceneFilePath, loadedReferenceImagePath);
                preset.PresetName = Path.GetFileNameWithoutExtension(path);
                preset.IsOrthographic = isOrthographicMode;
                
                // Save top bar settings
                preset.AASamples = currentAASamples;
                preset.ShadowMode = shadowMode;
                preset.EnableGlossy = isGlossy;
                preset.BlurMode = blurMode;
                
                // Serialize to JSON
                string json = JsonUtility.ToJson(preset, prettyPrint: true);
                
                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json);
                
                StartCoroutine(ShowToast($"Preset saved: {Path.GetFileName(path)}"));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneBuilder] Failed to save preset: {ex.Message}");
                StartCoroutine(ShowToast("Failed to save preset!"));
            }
        }
#else
        Debug.LogWarning("File dialog is only supported in the Unity Editor.");
        StartCoroutine(ShowToast("File dialog not supported in build."));
#endif
    }

    void OnLoadSceneClicked()
    {
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Load Scene Preset", "Assets/Output", "json");
        
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                // Read and deserialize JSON
                string json = File.ReadAllText(path);
                ScenePreset preset = JsonUtility.FromJson<ScenePreset>(json);
                
                // Load scene data if path exists
                if (!string.IsNullOrEmpty(preset.SceneFilePath) && File.Exists(preset.SceneFilePath))
                {
                    scene = sceneService.LoadScene(preset.SceneFilePath);
                    loadedSceneFilePath = preset.SceneFilePath;
                }
                else if (!string.IsNullOrEmpty(preset.SceneFilePath))
                {
                    Debug.LogWarning($"[SceneBuilder] Scene file not found: {preset.SceneFilePath}");
                    StartCoroutine(ShowToast("Scene file not found. Load manually."));
                }
                
                // Load reference image if path exists
                if (!string.IsNullOrEmpty(preset.ReferenceImagePath) && File.Exists(preset.ReferenceImagePath))
                {
                    byte[] fileData = File.ReadAllBytes(preset.ReferenceImagePath);
                    Texture2D tex = new Texture2D(2, 2);
                    if (tex.LoadImage(fileData))
                    {
                        loadedReferenceImagePath = preset.ReferenceImagePath;
                        Display3DTexture(tex);
                    }
                }
                
                // Apply preset values to UI controls
                ApplyPresetToUI(preset);
                
                StartCoroutine(ShowToast($"Preset loaded: {preset.PresetName}"));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneBuilder] Failed to load preset: {ex.Message}");
                StartCoroutine(ShowToast("Failed to load preset!"));
            }
        }
#else
        Debug.LogWarning("File dialog is only supported in the Unity Editor.");
        StartCoroutine(ShowToast("File dialog not supported in build."));
#endif
    }

    /// <summary>
    /// Applies a loaded scene preset to the UI controls.
    /// </summary>
    void ApplyPresetToUI(ScenePreset preset)
    {
        // Resolution
        if (txtResX != null) txtResX.value = preset.ResolutionX.ToString();
        if (txtResY != null) txtResY.value = preset.ResolutionY.ToString();
        
        // Background color (sliders are 0-100)
        if (sldBgR != null && preset.BackgroundColor.Length >= 3)
        {
            sldBgR.value = preset.BackgroundColor[0] * 100f;
            sldBgG.value = preset.BackgroundColor[1] * 100f;
            sldBgB.value = preset.BackgroundColor[2] * 100f;
        }
        
        // Light intensity (slider 0-100 maps to 0-5)
        if (sldLightIntensity != null) sldLightIntensity.value = preset.LightIntensity * 20f;
        if (txtLightIntensity != null) txtLightIntensity.value = preset.LightIntensity.ToString("F1");
        
        // Camera position
        if (txtCamPosX != null && preset.CameraPosition.Length >= 3)
        {
            txtCamPosX.value = preset.CameraPosition[0].ToString("F1");
            txtCamPosY.value = preset.CameraPosition[1].ToString("F1");
            txtCamPosZ.value = preset.CameraPosition[2].ToString("F1");
        }
        
        // Camera rotation
        if (sldRotX != null && preset.CameraRotation.Length >= 3)
        {
            sldRotX.value = preset.CameraRotation[0];
            sldRotY.value = preset.CameraRotation[1];
            sldRotZ.value = preset.CameraRotation[2];
        }
        if (txtRotX != null && preset.CameraRotation.Length >= 3)
        {
            txtRotX.value = preset.CameraRotation[0].ToString("F0");
            txtRotY.value = preset.CameraRotation[1].ToString("F0");
            txtRotZ.value = preset.CameraRotation[2].ToString("F0");
        }
        
        // Camera FOV
        if (txtCamFov != null) txtCamFov.value = preset.CameraFov.ToString("F1");
        sceneFov = preset.CameraFov;
        
        // Orthographic mode
        isOrthographicMode = preset.IsOrthographic;
        if (btnOrthographic != null)
        {
            btnOrthographic.style.backgroundColor = isOrthographicMode 
                ? new StyleColor(new Color(0.3f, 0.7f, 0.3f))
                : new StyleColor(StyleKeyword.Initial);
        }
        
        // Recursion depth
        if (txtRecursionDepth != null) txtRecursionDepth.value = preset.RecursionDepth.ToString();
        
        // Lighting toggles
        if (togAmbient != null) togAmbient.value = preset.EnableAmbient;
        if (togDiffuse != null) togDiffuse.value = preset.EnableDiffuse;
        if (togSpecular != null) togSpecular.value = preset.EnableSpecular;
        if (togRefraction != null) togRefraction.value = preset.EnableRefraction;
        
        // Top bar settings
        // AA
        currentAASamples = preset.AASamples;
        if (btnAA != null) btnAA.text = $"AA: {currentAASamples}x";
        
        // Shadows
        shadowMode = preset.ShadowMode;
        if (btnShadows != null)
        {
            btnShadows.text = shadowMode == 0 ? "Shadows: Hard" : $"Shadows: {shadowSizes[shadowMode]:0.0}";
        }
        
        // Glossy
        isGlossy = preset.EnableGlossy;
        if (btnGlossy != null) btnGlossy.text = isGlossy ? "Glossy: On" : "Glossy: Off";
        
        // Motion Blur
        blurMode = preset.BlurMode;
        if (btnBlur != null)
        {
            btnBlur.text = blurMode == 0 ? "Blur: Off" : $"Blur: {blurSpeeds[blurMode]:0.0}";
        }
    }

    void OnLoadImageClicked()
    {
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Load Image", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path))
        {
            // Load the image from disk
            byte[] fileData = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(fileData))
            {
                loadedReferenceImagePath = path; // Track for scene preset saving
                Display3DTexture(tex);
                StartCoroutine(ShowToast($"Loaded: {Path.GetFileName(path)}"));
                Debug.Log($"[SceneBuilder] Loaded image from {path}");
            }
            else
            {
                StartCoroutine(ShowToast("Failed to load image!"));
                Debug.LogError($"[SceneBuilder] Failed to load image from {path}");
            }
        }
#else
        Debug.LogWarning("File dialog is only supported in the Unity Editor.");
        StartCoroutine(ShowToast("File dialog not supported in build."));
#endif
    }

    void Display3DTexture(Texture2D tex)
    {
        if (uiDocument == null) return;
        
        // Get the container for the 3D rendered image
        var container = uiDocument.rootVisualElement.Q<VisualElement>("3d-rendered-image");
        if (container != null)
        {
            // Set texture properties for clean display
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            
            // Set the texture as background image with proper scaling
            container.style.backgroundImage = new StyleBackground(tex);
            container.style.unityBackgroundImageTintColor = Color.white;
            
            // ScaleToFit maintains aspect ratio and centers the image
            #pragma warning disable 0618 // Suppress deprecation warning
            container.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            #pragma warning restore 0618
            
            // Ensure container fills available space
            container.style.flexGrow = 1;
            container.style.width = StyleKeyword.Auto;
            container.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            
            // Force repaint
            container.MarkDirtyRepaint();
        }
        else
        {
            Debug.LogWarning("Could not find '3d-rendered-image' VisualElement in the UI Document.");
        }
    }

    void DisplayTexture(Texture2D tex)
    {
        if (uiDocument == null) return;
        
        // Get the container for the ray-traced image
        var container = uiDocument.rootVisualElement.Q<VisualElement>("ray-traced-image");
        if (container != null)
        {
            // Set texture properties for clean display
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            
            // Set the texture as background image with proper scaling
            container.style.backgroundImage = new StyleBackground(tex);
            container.style.unityBackgroundImageTintColor = Color.white;
            
            // ScaleToFit maintains aspect ratio and centers the image
            #pragma warning disable 0618 // Suppress deprecation warning
            container.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            #pragma warning restore 0618
            
            // Ensure container fills available space
            container.style.flexGrow = 1;
            container.style.width = StyleKeyword.Auto;
            container.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            
            // Force repaint
            container.MarkDirtyRepaint();
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
