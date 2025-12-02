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

    // State
    private bool isRendering = false;
    private System.Diagnostics.Stopwatch stopwatch;
    private CancellationTokenSource cancellationTokenSource;

    void OnEnable()
    {
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

        lblElapsedTime = root.Q<Label>("lbl-elapsed-time");
        progressBar = root.Q<ProgressBar>("progress-bar");
        lblProgressText = root.Q<Label>("lbl-progress-text");
    }

    void Start()
    {
        // Load default scene initially but do not render
        string filePath = "Assets/Resources/Scenes/test_scene_1.txt"; 
        if (File.Exists(filePath))
        {
            scene = sceneService.LoadScene(filePath);
            LogSceneSummary(scene);
        }
    }

    void Update()
    {
        if (isRendering && stopwatch != null)
        {
            TimeSpan ts = stopwatch.Elapsed;
            if (lblElapsedTime != null)
            {
                lblElapsedTime.text = $"Elapsed Time: {ts:hh\\:mm\\:ss}";
            }
        }
    }

    async void OnStartRayTracingClicked()
    {
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
            var result = await rayTracer.RenderAsync(scene, progress, cancellationTokenSource.Token);
            
            // Create texture on main thread
            lastRenderedTexture = new Texture2D(result.width, result.height, TextureFormat.RGBA32, false);
            lastRenderedTexture.SetPixels(result.pixels);
            lastRenderedTexture.Apply();
            
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
            // Clear any existing content
            container.Clear();
            
            // Create a new UI Toolkit Image element
            var image = new UnityEngine.UIElements.Image();
            
            // Convert the Texture2D to a Sprite
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            image.sprite = sprite;
            
            // Set image to scale to fit the container while maintaining aspect ratio
            image.style.width = new StyleLength(Length.Percent(100));
            image.style.height = new StyleLength(Length.Percent(100));
            image.scaleMode = ScaleMode.ScaleToFit;
            
            // Add the image to the container
            container.Add(image);
            
            Debug.Log("Displayed render on UI Toolkit VisualElement.");
        }
        else
        {
            Debug.LogWarning("Could not find 'ray-traced-image' VisualElement in the UI Document.");
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
