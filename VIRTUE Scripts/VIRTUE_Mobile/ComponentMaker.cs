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
using static ComponentMaker;
using System.Linq;
using TriLibCore;
using TriLibCore.General;
using TriLibCore.Mappers;
using TriLibCore.Utils;
using UnityEngine.Networking;
using NativeFilePickerNamespace;
using UnityEngine.Android;
#pragma warning disable 0618

public class ComponentMaker : MonoBehaviour
{
    [System.Serializable]
    public class ComponentListWrapper
    {
        public Header header;
        public Components[] components;
    }

    [System.Serializable]
    public class Header
    {
        public string version;
        public string detector;
        public string length_unit = "m";
        public float scale = 1.0f;
    }


    [System.Serializable]
    public class Components
    {
        public string type;
        public int index = -1;
        public string name = "";
        public int sides;
        public float[] position = new float[] { 0f, 0f, 0f };
        public Radii radii;
        public Length length;
        public float inner_offset = 0f;
        public float[] euler_angles_deg = new float[] { 0f, 0f, 0f };

        public float[] size = new float[] { 1f, 1f, 1f };

        public float[] color_rgba = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
    }
    [System.Serializable]
    public class Radii
    {
        public float[] left = new float[] { -1f, -1f };  // Array for left side radii [rmin1, rmax1]
        public float[] right = new float[] { -1f, -1f }; // Array for right side radii [rmin2, rmax2]
    }

    [System.Serializable]
    public class Length
    {
        public float outer = -1f;
        public float inner = -1f;
    }

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
    private string targetVersion = "3.1.0";
    private List<string> compatibleVersions = new List<string> { "3.0.0" };
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
    public GameObject figures;
    public UnityEngine.UI.Text figureText;
    List<int> jsonFileIndexes = new List<int>();
    List<int> objectIndexes = new List<int>();
    public GameObject models;
    private List<float> detectorPartAlphas = new List<float>();
    private string modelTextCache = "";
    private GameObject activeModel;

    private List<string> acceptedExtensions = new List<string>
    {
        ".json"
    };

    // Start is called before the first frame update
    void Start()
    {
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
        // Set filename and start loading the model
        filename = newFilename;

        int index = fileNames.FindIndex(f =>
        string.Equals(f, Path.GetFileNameWithoutExtension(newFilename),
        StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            fileDropdown.value = index;
        }

        LoadFile();  // This should set loadingModel = true internally

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
        if (figures.active)
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

        detectorText.text = "";

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
                    //StartCoroutine(LoadFBXFileFromPath(path));
                    errorText.text = "FBX files are not supported";
                }
                else
                {
                    errorText.text = "Not a .json file";
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
        var assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions();
        assetLoaderOptions.UseUnityNativeNormalCalculator = true;
        assetLoaderOptions.AlphaMaterialMode = AlphaMaterialMode.Transparent;

        AssetLoader.LoadModelFromFile(filepath, OnLoad, OnMaterialsLoad, OnProgress, OnError, null, assetLoaderOptions);
    }


    private void OnBeginLoad(bool anyModelSelected)
    {
        loadingModel = true;
    }


    private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
    {
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


    private void OnError(IContextualizedError contextualizedError)
    {
        errorText.text = $"Error: {contextualizedError.ToString()}";
    }

    private void OnLoad(AssetLoaderContext assetLoaderContext)
    {
        explodeSlider.value = 1f;
        var myLoadedGameObject = assetLoaderContext.RootGameObject;
        TagNthLevelChildren(myLoadedGameObject, "Detector", 2);
        TagNthLevelChildren(myLoadedGameObject, "Detector", 1);
        myLoadedGameObject.SetActive(false);
        detectorParts.Add(myLoadedGameObject);
        activeModel = myLoadedGameObject;
        loadingModel = false;
    }

    private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
    {
        var myLoadedGameObject = assetLoaderContext.RootGameObject;
        myLoadedGameObject.SetActive(true);
        MeshRenderer[] renderers = myLoadedGameObject.GetComponentsInChildren<MeshRenderer>();

        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material.shader.name != "Standard (Specular setup)")
                {
                    material.shader = Shader.Find("Standard (Specular setup)");
                }
            }
        }
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
        if (figures.active)
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

    public void LoadFile()
    {

        if (!String.Equals(filename, lastFilename) && loadingModel == false)
        {
            explodeSlider.value = 1f;
            lastSliderValue = 1f;
            lastFilename = filename;
            BuildSimModel();

        }
    }

    public void ResetModelState()
    {
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

    public void BuildSimModel()
    {
        ResetModelState();  // Call the new method to reset everything

        int selectedIndex = fileDropdown.value;

        // Check if the selected index matches a JSON file or a 3D model
        if (jsonFileIndexes.Contains(selectedIndex))
        {
            // Load JSON file
            TextAsset[] files = Resources.LoadAll<TextAsset>("Models");
            string filename = displayNames[selectedIndex];

            TextAsset jsonFile = files.FirstOrDefault(f => f.name == filename);
            if (jsonFile != null)
            {
                LoadJsonFile(jsonFile);
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

    void LoadJsonFile(TextAsset jsonFile)
    {
        try
        {
            fileContents = jsonFile.text;

            // Parse JSON file to EventDataWrapper class
            ComponentListWrapper componentListWrapper = JsonUtility.FromJson<ComponentListWrapper>(fileContents);

            string version = componentListWrapper.header.version;

            if (string.Equals(version, targetVersion) || compatibleVersions.Contains(version))
            {
                string unit = componentListWrapper.header.length_unit;
                detectorText.text = componentListWrapper.header.detector;

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

                int detCount = 0;
                var sortedComponents = componentListWrapper.components
                        .OrderBy(c => c.index == -1 ? int.MaxValue : c.index)
                        .ToList();
                foreach (var data in sortedComponents)
                {
                    string name = data.name;
                    int index = data.index;
                    
                    if (index == -1)
                    {
                        index = detCount;
                    }
                    float[] position = data.position;
                    float[] eulerAngle = data.euler_angles_deg;
                    float[] rgba = data.color_rgba;
                    string typeLower = data.type.ToLowerInvariant();

                    if (typeLower.Contains("t"))
                    {
                        int sides = data.sides;

                        float[] rLeft = data.radii.left;
                        float[] rRight = data.radii.right;
                        float[] length = new float[2];
                        length[0] = data.length.inner;
                        length[1] = data.length.outer;

                        if (rLeft[0] == -1) rLeft = rRight;
                        else if (rRight[0] == -1) rRight = rLeft;

                        if (length[0] == -1) length[0] = length[1];
                        else if (length[1] == -1) length[1] = length[0];

                        float offsetIn = data.inner_offset;
                        
                        MakeToroid(name, sides, position, rLeft, rRight, length, offsetIn, eulerAngle, rgba, componentListWrapper.components.Length - index);
                        detCount++;
                    }
                    else if (typeLower.Contains("b"))
                    {
                        float[] size = data.size;
                        MakeBlock(name, position, size, eulerAngle, rgba, componentListWrapper.components.Length - index, true);
                        detCount++;
                    }
                    else if (typeLower.Contains("s"))
                    {
                        float[] size = data.size;
                        MakeSpheroid(name, position, size, eulerAngle, rgba, componentListWrapper.components.Length - index);
                        detCount++;
                    }
                }
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
                errorText.text = "Model JSON File not version " + targetVersion;
                UnityEngine.Debug.LogError("Model JSON File not version " + targetVersion);
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

    void MakeBlock(String name, float[] position, float[] size, float[] eulerAngle, float[] rgba, int renderQueue, bool isReal)
    {
        // Create a new GameObject for the prism
        GameObject prism = new GameObject("Detector Piece", typeof(MeshFilter), typeof(MeshRenderer));
        prism.tag = "Detector";

        // Create the mesh
        Mesh mesh = new Mesh();

        // Set up vertices based on size array
        Vector3[] vertices = new Vector3[8]
        {
        new Vector3(-scale*size[0] / 2, -scale*size[1] / 2, -scale*size[2] / 2),
        new Vector3(scale*size[0] / 2, -scale*size[1] / 2, -scale*size[2] / 2),
        new Vector3(scale*size[0] / 2, scale*size[1] / 2, -scale*size[2] / 2),
        new Vector3(-scale*size[0] / 2, scale*size[1] / 2, -scale*size[2] / 2),
        new Vector3(-scale*size[0] / 2, -scale*size[1] / 2, scale*size[2] / 2),
        new Vector3(scale*size[0] / 2, -scale*size[1] / 2, scale*size[2] / 2),
        new Vector3(scale*size[0] / 2, scale*size[1] / 2, scale*size[2] / 2),
        new Vector3(-scale*size[0] / 2, scale*size[1] / 2, scale*size[2] / 2)
        };

        // Define triangles
        int[] triangles = new int[]
        {
        0, 2, 1, 0, 3, 2, // Back face
        4, 5, 6, 4, 6, 7, // Front face
        0, 1, 5, 0, 5, 4, // Bottom face
        2, 3, 7, 2, 7, 6, // Top face
        0, 4, 7, 0, 7, 3, // Left face
        1, 2, 6, 1, 6, 5,  // Right face

        2, 3, 0, 1, 2, 0, // Faces in reverse
        7, 6, 4, 6, 5, 4,
        4, 5, 0, 5, 1, 0,
        6, 7, 2, 7, 3, 2,
        3, 7, 0, 7, 4, 0,
        5, 6, 1, 6, 2, 1
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;

        prism.GetComponent<MeshFilter>().mesh = mesh;
        if (isReal)
        {
            MeshCollider meshCollider = prism.AddComponent<MeshCollider>();
            meshCollider.enabled = collidersOn;
        }


        // Material and color setup
        Material material = new Material(Shader.Find("Transparent/Diffuse"));

        Color color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);

        material.color = color;
        material.renderQueue = renderQueue;

        prism.GetComponent<MeshRenderer>().sharedMaterial = material;

        // Create lines to outline the edges of the rectangular prism
        GameObject[] lines = new GameObject[12];
        int[,] edges = new int[12, 2]
        {
        {0, 1}, {1, 2}, {2, 3}, {3, 0}, // Back face
        {4, 5}, {5, 6}, {6, 7}, {7, 4}, // Front face
        {0, 4}, {1, 5}, {2, 6}, {3, 7}  // Connecting edges
        };

        Material lineMaterial = new Material(Shader.Find("Sprites/Default"));

        if (isReal)
        {
            for (int i = 0; i < 12; i++)
            {
                lines[i] = new GameObject("Line");
                LineRenderer lineRenderer = lines[i].AddComponent<LineRenderer>();
                lineRenderer.positionCount = 2;
                lineRenderer.useWorldSpace = false;

                lineRenderer.startWidth = lineThickness;
                lineRenderer.endWidth = lineThickness;

                lineRenderer.SetPosition(0, vertices[edges[i, 0]]);
                lineRenderer.SetPosition(1, vertices[edges[i, 1]]);

                lineRenderer.material = lineMaterial;
                lineRenderer.material.renderQueue = -1;

                lines[i].transform.parent = prism.transform;
                lines[i].transform.localPosition = Vector3.zero;
                lines[i].SetActive(false);
                lines[i].tag = "Line";
                lineObjects.Add(lines[i]);
            }
        }

        // Set orientation and position
        prism.transform.eulerAngles = new Vector3(eulerAngle[0], -eulerAngle[1], eulerAngle[2]);
        prism.transform.position = new Vector3(-scale * position[0], scale * position[1], scale * position[2]);
        detectorParts.Add(prism);
        if (!String.Equals(name, ""))
        {
            CreateNameTag(prism, name, eulerAngle, renderQueue);
        }
    }

    public void MakeSpheroid(string name, float[] position, float[] size, float[] eulerAngle, float[] rgba, int renderQueue)
    {
        float[] clear = { 0, 0, 0, 0 };

        // --- build hidden supporting block ---
        MakeBlock(name, position, size, eulerAngle, clear, renderQueue, false);
        GameObject hiddenBlock = detectorParts[detectorParts.Count - 1]; // last created object

        // --- build visible spheroid ---
        GameObject spheroid = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        spheroid.tag = "Detector";

        // parent the hidden block under the sphere
        hiddenBlock.transform.parent = spheroid.transform;
        hiddenBlock.SetActive(false);

        // remove hidden block from primary listing
        detectorParts.Remove(hiddenBlock);

        // material
        Material material = new Material(Shader.Find("Transparent/Diffuse"));
        material.color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
        material.renderQueue = renderQueue;
        spheroid.GetComponent<Renderer>().material = material;

        // wireframe circles
        CreateCircle(spheroid, Vector3.right);
        CreateCircle(spheroid, Vector3.up);
        CreateCircle(spheroid, Vector3.forward);

        // transform
        spheroid.transform.localScale = new Vector3(scale * size[0], scale * size[1], scale * size[2]);
        spheroid.transform.position = new Vector3(-scale * position[0], scale * position[1], scale * position[2]);
        spheroid.transform.eulerAngles = new Vector3(eulerAngle[0], -eulerAngle[1], eulerAngle[2]);

        MeshCollider meshCollider = spheroid.AddComponent<MeshCollider>();
        meshCollider.enabled = collidersOn;
        Destroy(spheroid.GetComponent<SphereCollider>());

        // *** add only the spheroid as the tour-visible "component" ***
        detectorParts.Add(spheroid);
    }

    void CreateCircle(GameObject parent, Vector3 axis)
    {
        int segments = 64;
        float radius = 0.5f;
        GameObject lineObj = new GameObject("Wireframe Circle");
        LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();

        lineRenderer.positionCount = segments + 1;
        lineRenderer.widthMultiplier = lineThickness;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * 2 * Mathf.PI / segments;
            Vector3 point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
            point = Quaternion.LookRotation(axis) * point;
            lineRenderer.SetPosition(i, point);
        }

        lineObj.transform.parent = parent.transform;
        lineObj.GetComponent<LineRenderer>().useWorldSpace = false;

        lineObj.SetActive(false);
        lineObjects.Add(lineObj);
    }

    void MakeToroid(String name, int sides, float[] position, float[] rLeft, float[] rRight, float[] length, float offsetIn, float[] eulerAngle, float[] rgba, int renderQueue)
    {
        float lengthIn = scale * length[0];
        float lengthOut = scale * length[1];
        offsetIn = scale * offsetIn;
        float innerR = scale * rLeft[0];
        float outerR = scale * rLeft[1];
        float innerR2 = scale * rRight[0];
        float outerR2 = scale * rRight[1];
        float rotate = 0f;
        rotate = (float)rotate * (180.0f / Mathf.PI);

        if (sides % 2 == 0)
        {
            rotate = rotate + (360 / (sides * 2)) + 90;
        }
        else
        {
            rotate = rotate + 90;
        }
        Vector3[] vertices = new Vector3[sides * 4];
        int[] triangles = new int[sides * 12 * 4];
        GameObject[] lines = new GameObject[sides * 8];

        int index = 0;
        int lineIndex = 0;
        for (int i = 0; i < sides; i++)
        {
            float angle = (360f / sides) * i + rotate;
            double theta = Math.PI * angle / 180.0;
            vertices[index] = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), (-lengthOut / 2));
            index++;
            vertices[index] = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (-lengthIn / 2) + offsetIn);
            index++;
            vertices[index] = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), (lengthOut / 2));
            index++;
            vertices[index] = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (lengthIn / 2) + offsetIn);
            index++;


            float angle2 = (360f / sides) * (i + 1) + rotate;
            double theta2 = Math.PI * angle2 / 180.0;
            Vector3 start;
            Vector3 end;
            LineRenderer lr = new LineRenderer();
            Material whiteDiffuseMat = new Material(Shader.Find("Sprites/Default"));
            for (float j = (-0.5f); j <= 0.5f; j++)
            {

                start = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), j * lengthOut);
                end = new Vector3(outerR * (float)Math.Cos(theta2), outerR * (float)Math.Sin(theta2), j * lengthOut);
                if (j > 0)
                {
                    start = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), j * lengthOut);
                    end = new Vector3(outerR2 * (float)Math.Cos(theta2), outerR2 * (float)Math.Sin(theta2), j * lengthOut);
                }
                lines[lineIndex] = new GameObject();

                lines[lineIndex].transform.position = start;
                lines[lineIndex].AddComponent<LineRenderer>();
                lr = lines[lineIndex].GetComponent<LineRenderer>();
                lr.material = whiteDiffuseMat;
                lr.material.renderQueue = -1;
                lr.SetWidth(lineThickness, lineThickness);
                lr.SetPosition(0, start);
                lr.SetPosition(1, end);
                lineIndex++;
                if ((innerR != 0 && j < 0) ^ (innerR2 != 0 && j > 0))
                {
                    start = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                    end = new Vector3(innerR * (float)Math.Cos(theta2), innerR * (float)Math.Sin(theta2), (j * lengthIn) + offsetIn);
                    if (j > 0)
                    {
                        start = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                        end = new Vector3(innerR2 * (float)Math.Cos(theta2), innerR2 * (float)Math.Sin(theta2), (j * lengthIn) + offsetIn);
                    }
                    lines[lineIndex] = new GameObject();

                    lines[lineIndex].transform.position = start;
                    lines[lineIndex].AddComponent<LineRenderer>();
                    lr = lines[lineIndex].GetComponent<LineRenderer>();
                    lr.material = whiteDiffuseMat;
                    lr.material.renderQueue = -1;
                    lr.SetWidth(lineThickness, lineThickness);
                    lr.SetPosition(0, start);
                    lr.SetPosition(1, end);
                    lineIndex++;
                    if (sides <= 1)
                    {
                        start = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), j * lengthOut);
                        end = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                        if (j > 0)
                        {
                            start = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), j * lengthOut);
                            end = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                        }
                        lines[lineIndex] = new GameObject();

                        lines[lineIndex].transform.position = start;
                        lines[lineIndex].AddComponent<LineRenderer>();
                        lr = lines[lineIndex].GetComponent<LineRenderer>();
                        lr.material = whiteDiffuseMat;
                        lr.material.renderQueue = -1;
                        lr.SetWidth(lineThickness, lineThickness);
                        lr.SetPosition(0, start);
                        lr.SetPosition(1, end);
                        lineIndex++;
                    }

                }

            }
            if (sides <= 0)
            {
                start = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), (-lengthOut / 2));
                end = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), (lengthOut / 2));

                lines[lineIndex] = new GameObject();

                lines[lineIndex].transform.position = start;
                lines[lineIndex].AddComponent<LineRenderer>();
                lr = lines[lineIndex].GetComponent<LineRenderer>();
                lr.material = whiteDiffuseMat;
                lr.material.renderQueue = 100;
                lr.SetWidth(lineThickness, lineThickness);
                lr.SetPosition(0, start);
                lr.SetPosition(1, end);
                lineIndex++;


                if (innerR > 0f && innerR2 > 0f)
                {
                    start = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (-lengthIn / 2) + offsetIn);
                    end = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (lengthIn / 2) + offsetIn);
                    lines[lineIndex] = new GameObject();

                    lineObjects.Add(lines[lineIndex]);
                    lines[lineIndex].transform.position = start;
                    lines[lineIndex].AddComponent<LineRenderer>();
                    lr = lines[lineIndex].GetComponent<LineRenderer>();
                    lr.material = whiteDiffuseMat;
                    lr.material.renderQueue = -1;
                    lr.SetWidth(lineThickness, lineThickness);
                    lr.SetPosition(0, start);
                    lr.SetPosition(1, end);
                    lineIndex++;
                }
            }

        }
        index = 0;

        //front and back faces
        for (int i = 0; i < sides; i++)
        {
            for (int j = 0; j <= 2; j = j + 2)
            {
                //side 1
                triangles[index] = (i * 4) + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = ((i * 4) + 1) + j;
                index++;
                triangles[index] = ((i * 4) + 1) + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = ((((i + 1) * 4) % (sides * 4)) + 1) + j;
                index++;

                //side 2
                triangles[index] = ((((i + 1) * 4) % (sides * 4)) + 1) + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = ((i * 4) + 1) + j;
                index++;
                triangles[index] = ((i * 4) + 1) + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = (i * 4) + j;
                index++;
            }
        }

        //inner and outer faces
        for (int i = 0; i < sides; i++)
        {
            for (int j = 0; j <= 1; j++)
            {
                //outer pointing faces
                triangles[index] = (i * 4) + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = (i * 4) + 2 + j;
                index++;
                triangles[index] = (i * 4) + 2 + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = (((i * 4) + 6) % (sides * 4)) + j;
                index++;

                //inner pointing faces
                triangles[index] = (((i * 4) + 6) % (sides * 4)) + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = (i * 4) + 2 + j;
                index++;
                triangles[index] = (i * 4) + 2 + j;
                index++;
                triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                index++;
                triangles[index] = (i * 4) + j;
                index++;
            }
        }

        Mesh mesh = new Mesh();

        mesh.vertices = vertices;
        mesh.triangles = triangles;

        GameObject gameObject = new GameObject("Detector Piece", typeof(MeshFilter), typeof(MeshRenderer));
        gameObject.tag = "Detector";
        gameObject.GetComponent<MeshFilter>().mesh = mesh;
        MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
        meshCollider.enabled = collidersOn;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] != null)
            {

                lines[i].transform.parent = gameObject.transform;
                lines[i].GetComponent<LineRenderer>().useWorldSpace = false;
                lines[i].transform.position = new Vector3(0, 0, 0);
                lineObjects.Add(lines[i]);
                lines[i].SetActive(false);
            }
        }

        Material material = new Material(Shader.Find("Transparent/Diffuse"));
        Color color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);

        material.color = color;
        material.renderQueue = renderQueue;

        gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;

        gameObject.transform.eulerAngles = new Vector3(eulerAngle[0], -eulerAngle[1], eulerAngle[2]);

        gameObject.transform.position = new Vector3(-scale * position[0], scale * position[1], scale * position[2]);
        detectorParts.Add(gameObject);

        if (!String.Equals(name, ""))
        {

            CreateNameTag(gameObject, name, eulerAngle, renderQueue);

        }

    }

    // Function to normalize angles to [0, 360)
    float NormalizeAngle(float angle)
    {
        angle %= 360; // Get the remainder when divided by 360
        if (angle < 0) angle += 360; // Ensure positive angle
        return angle;
    }

    public void CreateNameTag(GameObject gameObject, string name, float[] rot, int renderQueue)
    {

        Vector3[] vertices = gameObject.GetComponent<MeshFilter>().mesh.vertices;
        // Create a new GameObject for the text
        GameObject textObject = new GameObject("NameTagText");
        TextMesh textMesh = textObject.AddComponent<TextMesh>();

        // Set text properties
        textMesh.text = name;
        textMesh.fontSize = 48; // Smaller font size
        textMesh.characterSize = 0.1f; // Smaller character size for better resolution   
        textMesh.alignment = TextAlignment.Center; // Centered text

        // Calculate the upper-right corner of the mesh bounds
        Bounds bounds = gameObject.GetComponent<MeshFilter>().mesh.bounds;
        Vector3 upperRight = new Vector3(bounds.max.x, bounds.max.y, bounds.max.z);

        if (renderQueue % 2 == 0)
        {
            upperRight = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
        }

        // Position the text near the upper-right corner of the mesh
        Vector3 offset = new Vector3(0.2f, 0.2f, 0.0f); // Adjust as needed for spacing
        textObject.transform.position = gameObject.transform.TransformPoint(upperRight + offset);

        if (renderQueue % 2 == 0)
        {
            textObject.transform.position = gameObject.transform.TransformPoint(upperRight - offset);
        }

        // Optionally scale the text if needed
        textObject.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f); // Adjust scale for better sizing

        // Create a new GameObject for the line
        GameObject lineObject = new GameObject("Name Tag Line");
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();

        // Set line material and appearance
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startWidth = lineThickness;
        lineRenderer.endWidth = lineThickness;
        lineRenderer.positionCount = 2;

        // Position of the bottom of the text
        Bounds textBounds = textObject.GetComponent<Renderer>().bounds;
        // Main logic for determining textBottomCenter
        Vector3 textBottomCenter = textBounds.center - new Vector3(0, textBounds.extents.y, 0);

        // Normalize rotation angles
        float normalizedX = NormalizeAngle(rot[0]);
        float normalizedY = NormalizeAngle(rot[1]);
        float normalizedZ = NormalizeAngle(rot[2]);

        bool isAngleBetween90And270 = (normalizedX > 90 && normalizedX < 270) ||
                                       (normalizedY > 90 && normalizedY < 270) ||
                                       (normalizedZ > 90 && normalizedZ < 270);

        if (isAngleBetween90And270 && renderQueue % 2 == 1)
        {
            textBottomCenter = textBounds.center + new Vector3(0, textBounds.extents.y, 0);
        }
        else if (isAngleBetween90And270 && renderQueue % 2 == 0)
        {
            textBottomCenter = textBounds.center - new Vector3(0, textBounds.extents.y, 0);
        }
        else if (renderQueue % 2 == 0)
        {
            textBottomCenter = textBounds.center + new Vector3(0, textBounds.extents.y, 0);
        }

        // Initialize the nearest vertex and minimum distance
        Vector3 nearestVertex = Vector3.zero;
        float minDistance = Mathf.Infinity;

        // Find the nearest vertex to the text's bottom center
        foreach (Vector3 vertex in vertices)
        {
            // Convert local vertex position to world space
            Vector3 worldVertex = gameObject.transform.TransformPoint(vertex);

            // Calculate the distance between the vertex and the text's bottom center
            float distance = Vector3.Distance(textBottomCenter, worldVertex);

            // If this vertex is closer than the previous, update nearest vertex
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestVertex = worldVertex;
            }
        }

        // Assign positions for the line
        lineRenderer.SetPosition(0, textBottomCenter); // Start at the bottom of the text
        lineRenderer.SetPosition(1, nearestVertex); // End at the nearest vertex of the mesh

        lineObject.transform.parent = gameObject.transform;
        lineObject.GetComponent<LineRenderer>().useWorldSpace = false;
        lineObject.transform.position = Vector3.zero;
        lineObject.SetActive(false);

        // Create a parent GameObject for the pivot
        GameObject pivotObject = new GameObject("Text Pivot");
        pivotObject.transform.position = textBottomCenter; // Set the pivot position
        textObject.transform.SetParent(pivotObject.transform); // Make text a child of the pivot
        pivotObject.transform.parent = gameObject.transform;

        // Set the pivot's position to the bottom center
        pivotObject.transform.position = textBottomCenter;

        pivots.Add(pivotObject);

        textObject.SetActive(false);
        textObject.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

        nameTagObjects.Add(textObject);
        nameTagObjects.Add(lineObject);
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
