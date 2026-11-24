using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.UI;
using System.Collections.Specialized;
using System.Diagnostics;
using TMPro;
using static System.Net.Mime.MediaTypeNames;
using System.Linq;
using TriLibCore;
using TriLibCore.General;
using TriLibCore.Mappers;
using TriLibCore.Utils;
using UnityEngine.Networking;
using NativeFilePickerNamespace;
using UnityEngine.Android;
using VirtueCore.Models;
using VirtueCore.Tours;
using VirtueCore.Shared;
#pragma warning disable 0618

public class ComponentMaker : MonoBehaviour
{
    private bool menagerieActive = false;
    public UnityEngine.UI.Text errorText;

    private string filename = "EIC_ePIC";
    private string lastFilename = "EIC_ePIC";
    private StreamReader source;
    private string fileContents;
    private List<GameObject> nameTagObjects = new List<GameObject>();  // Stores references to name tags
    private List<GameObject> detectorParts = new List<GameObject>();
    private List<GameObject> lineObjects = new List<GameObject>();
    private List<GameObject> pivots = new List<GameObject>();
    private bool tagsActive = false;
    private float scale = 1.0f;
    private float lineThickness = 0.01f;
    public UnityEngine.UI.Text detectorText;
    private string targetVersion = "3.2.3";
    // 3.2.0 model files remain compatible: the only change since then is the
    // header field rename (detector -> title), which this parser already
    // accepts via the legacy detector alias above.
    private List<string> compatibleVersions = new List<string> { "3.2.0", "3.2.1", "3.2.2" };
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
    // Bumped every time a new load starts (ResetModelState, called by both
    // BuildSimModel and LoadFBXModel). Captured by the async FBX callbacks
    // below so a load that gets superseded by a newer selection -- from
    // either the dropdown or another file-picker pick -- can tell it's
    // stale and discard its results instead of clobbering whatever the
    // newer selection already built.
    private int modelLoadToken = 0;
    public GameObject figures;
    public UnityEngine.UI.Text figureText;
    List<int> jsonFileIndexes = new List<int>();
    // Model files whose header has "hidden": true -- kept out of the
    // dropdown (fileNames/displayNames) but still loadable by reference.
    private List<string> hiddenFileNames = new List<string>();
    List<int> objectIndexes = new List<int>();
    public GameObject models;
    private List<float> detectorPartAlphas = new List<float>();
    private string modelTextCache = "";
    private GameObject activeModel;

    // Found at runtime the same way EventLoader finds componentMaker, since
    // the two scripts are otherwise independent.
    private EventLoader eventLoader;

    private List<string> acceptedExtensions = new List<string>
    {
        ".json"
    };

    // Start is called before the first frame update
    void Start()
    {
        eventLoader = FindAnyObjectByType<EventLoader>();

        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
        {
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
        }
        figures.SetActive(false);
        LoadFilesIntoDropdown();
        int initialIndex = fileNames.IndexOf("EIC_ePIC");
        if (initialIndex != -1)
        {
            fileDropdown.value = initialIndex;
            OnFileSelected(initialIndex);
        }
        else
        {
            filename = fileNames[0];
        }
        BuildSimModel();
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

        int index = fileNames.FindIndex(f =>
        string.Equals(f, Path.GetFileNameWithoutExtension(newFilename),
        StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            fileDropdown.value = index;
        }

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

    // Fades to a tour scene's components. A newer call (the user skipping
    // ahead) replaces a fade still in progress; it starts from whatever
    // alphas that fade had reached, so nothing jumps.
    private Coroutine activateComponentsCoroutine;

    public void ActivateComponents(ModelSettings modelSettings)
    {
        if (activateComponentsCoroutine != null) StopCoroutine(activateComponentsCoroutine);
        activateComponentsCoroutine = StartCoroutine(ActivateComponentsCoroutine(modelSettings));
    }

    // Part lookups for ComponentFade, kept for the current model.
    private ComponentFade.Cache componentFadeCache;

    private IEnumerator ActivateComponentsCoroutine(ModelSettings modelSettings)
    {
        // Wait until model loading is done
        while (loadingModel)
            yield return null;

        if (componentFadeCache == null || !componentFadeCache.Matches(detectorParts))
            yield return ComponentFade.BuildCache(detectorParts, cache => componentFadeCache = cache);

        bool activateAll = modelSettings.all_components;

        HashSet<int> lineActiveSet = modelSettings.lines_active != null
            ? new HashSet<int>(modelSettings.lines_active)
            : new HashSet<int>();

        detectorText.text = "";

        HashSet<int> selected = null;
        if (!activateAll && modelSettings.components != null && modelSettings.components.Count > 0)
            selected = new HashSet<int>(modelSettings.components);

        // Where each part (and its lines) should end up.
        int count = detectorParts.Count;
        float[] meshTargets = new float[count];
        float[] lineTargets = new float[count];
        for (int i = 0; i < count; i++)
        {
            bool isSelected = activateAll || (selected != null && selected.Contains(i));
            meshTargets[i] = isSelected && i < detectorPartAlphas.Count ? detectorPartAlphas[i] : 0f;
            lineTargets[i] = isSelected && lineActiveSet.Contains(i) ? 1f : 0f;
        }

        // Spread over frames within a budget, so a scene that changes many
        // parts doesn't stall the camera moving to it.
        yield return ComponentFade.Fade(componentFadeCache, meshTargets, lineTargets, 0.5f);
    }

    public void UploadNewModel()
    {
        errorText.text = "";
        // Open file picker to select an FBX or JSON file
        NativeFilePicker.PickFile((string path) =>
        {
            if (path != null)
            {
                string extension = Path.GetExtension(path).ToLower();
                if (extension == ".json")
                {
                    // Handle JSON file
                    StartCoroutine(LoadJsonFileFromPath(path));

                }
                else if (extension == ".fbx")
                {
                    // Handle FBX file
                    StartCoroutine(LoadFBXFileFromPath(path));
                }
                else
                {
                    errorText.text = "Not a .json or .fbx file";
                }
            }
            else
            {
                UnityEngine.Debug.Log("No file picked.");
            }
        });  // Allow both json and fbx files to be selected
    }

    private IEnumerator LoadJsonFileFromPath(string path)
    {
        if (File.Exists(path))
        {
            // Read the file contents directly
            string fileContents = File.ReadAllText(path);
            TextAsset jsonFile = new TextAsset(fileContents);

            // Now load the JSON data
            ResetModelState();
            LoadJsonFile(jsonFile); // Your method to handle the loaded JSON file
        }
        else
        {
            UnityEngine.Debug.LogError("Error loading file: File not found");
            errorText.text = "Error loading file: File not found " + path;
        }

        // Ensure coroutine exits properly
        yield break;
    }

    private IEnumerator LoadFBXFileFromPath(string path)
    {
        if (File.Exists(path))
        {
            // Read the file bytes directly from the local path
            byte[] fileData = File.ReadAllBytes(path);

            // Optionally, write the file to a temporary location
            string tempPath = Path.Combine(UnityEngine.Application.persistentDataPath, Path.GetFileName(path));
            File.WriteAllBytes(tempPath, fileData);

            // Call the function to load the FBX model using TriLib
            LoadFBXModel(tempPath);
        }
        else
        {
            UnityEngine.Debug.LogError("File not found: " + path);
            errorText.text = "Error loading file: File not found";
        }

        // Ensure the coroutine completes correctly
        yield break;
    }

    private void LoadFBXModel(string filepath)
    {
        ResetModelState();
        int myToken = modelLoadToken;
        var assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions();
        assetLoaderOptions.UseUnityNativeNormalCalculator = true;
        assetLoaderOptions.AlphaMaterialMode = AlphaMaterialMode.Transparent;

        AssetLoader.LoadModelFromFile(
            filepath,
            (context) => OnLoad(context, myToken),
            (context) => OnMaterialsLoad(context, myToken),
            (context, progress) => OnProgress(context, progress, myToken),
            (error) => OnError(error, myToken),
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
    private void TagNthLevelChildren(GameObject parent, string tag, int level)
    {
        try
        {
            TagChildrenAtLevel(parent.transform, tag, level, 0);
        }
        catch { }
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
            BuildSimModel(chaseCompanion);

        }
    }

    // For external callers (e.g. EventLoader.cs, when an event file's own
    // header.model_file names a model) that just want a named model built
    // normally -- unlike LoadTourFile(), this does not hide components
    // afterward for a tour-style reveal. This model's own header.event_file
    // (if any) is not chased, since the event file that led here already
    // takes precedence.
    //
    // The model dropdown is switched to the loaded file without firing its
    // onValueChanged (which is wired to OnFileSelected + LoadFile and so
    // used to start a second, companion-chasing load). If the reference
    // doesn't match any available model, the previous model is cleared and
    // the model is left empty (dropdown blank) -- previously BuildSimModel
    // would just rebuild whatever the dropdown still had selected.
    public void LoadModelFile(string newFilename)
    {
        int index = FileSelection.FindIndex(fileNames, newFilename);
        if (index >= 0)
        {
            filename = fileNames[index];
        }
        else
        {
            // A file hidden from the list still loads when referenced.
            int hiddenIndex = FileSelection.FindIndex(hiddenFileNames, newFilename);
            if (hiddenIndex < 0)
            {
                ClearMissingModel(newFilename);
                return;
            }
            filename = hiddenFileNames[hiddenIndex];
        }

        ShowCurrentSelection();
        LoadFile(chaseCompanion: false);
    }

    // Shows the current model in the dropdown: its entry if listed, its
    // name (without adding an entry) if it's a hidden file, or blank.
    private void ShowCurrentSelection()
    {
        int index = FileSelection.FindExact(fileNames, filename);
        if (index < 0 && FileSelection.FindExact(hiddenFileNames, filename) >= 0)
            FileSelection.ShowUnlisted(fileDropdown, filename);
        else
            FileSelection.ShowSelection(fileDropdown, index);
    }

    // The hidden model `filename` refers to, or null if it isn't one. Hidden
    // models have no dropdown entry, so BuildSimModel can't find them
    // through the selected index like listed ones.
    private string HiddenModelForCurrentFile()
    {
        if (FileSelection.FindIndex(fileNames, filename) >= 0) return null;
        int hiddenIndex = FileSelection.FindIndex(hiddenFileNames, filename);
        return hiddenIndex >= 0 ? hiddenFileNames[hiddenIndex] : null;
    }

    // Empties the scene of the current model and its title text, for a
    // companion reference that points to a model that doesn't exist.
    private void ClearMissingModel(string missingFilename)
    {
        UnityEngine.Debug.LogWarning($"Model file \"{missingFilename}\" not found -- leaving the model empty.");

        // Also invalidates any FBX load still in flight (modelLoadToken).
        ResetModelState();
        loadingModel = false;
        activeModel = null;

        detectorText.text = "";
        detectorPartAlphas.Clear();
        if (errorText != null && errorText.text.StartsWith("Loading model"))
            errorText.text = "";

        explodeSlider.value = 1f;
        lastSliderValue = 1f;

        // Cleared rather than left at the previous file, so selecting any
        // dropdown entry afterward -- including the one that was loaded
        // before -- actually loads it.
        filename = "";
        lastFilename = null;
        FileSelection.ShowSelection(fileDropdown, -1);
    }

    public void ResetModelState()
    {
        loadingModel = true;
        // See modelLoadToken's declaration -- invalidates any FBX load
        // still in flight from a previous selection.
        modelLoadToken++;
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

    public void BuildSimModel(bool chaseCompanion = true)
    {
        ResetModelState();  // Call the new method to reset everything

        int selectedIndex = fileDropdown.value;
        string hiddenModel = HiddenModelForCurrentFile();

        if (hiddenModel != null)
        {
            TextAsset jsonFile = Resources.LoadAll<TextAsset>("Models").FirstOrDefault(f => f.name == hiddenModel);
            if (jsonFile != null)
            {
                LoadJsonFile(jsonFile, chaseCompanion);
            }
        }
        // Check if the selected index matches a JSON file or a 3D model
        else if (jsonFileIndexes.Contains(selectedIndex))
        {
            // Load JSON file
            TextAsset[] files = Resources.LoadAll<TextAsset>("Models");
            string filename = displayNames[selectedIndex];

            TextAsset jsonFile = files.FirstOrDefault(f => f.name == filename);
            if (jsonFile != null)
            {
                LoadJsonFile(jsonFile, chaseCompanion);
            }
        }
        else if (objectIndexes.Contains(selectedIndex))
        {
            GameObject modelsParent = GameObject.Find("Models");
            if (modelsParent != null)
            {
                Transform selectedObject = modelsParent.transform.GetChild(objectIndexes.IndexOf(selectedIndex));
                if (selectedObject != null)
                {
                    selectedObject.gameObject.SetActive(true);
                    activeModel = selectedObject.gameObject;
                    detectorText.text = selectedObject.name;
                    try
                    {
                        TagChildrenAtLevel(selectedObject.transform, "Detector", 1, 0);
                    }
                    catch { }
                }
            }
        }

        loadingModel = false;
    }

    void LoadJsonFile(TextAsset jsonFile, bool chaseCompanion = true)
    {
        try
        {
            fileContents = jsonFile.text;

            // Parse JSON file to EventDataWrapper class
            ComponentListWrapper componentListWrapper = ModelFile.FromJson(fileContents);

            string version = componentListWrapper.header.version;

            if (VersionCheck.IsCompatible(version, targetVersion, compatibleVersions))
            {
                string unit = componentListWrapper.header.length_unit;
                detectorText.text = LegacyField.Resolve(componentListWrapper.header.title, componentListWrapper.header.detector);

                if (string.Equals(unit, "m"))
                {
                    scale = 1.0f;
                }
                else if (string.Equals(unit, "cm"))
                {
                    scale = 0.01f;
                }
                else if (string.Equals(unit, "mm"))
                {
                    scale = 0.001f;
                }
                scale = scale * componentListWrapper.header.scale;

                if (chaseCompanion && !string.IsNullOrEmpty(componentListWrapper.header.event_file) && eventLoader != null)
                {
                    eventLoader.LoadEventFile(componentListWrapper.header.event_file);
                }

                var sortedComponents = componentListWrapper.components
                        .OrderBy(c => c.index == -1 ? int.MaxValue : c.index)
                        .ToList();

                ComponentsBuildResult buildResult = ModelGeometry.BuildComponents(sortedComponents, scale, lineThickness, collidersOn);
                // Components the file described but that couldn't be built (e.g. a
                // hexahedron without 8 valid vertices) -- shown so the file's author
                // sees them; Escape or a click clears the message.
                if (buildResult.Warnings.Count > 0)
                    errorText.text = string.Join("\n", buildResult.Warnings);
                detectorParts.AddRange(buildResult.DetectorParts);
                lineObjects.AddRange(buildResult.LineObjects);
                nameTagObjects.AddRange(buildResult.NameTagObjects);
                pivots.AddRange(buildResult.Pivots);

                detectorPartAlphas.Clear();

                foreach (var go in detectorParts)
                {
                    var renderer = go.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        detectorPartAlphas.Add(renderer.material.color.a);
                    }
                    else
                    {
                        detectorPartAlphas.Add(0f);
                    }
                }
                GameObject root = new GameObject("Loaded JSON Model");

                foreach (GameObject part in detectorParts)
                {
                    part.transform.SetParent(root.transform, true);
                }

                activeModel = root;
            }
            else
            {
                errorText.text = "Model JSON file is not version " + targetVersion + ".";
                UnityEngine.Debug.LogError("Model JSON file is not version " + targetVersion + ".");
            }
        }
        catch (Exception ex)
        {
            errorText.text = "Error loading model file: " + ex.Message;
            UnityEngine.Debug.LogError("Error loading model file: " + ex.Message);
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
        // Load all JSON files from the Resources/Models folder
        TextAsset[] files = Resources.LoadAll<TextAsset>("Models");

        if (files.Length == 0)
        {
            errorText.text = "No files found in Resources/Models.";
            return;
        }

        fileDropdown.ClearOptions();
        fileNames.Clear();
        displayNames.Clear();
        jsonFileIndexes.Clear();
        objectIndexes.Clear();
        hiddenFileNames.Clear();

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

        // Add JSON file names
        foreach (TextAsset file in files)
        {
            string fileName = file.name;

            if (FileHeaderFlags.IsHiddenJson(file.text))
            {
                hiddenFileNames.Add(fileName);
                continue;
            }

            fileNames.Add(fileName);
            displayNames.Add(fileName);
            jsonFileIndexes.Add(displayNames.Count - 1); // Track JSON file index
        }



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
}
