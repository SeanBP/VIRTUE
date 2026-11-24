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
    private string targetVersion = "3.2.3";
    // 3.2.0 model files remain compatible: the only change since then is the
    // header field rename (detector -> title), which this parser already
    // accepts via the legacy detector alias above.
    private List<string> compatibleVersions = new List<string> { "3.2.0", "3.2.1", "3.2.2" };
    private List<string> fileNames = new List<string>();
    private List<string> displayNames = new List<string>();
    // Model files whose header has "hidden": true -- kept out of the
    // dropdown (fileNames/displayNames) but still loadable by reference.
    private List<string> hiddenFileNames = new List<string>();
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
        int initialIndex = FileSelection.FindIndex(fileNames, "EIC_ePIC.json");
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

    // ---- Returning from a tour ----
    // A tour swaps in its own model and fades components/lines, so the
    // model the user had (and how they had it set up) is saved when the tour
    // starts and rebuilt when it ends, instead of reloading the whole scene.
    private struct TourReturnState
    {
        public string loadedFile;   // "" if no model was loaded
        public float explode;
        public bool wireOn;
        public bool tagsActive;
        public bool collidersOn;
        public bool modelHidden;
        public bool figuresActive;
    }

    private TourReturnState? tourReturnState;
    private Coroutine tourLoadCoroutine;
    private Coroutine activateComponentsCoroutine;

    public void CaptureTourReturnState()
    {
        tourReturnState = new TourReturnState
        {
            // lastFilename is the model actually built (null once a missing
            // companion has emptied it); filename may only be a selection.
            loadedFile = lastFilename ?? "",
            explode = explodeSlider.value,
            wireOn = wireOn,
            tagsActive = tagsActive,
            collidersOn = collidersOn,
            modelHidden = menagerieActive,
            figuresActive = figures.activeSelf
        };
    }

    public void RestoreTourReturnState()
    {
        if (tourLoadCoroutine != null) { StopCoroutine(tourLoadCoroutine); tourLoadCoroutine = null; }
        if (activateComponentsCoroutine != null) { StopCoroutine(activateComponentsCoroutine); activateComponentsCoroutine = null; }

        if (!tourReturnState.HasValue)
            return;

        TourReturnState state = tourReturnState.Value;
        tourReturnState = null;
        StartCoroutine(RestoreTourReturnStateCoroutine(state));
    }

    private IEnumerator RestoreTourReturnStateCoroutine(TourReturnState state)
    {
        if (string.IsNullOrEmpty(state.loadedFile))
        {
            ClearModel();
        }
        else
        {
            // Rebuilt even when the tour used this same file, since the tour
            // faded its components and lines.
            filename = state.loadedFile;
            lastFilename = null;
            ShowCurrentSelection();
            LoadFile(chaseCompanion: false);

            while (loadingModel)
                yield return null;

            // A rebuild starts with every toggle off and explode at 1, so
            // put back whatever the user had.
            explodeSlider.value = state.explode;
            if (state.wireOn) ToggleLines();
            if (state.tagsActive) ToggleTags();
            if (state.collidersOn) ToggleColliders();
            if (state.modelHidden) ToggleMenagerie();
        }

        if (state.figuresActive != figures.activeSelf)
            ToggleFigures();
    }

    public void LoadTourFile(string newFilename)
    {
        tourLoadCoroutine = StartCoroutine(LoadTourFileCoroutine(newFilename));
    }

    private IEnumerator LoadTourFileCoroutine(string newFilename)
    {
        // Set filename and start loading the model. A tour manages its own
        // model/event pairing explicitly (per-scene), so don't chase this
        // model's header.event_file.
        filename = ResolveFileReference(newFilename);
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
    //
    // The model dropdown is switched to the loaded file. If the reference
    // doesn't match any available model, the previous model is cleared and
    // the model is left empty (dropdown blank) instead of loading anything.
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
            FileSelection.ShowUnlisted(fileDropdown, FileListing.DisplayName(filename));
        else
            FileSelection.ShowSelection(fileDropdown, index);
    }

    private static string ModelsFolder
    {
        get { return Path.Combine(UnityEngine.Application.streamingAssetsPath, "Models"); }
    }

    // The list entry a file reference points to -- a bare name, a path
    // relative to Models (files can be in subfolders), or an absolute path
    // inside Models -- including hidden files. Unchanged if nothing matches.
    private string ResolveFileReference(string reference)
    {
        string relative = FileListing.ToRelative(ModelsFolder, reference);

        int index = FileSelection.FindIndex(fileNames, relative);
        if (index >= 0) return fileNames[index];

        index = FileSelection.FindIndex(hiddenFileNames, relative);
        if (index >= 0) return hiddenFileNames[index];

        return reference;
    }

    // Empties the scene of the current model and its title text, for a
    // companion reference that points to a model that doesn't exist.
    private void ClearMissingModel(string missingFilename)
    {
        UnityEngine.Debug.LogWarning($"Model file \"{missingFilename}\" not found -- leaving the model empty.");
        ClearModel();
    }

    // Empties the scene of the current model and its title text, leaving
    // nothing loaded and the dropdown blank.
    private void ClearModel()
    {
        // Invalidates any FBX load still in flight, same as buildSimModel.
        modelLoadToken++;
        ResetModelState();
        loadingModel = false;

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

        // The destroyed parts' materials and meshes aren't freed with them.
        AssetCleanup.UnloadUnusedSoon(this);
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

        string path = ModelsFolder;
        string filePath = FileListing.FullPath(path, filename);

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
                    ModelFile.FromJson(fileContents);

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
                    // Components the file described but that couldn't be built (e.g. a
                    // hexahedron without 8 valid vertices) -- shown so the file's author
                    // sees them; Escape or a click clears the message.
                    if (buildResult.Warnings.Count > 0)
                        errorText.text = string.Join("\n", buildResult.Warnings);
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
                    errorText.text = "Model JSON file is not version " + targetVersion + ".";
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
        // Name tags face the camera -- only worth doing while they're shown
        // (a large model has thousands of them).
        if (!tagsActive || Camera.main == null)
            return;

        Quaternion cameraRotation = Camera.main.transform.rotation;
        for (int i = 0; i < pivots.Count; i++)
        {
            pivots[i].transform.rotation = cameraRotation;

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
        hiddenFileNames.Clear();
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

        // All accepted files in Models and its subfolders, as paths relative
        // to Models ("LHC/ATLAS.json"); top-level files keep plain names.
        List<string> allFiles = FileListing.ListFiles(path,
            file => acceptedExtensions.Contains(Path.GetExtension(file).ToLower()));

        foreach (string fileName in allFiles)
        {
            string file = FileListing.FullPath(path, fileName);
            string extension = Path.GetExtension(fileName).ToLower();

            if (extension == ".json" && FileHeaderFlags.IsHidden(file))
            {
                hiddenFileNames.Add(fileName);
                continue;
            }

            fileNames.Add(fileName); // Store for selection handling
            displayNames.Add(FileListing.DisplayName(fileName)); // "LHC/ATLAS"
        }



        // Add file names to the dropdown
        fileDropdown.AddOptions(displayNames);
    }

    // Re-reads the model list from disk (PauseMenu calls this whenever the
    // menu opens), so files added or removed while the app is running show
    // up. The dropdown keeps showing the current model, or goes blank if its
    // file no longer exists. Nothing is loaded or unloaded.
    public void RefreshFileList()
    {
        LoadFilesIntoDropdown();
        ShowCurrentSelection();
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
