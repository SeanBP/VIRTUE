using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.UI;
using System.Collections.Specialized;
using System.Diagnostics;
using TMPro;
using TriLibCore;
using TriLibCore.General;
using TriLibCore.Mappers;
using TriLibCore.Utils;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;
using VirtueCore.Models;
using VirtueCore.Tours;
using VirtueCore.Shared;
//using System.Runtime.Remoting.Contexts;

#pragma warning disable 0618

public class ComponentMaker : MonoBehaviour
{
    private bool menagerieActive = false;
    public UnityEngine.UI.Text errorText;

    private string filename = "EIC_ePIC.json";
    private string lastFilename = "EIC_ePIC.json";
    private StreamReader source;
    private string fileContents;
    private List<GameObject> nameTagObjects = new List<GameObject>();  // Stores references to name tags
    public List<GameObject> detectorParts = new List<GameObject>();
    private List<GameObject> lineObjects = new List<GameObject>();
    private List<GameObject> pivots = new List<GameObject>();
    private bool tagsActive = false;
    private float scale = 1.0f;
    private float lineThickness = 0.01f;
    public UnityEngine.UI.Text detectorText;
    private string targetVersion = "3.2.1";
    // 3.2.0 model files remain compatible: the only change since then is the
    // header field rename (detector -> title), which this parser already
    // accepts via the legacy detector alias above.
    private List<string> compatibleVersions = new List<string> { "3.2.0" };
    private List<string> fileNames = new List<string>();
    private List<string> displayNames = new List<string>();
    public TMP_Dropdown fileDropdown;
    public Slider explodeSlider;
    private float lastSliderValue = 1;
    private bool collidersOn = false;
    private bool wireOn = false;
    public UnityEngine.UI.Text modelText;
    public UnityEngine.UI.Text wireText;
    public UnityEngine.UI.Text nameText;
    public bool loadingModel = false;
    // Bumped every time a new load starts (buildSimModel). Captured by the
    // async FBX callbacks below so a load that gets superseded by a newer
    // selection can tell it's stale and discard its results instead of
    // clobbering whatever the newer selection already built.
    private int modelLoadToken = 0;
    public GameObject figures;
    public GameObject models;
    public UnityEngine.UI.Text figureText;
    List<int> objectIndexes = new List<int>();
    private List<float> detectorPartAlphas = new List<float>();
    private string modelTextCache = "";
    private GameObject activeModel;

    // Found at runtime the same way EventLoader finds componentMaker, since
    // the two scripts are otherwise independent.
    private EventLoader eventLoader;


    private List<string> acceptedExtensions = new List<string>
    {
        ".json", ".fbx"
    };

    // Start is called before the first frame update
    void Start()
    {
        eventLoader = FindAnyObjectByType<EventLoader>();

        figures.SetActive(false);
        LoadFilesIntoDropdown();
        int initialIndex = fileNames.IndexOf("EIC_ePIC.json");
        if (initialIndex != -1)
        {
            fileDropdown.value = initialIndex;
            OnFileSelected(initialIndex);
        }
        else
        {
            filename = fileNames[0];
        }
        buildSimModel();
    }

    public void LoadTourFile(string newFilename)
    {
        StartCoroutine(LoadTourFileCoroutine(newFilename));
    }

    private IEnumerator LoadTourFileCoroutine(string newFilename)
    {
        // Set filename and start loading the model. A tour manages its own
        // model/event pairing explicitly (per-scene), so don't chase this
        // model's header.event_file.
        filename = newFilename;
        LoadFile(chaseCompanion: false);  // This should set loadingModel = true internally

        // Wait until loading is done
        while (loadingModel)
            yield return null;

        modelTextCache = detectorText.text;
        detectorText.text = "";


        // Immediately hide all components by setting their alpha to 0
        for (int i = 0; i < detectorParts.Count; i++)
        {
            Renderer renderer = detectorParts[i].GetComponent<Renderer>();
            if (renderer == null) continue;

            Color c = renderer.material.color;
            c.a = 0f;
            renderer.material.color = c;
        }
        if (!wireOn)
        {
            for (int i = 0; i < lineObjects.Count; i++)
            {

                LineRenderer line = lineObjects[i].GetComponent<LineRenderer>();
                if (line == null) continue;

                // Set both start and end color alpha to 0
                Color start = line.startColor;
                Color end = line.endColor;
                start.a = 0f;
                end.a = 0f;
                line.startColor = start;
                line.endColor = end;
                lineObjects[i].SetActive(lineObjects[i]);
            }
        }

        // If figures are active, toggle them off
        if (figures.activeSelf)
            ToggleFigures();
    }


    public void ActivateComponents(ModelSettings modelSettings)
    {
        StartCoroutine(ActivateComponentsCoroutine(modelSettings));
    }

    private IEnumerator ActivateComponentsCoroutine(ModelSettings modelSettings)
    {
        // Wait until model loading is done
        while (loadingModel)
            yield return null;

        bool activateAll = modelSettings.all_components;

        // Build HashSet for line-active components
        HashSet<int> lineActiveSet = new HashSet<int>();
        if (modelSettings.lines_active != null && modelSettings.lines_active.Count > 0)
            lineActiveSet = new HashSet<int>(modelSettings.lines_active);

        // Create selection set
        HashSet<int> selected = null;
        if (!activateAll && modelSettings.components != null && modelSettings.components.Count > 0)
            selected = new HashSet<int>(modelSettings.components);

        float duration = 0.5f;
        float t = 0f;

        List<float> startAlphas = new List<float>();
        List<List<float>> lineStartAlphas = new List<List<float>>();

        // Capture starting alpha values
        for (int i = 0; i < detectorParts.Count; i++)
        {
            GameObject part = detectorParts[i];

            Renderer renderer = part.GetComponent<Renderer>();
            startAlphas.Add(renderer != null ? renderer.material.color.a : 0f);

            LineRenderer[] partLines = part.GetComponentsInChildren<LineRenderer>();
            List<float> lineAlphas = new List<float>();

            foreach (var line in partLines)
                lineAlphas.Add(line.startColor.a);

            lineStartAlphas.Add(lineAlphas);
        }

        // Fade
        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = t / duration;

            for (int i = 0; i < detectorParts.Count; i++)
            {
                GameObject part = detectorParts[i];
                bool isSelected = activateAll || (selected != null && selected.Contains(i));

                // Mesh alpha
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer != null)
                {
                    float start = startAlphas[i];
                    float target = isSelected ? detectorPartAlphas[i] : 0f;

                    Color c = renderer.material.color;
                    c.a = Mathf.Lerp(start, target, lerp);
                    renderer.material.color = c;
                }

                // Line alpha
                LineRenderer[] partLines = part.GetComponentsInChildren<LineRenderer>();
                for (int j = 0; j < partLines.Length; j++)
                {
                    float startAlpha = lineStartAlphas[i][j];
                    bool showLine = isSelected && lineActiveSet.Contains(i);
                    float targetAlpha = showLine ? 1f : 0f;

                    LineRenderer line = partLines[j];

                    Color sc = line.startColor;
                    Color ec = line.endColor;

                    sc.a = Mathf.Lerp(startAlpha, targetAlpha, lerp);
                    ec.a = Mathf.Lerp(startAlpha, targetAlpha, lerp);

                    line.startColor = sc;
                    line.endColor = ec;
                }
            }

            yield return null;
        }

        // Snap final values
        for (int i = 0; i < detectorParts.Count; i++)
        {
            GameObject part = detectorParts[i];
            bool isSelected = activateAll || (selected != null && selected.Contains(i));

            // Mesh
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color c = renderer.material.color;
                c.a = isSelected ? detectorPartAlphas[i] : 0f;
                renderer.material.color = c;
            }

            // Lines (FIXED: re-fetch lines here)
            LineRenderer[] partLines = part.GetComponentsInChildren<LineRenderer>();

            bool showLineFinal = isSelected && lineActiveSet.Contains(i);
            float targetAlphaFinal = showLineFinal ? 1f : 0f;

            foreach (var line in partLines)
            {
                Color sc = line.startColor;
                Color ec = line.endColor;

                sc.a = targetAlphaFinal;
                ec.a = targetAlphaFinal;

                line.startColor = sc;
                line.endColor = ec;
            }
        }
    }




    public void ToggleFigures()
    {
        if (figures.activeSelf)
        {
            figures.SetActive(false);
            figureText.text = "Show Figures";
        }
        else
        {
            figures.SetActive(true);
            figureText.text = "Hide Figures";
        }
    }

    private void LoadFBXModel(string filepath, int token)
    {
        var assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions();
        assetLoaderOptions.UseUnityNativeNormalCalculator = true;
        assetLoaderOptions.AlphaMaterialMode = AlphaMaterialMode.Transparent;

        AssetLoader.LoadModelFromFile(
            filepath,
            (context) => OnLoad(context, token),
            (context) => OnMaterialsLoad(context, token),
            (context, progress) => OnProgress(context, progress, token),
            (error) => OnError(error, token),
            null,
            assetLoaderOptions);
    }


    private void OnBeginLoad(bool anyModelSelected)
    {
        loadingModel = true;
    }


    private void OnProgress(AssetLoaderContext assetLoaderContext, float progress, int token)
    {
        if (token != modelLoadToken) return;

        if (progress < 1f)
        {
            // Display the loading progress rounded to the nearest integer
            if (errorText != null)
            {
                errorText.text = $"Loading model: {Math.Round(progress * 100)}%";
            }
        }
        else
        {
            // Clear the error text once loading is complete
            if (errorText != null)
            {
                errorText.text = "";
            }
        }
    }


    private void OnError(IContextualizedError contextualizedError, int token)
    {
        if (token != modelLoadToken) return;
        errorText.text = $"Error: {contextualizedError.ToString()}";
    }

    private void OnLoad(AssetLoaderContext assetLoaderContext, int token)
    {
        if (token != modelLoadToken)
        {
            // A newer model selection has since started -- this load was
            // effectively aborted. Discard what it built instead of adding
            // it on top of whatever superseded it.
            if (assetLoaderContext.RootGameObject != null)
                Destroy(assetLoaderContext.RootGameObject);
            return;
        }

        explodeSlider.value = 1f;
        var myLoadedGameObject = assetLoaderContext.RootGameObject;
        TagNthLevelChildren(myLoadedGameObject, "Detector", 2);
        TagNthLevelChildren(myLoadedGameObject, "Detector", 1);
        myLoadedGameObject.SetActive(false);
        detectorParts.Add(myLoadedGameObject);
        activeModel = myLoadedGameObject;
        loadingModel = false;
    }

    private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext, int token)
    {
        if (token != modelLoadToken) return;

        var myLoadedGameObject = assetLoaderContext.RootGameObject;
        myLoadedGameObject.SetActive(true);
        myLoadedGameObject.tag = "Detector";
        int acceptLightLayer = LayerMask.NameToLayer("Accept Light");
        SetLayerRecursively(myLoadedGameObject, acceptLightLayer);
    }
    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer; // Set layer for the current object
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer); // Recursively set layer for children
        }
    }

    private void TagNthLevelChildren(GameObject parent, string tag, int level)
    {
        try
        {
            TagChildrenAtLevel(parent.transform, tag, level, 0);
        }
        catch { }
    }

    private void TagChildrenAtLevel(Transform parent, string tag, int targetLevel, int currentLevel)
    {
        if (currentLevel == targetLevel)
        {
            // Tag all children at the target level
            foreach (Transform child in parent)
            {
                if (child.childCount > 0)
                {
                    // Tag the child if it has children
                    child.gameObject.tag = tag;
                }
            }
        }
        else
        {
            // Recursively check deeper levels
            foreach (Transform child in parent)
            {
                TagChildrenAtLevel(child, tag, targetLevel, currentLevel + 1);
            }
        }
    }

    public void ToggleMenagerie()
    {
        if (activeModel == null)
            return;

        bool newState = !activeModel.activeSelf;
        activeModel.SetActive(newState);

        menagerieActive = !newState;
        modelText.text = newState ? "Hide Model" : "Show Model";
    }

    // Kept as a genuine zero-parameter method (not a default-value overload)
    // because it's wired directly to UI Button.onClick in the scene, and
    // Unity's persistent-call resolver only binds to an exact-arity match --
    // a default parameter here would silently break that binding.
    public void LoadFile()
    {
        LoadFile(true);
    }

    // chaseCompanion controls whether a successfully-loaded model's own
    // header.event_file gets auto-loaded afterward. Direct/manual loads
    // (via the parameterless LoadFile() above) chase it; companion loads
    // triggered the other way (LoadModelFile, below) and tour loads don't,
    // so the two headers can never chase each other in a loop and the file
    // the user actually selected always wins.
    private void LoadFile(bool chaseCompanion)
    {

        if (!String.Equals(filename, lastFilename))
        {
            explodeSlider.value = 1f;
            lastSliderValue = 1f;
            lastFilename = filename;
            buildSimModel(chaseCompanion);

        }
    }

    // For external callers (e.g. EventLoader.cs, when an event file's own
    // header.model_file names a model) that just want a named model built
    // normally -- unlike LoadTourFile(), this does not hide components
    // afterward for a tour-style reveal. This model's own header.event_file
    // (if any) is not chased, since the event file that led here already
    // takes precedence.
    public void LoadModelFile(string newFilename)
    {
        filename = newFilename;
        LoadFile(chaseCompanion: false);
    }

    public void ResetModelState()
    {
        activeModel = null;
        loadingModel = true;
        wireText.text = "Show Wireframe";
        wireOn = false;

        // Deactivate all name tags
        for (int i = 0; i < nameTagObjects.Count; i++)
        {
            nameTagObjects[i].SetActive(false);
        }
        nameText.text = "Show Nametags";
        tagsActive = false;
        collidersOn = false;
        modelText.text = "Hide Model";
        menagerieActive = false;

        // Deactivate all models under the "Models" parent
        GameObject modelsParent = GameObject.Find("Models");
        if (modelsParent != null)
        {
            foreach (Transform child in modelsParent.transform)
            {
                child.gameObject.SetActive(false);
            }
        }

        // Find and deactivate all objects tagged "Detector" that are not part of the "Models" root
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        List<GameObject> detectorObjects = new List<GameObject>();
        foreach (var obj in allObjects)
        {
            if (obj.CompareTag("Detector") && (obj.transform.parent == null || obj.transform.root.name != "Models"))
            {
                detectorObjects.Add(obj);
            }
        }

        GameObject[] components = detectorObjects.ToArray();

        // Clear and destroy any detector components
        detectorParts = new List<GameObject>();
        lineObjects = new List<GameObject>();
        pivots = new List<GameObject>();
        for (int i = 0; i < components.Length; i++)
        {
            Destroy(components[i]);
        }

        menagerieActive = false;

        // Destroy any remaining name tags
        for (int i = 0; i < nameTagObjects.Count; i++)
        {
            Destroy(nameTagObjects[i]);
        }
        nameTagObjects = new List<GameObject>();
        tagsActive = false;
    }

    public void buildSimModel(bool chaseCompanion = true)
    {
        ResetModelState();

        // See modelLoadToken's declaration -- this makes the FBX branch's
        // async callbacks below able to detect a since-superseded load.
        modelLoadToken++;
        int myToken = modelLoadToken;
        bool startedAsyncLoad = false;

        int selectedIndex = fileDropdown.value;

        string path = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Models");
        string filePath = Path.Combine(path, filename);

        try
        {
            string fileExtension = Path.GetExtension(filePath).ToLower();

            // ================= JSON MODEL =================
            if (fileExtension == ".json")
            {
                StreamReader source = new StreamReader(filePath);
                fileContents = source.ReadToEnd();
                source.Close();

                ComponentListWrapper componentListWrapper =
                    JsonUtility.FromJson<ComponentListWrapper>(fileContents);

                string version = componentListWrapper.header.version;

                if (VersionCheck.IsCompatible(version, targetVersion, compatibleVersions))
                {
                    string modelTitle = LegacyField.Resolve(componentListWrapper.header.title, componentListWrapper.header.detector);
                    detectorText.text = modelTitle;

                    // NOTE: no default case, intentionally -- an unrecognized
                    // length_unit leaves scale at whatever the previously
                    // loaded model set it to, rather than resetting to 1.0.
                    switch (componentListWrapper.header.length_unit)
                    {
                        case "m":
                            scale = 1.0f;
                            break;
                        case "cm":
                            scale = 0.01f;
                            break;
                        case "mm":
                            scale = 0.001f;
                            break;
                    }

                    scale *= componentListWrapper.header.scale;

                    if (chaseCompanion && !string.IsNullOrEmpty(componentListWrapper.header.event_file) && eventLoader != null)
                    {
                        eventLoader.LoadEventFile(componentListWrapper.header.event_file);
                    }

                    var sortedComponents = componentListWrapper.components
                        .OrderBy(c => c.index == -1 ? int.MaxValue : c.index)
                        .ToList();

                    ComponentsBuildResult buildResult = ModelGeometry.BuildComponents(sortedComponents, scale, lineThickness, collidersOn);
                    detectorParts.AddRange(buildResult.DetectorParts);
                    lineObjects.AddRange(buildResult.LineObjects);
                    nameTagObjects.AddRange(buildResult.NameTagObjects);
                    pivots.AddRange(buildResult.Pivots);

                    // Store original alpha values for tour mode
                    detectorPartAlphas.Clear();

                    foreach (GameObject go in detectorParts)
                    {
                        Renderer renderer = go.GetComponent<Renderer>();

                        if (renderer != null)
                            detectorPartAlphas.Add(renderer.material.color.a);
                        else
                            detectorPartAlphas.Add(0f);
                    }

                    // Create a parent object for hiding/showing the entire model.
                    // IMPORTANT: detectorParts is left unchanged so tour mode still
                    // has access to every individual detector component.
                    GameObject jsonRoot = new GameObject(modelTitle);

                    foreach (GameObject part in detectorParts)
                    {
                        part.transform.SetParent(jsonRoot.transform, true);
                    }

                    activeModel = jsonRoot;
                }
                else
                {
                    errorText.text = "Model JSON File not version " + targetVersion;
                    UnityEngine.Debug.LogError(errorText.text);
                }
            }

            // ================= SCENE MODEL =================
            else if (objectIndexes.Contains(selectedIndex))
            {
                GameObject modelsParent = GameObject.Find("Models");

                if (modelsParent != null)
                {
                    Transform selectedObject =
                        modelsParent.transform.GetChild(objectIndexes.IndexOf(selectedIndex));

                    if (selectedObject != null)
                    {
                        selectedObject.gameObject.SetActive(true);
                        detectorText.text = selectedObject.name;

                        try
                        {
                            TagChildrenAtLevel(selectedObject.transform, "Detector", 1, 0);
                        }
                        catch { }

                        activeModel = selectedObject.gameObject;
                    }
                }
            }

            // ================= FBX MODEL =================
            else
            {
                detectorText.text = Path.GetFileNameWithoutExtension(filename);
                detectorPartAlphas.Clear();

                LoadFBXModel(filePath, myToken);
                startedAsyncLoad = true;
                // activeModel is assigned in OnLoad()
            }
        }
        catch (Exception ex)
        {
            errorText.text = "Error loading model file: " + ex.Message;
            UnityEngine.Debug.LogError(errorText.text);
        }

        // The FBX branch is still running its async load -- OnLoad/OnError
        // (if not superseded by then) will clear loadingModel when it
        // actually finishes, rather than the near-instant false this line
        // used to set right after merely kicking the load off.
        if (!startedAsyncLoad)
        {
            loadingModel = false;
        }
    }


    void Update()
    {
        for (int i = 0; i < pivots.Count; i++)
        {
            pivots[i].transform.rotation = Camera.main.transform.rotation;

        }
    }

    void LoadFilesIntoDropdown()
    {
        // Path to the "Models" subfolder in StreamingAssets
        string path = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Models");

        // Check if the "Models" folder exists
        if (!Directory.Exists(path))
        {
            errorText.text = "Models folder not found in StreamingAssets.";
            return;
        }

        // Clear existing options and populate new ones
        fileDropdown.ClearOptions();
        fileNames.Clear();
        displayNames.Clear();
        objectIndexes.Clear();

        // Add child object names from "3DModels"
        GameObject modelsParent = GameObject.Find("Models");
        if (modelsParent != null)
        {
            foreach (Transform child in modelsParent.transform)
            {
                string childName = child.gameObject.name;
                fileNames.Add(childName);
                displayNames.Add(childName);
                objectIndexes.Add(displayNames.Count - 1); // Track 3D object index
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("Models GameObject not found in the scene.");
        }

        // Retrieve all files in the directory
        string[] allFiles = Directory.GetFiles(path);

        foreach (string file in allFiles)
        {
            string extension = Path.GetExtension(file).ToLower();

            // Check if the file has an accepted extension
            if (acceptedExtensions.Contains(extension))
            {
                string fileName = Path.GetFileName(file); // Get the file name only
                string displayName = Path.GetFileNameWithoutExtension(file); // Display name without extension

                fileNames.Add(fileName); // Store for selection handling
                displayNames.Add(displayName); // Add to dropdown
            }
        }



        // Add file names to the dropdown
        fileDropdown.AddOptions(displayNames);
    }

    public void OnFileSelected(int index)
    {
        if (index < 0 || index >= fileNames.Count)
            return;

        filename = fileNames[index];

    }

    public void ToggleLines()
    {
        for (int i = 0; i < lineObjects.Count; i++)
        {
            lineObjects[i].SetActive(!lineObjects[i].activeSelf);
        }
        if (wireOn)
        {
            wireText.text = "Show Wireframe";
            wireOn = false;
        }
        else
        {
            wireText.text = "Hide Wireframe";
            wireOn = true;
        }
    }

    public void ToggleTags()
    {
        if (tagsActive)
        {
            for (int i = 0; i < nameTagObjects.Count; i++)
            {
                nameTagObjects[i].SetActive(false);
            }
            nameText.text = "Show Nametags";
        }
        else
        {
            for (int i = 0; i < nameTagObjects.Count; i++)
            {
                nameTagObjects[i].SetActive(true);
            }
            nameText.text = "Hide Nametags";
        }
        tagsActive = !tagsActive;
    }

    public void Explode(float newValue)
    {
        GameObject[] detectorParts = GameObject.FindGameObjectsWithTag("Detector");

        for (int i = 0; i < detectorParts.Length; i++)
        {
            Vector3 lastPosition = detectorParts[i].transform.position;
            detectorParts[i].transform.localPosition = new Vector3((lastPosition.x / lastSliderValue) * newValue, (lastPosition.y / lastSliderValue) * newValue, (lastPosition.z / lastSliderValue) * newValue);

        }
        lastSliderValue = newValue;
    }

    // Toggle the colliders on and off
    public void ToggleColliders()
    {
        GameObject[] components = GameObject.FindGameObjectsWithTag("Detector");

        if (collidersOn)
        {
            foreach (var obj in components)
            {
                Collider collider = obj.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;  // Toggle collider state
                }
            }
            collidersOn = false;
        }
        else
        {
            foreach (var obj in components)
            {
                Collider collider = obj.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = true;  // Toggle collider state
                }
            }
            collidersOn = true;
        }
    }

    public void OffColliders()
    {
        GameObject[] components = GameObject.FindGameObjectsWithTag("Detector");
        foreach (var obj in components)
        {
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;  // Toggle collider state
            }
        }
        collidersOn = false;
    }
}
