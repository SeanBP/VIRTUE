using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class TourHeader
{
    public string model_file;
    public string events_file;
    public string version;
}

[System.Serializable]
public class TourText
{
    public string title = "";
    public string body = "";
}

[System.Serializable]
public class CameraSettings
{
    public float[] position = new float[3] { 10f, 0f, 0f };
    public float[] focus = new float[3] { 0f, 0f, 0f };
}

[System.Serializable]
public class EventSettings
{
    public int index = -1;
    public float time_before = 10f;
    public float speed = 5f;
}

[System.Serializable]
public class ModelSettings
{
    public bool all_components = false;
    public List<int> components = new List<int>();
    public List<int> lines_active = new List<int>();
}

[System.Serializable]
public class TourScene
{
    public ModelSettings model_settings = new ModelSettings();
    public EventSettings event_settings = new EventSettings();
    public CameraSettings camera_settings = new CameraSettings();
    public TourText text = new TourText();
}

[System.Serializable]
public class TourFile
{
    public TourHeader header;
    public List<TourScene> scenes;
}

public class TourMaker : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Dropdown tourDropdown;
    public GameObject normalMenu;
    public GameObject tourMenu;
    public Text titleText;
    public Text bodyText;
    public Text errorText;

    private List<TextAsset> tourFiles = new List<TextAsset>();
    private TourFile currentTour;
    private int currentSceneIndex = 0;

    private ComponentMaker componentMaker;
    private EventLoader eventLoader;

    [SerializeField] private PlayerController playerController;

    void Start()
    {
        componentMaker = FindObjectOfType<ComponentMaker>();
        eventLoader = FindObjectOfType<EventLoader>();

        if (componentMaker == null || eventLoader == null)
        {
            if (errorText != null)
                errorText.text = "ComponentMaker or EventLoader not found!";
            return;
        }

        LoadTourFilesIntoDropdown();
        tourMenu.SetActive(false);
    }

    private void LoadTourFilesIntoDropdown()
    {
        TextAsset[] files = Resources.LoadAll<TextAsset>("Tours");

        if (files == null || files.Length == 0)
        {
            if (errorText != null)
                errorText.text = "No tour files found in Resources/Tours";

            Debug.LogError("No tour files found in Resources/Tours");
            return;
        }

        tourDropdown.ClearOptions();
        tourFiles.Clear();

        List<string> displayNames = new List<string>();
        int defaultIndex = -1;

        foreach (TextAsset file in files)
        {
            tourFiles.Add(file);
            displayNames.Add(file.name);

            if (string.Equals(file.name, "ePIC_Tour",
                              System.StringComparison.OrdinalIgnoreCase))
            {
                defaultIndex = displayNames.Count - 1;
            }
        }

        tourDropdown.AddOptions(displayNames);

        if (defaultIndex >= 0)
        {
            tourDropdown.value = defaultIndex;
            tourDropdown.RefreshShownValue();
        }
    }

    public void StartTour()
    {
   
        int index = tourDropdown.value;
        
        if (index < 0 || index >= tourFiles.Count)
            return;

        StartCoroutine(LoadTourCoroutine(tourFiles[index]));
        
    }

    private IEnumerator LoadTourCoroutine(TextAsset jsonAsset)
    {
        normalMenu.SetActive(false);

        string jsonText = "";

        try
        {
            jsonText = jsonAsset.text;
            currentTour = JsonUtility.FromJson<TourFile>(jsonText);
        }
        catch (System.Exception e)
        {
            if (errorText != null)
                errorText.text = "Error reading tour file: " + e.Message;

            yield break;
        }

        // Load model
        if (!string.IsNullOrEmpty(currentTour.header.model_file))
        {
            componentMaker.LoadTourFile(currentTour.header.model_file);

            while (componentMaker.loadingModel)
                yield return null;
        }

        // Load events
        if (!string.IsNullOrEmpty(currentTour.header.events_file))
        {
            yield return new WaitUntil(() => eventLoader.activeCoroutines == 0);
            yield return new WaitUntil(() => eventLoader.loadingEvent == false);

            eventLoader.LoadTourFile(currentTour.header.events_file);

            yield return new WaitUntil(() => eventLoader.loadingTour == false);
        }

        tourMenu.SetActive(true);

        currentSceneIndex = 0;
        ShowScene(currentSceneIndex);
    }

    private void ShowScene(int sceneIndex)
    {
        if (currentTour == null || sceneIndex < 0 || sceneIndex >= currentTour.scenes.Count)
            return;

        TourScene scene = currentTour.scenes[sceneIndex];

        // Model
        componentMaker?.ActivateComponents(scene.model_settings);

        // Event
        eventLoader?.AnimateEvent(scene.event_settings);

        // Player position
        Vector3 playerPos = new Vector3(
            scene.camera_settings.position[0],
            scene.camera_settings.position[1],
            scene.camera_settings.position[2]
        );
        playerController?.MovePlayerTo(playerPos);

        // Camera focus
        Vector3 focusPos = new Vector3(
            scene.camera_settings.focus[0],
            scene.camera_settings.focus[1],
            scene.camera_settings.focus[2]
        );
        playerController?.MoveTargetTo(focusPos);

        // UI text
        if (titleText != null) titleText.text = scene.text.title;
        if (bodyText != null) bodyText.text = scene.text.body;
    }

    public void ReplayScene()
    {
        if (!playerController.isMoving)
        {
            ShowScene(currentSceneIndex);
        }
    }

    public void RestartTour()
    {
        if (!playerController.isMoving)
        {
            currentSceneIndex = 0;
            ShowScene(currentSceneIndex);
        }
    }

    public void NextScene()
    {
        if (!playerController.isMoving)
        {
            if (currentTour == null) return;

            currentSceneIndex++;
            if (currentSceneIndex >= currentTour.scenes.Count)
                currentSceneIndex = currentTour.scenes.Count - 1;

            ShowScene(currentSceneIndex);
        }
    }

    public void PreviousScene()
    {
        if (!playerController.isMoving)
        {
            if (currentTour == null) return;

            currentSceneIndex--;
            if (currentSceneIndex < 0)
                currentSceneIndex = 0;

            ShowScene(currentSceneIndex);
        }
    }
}