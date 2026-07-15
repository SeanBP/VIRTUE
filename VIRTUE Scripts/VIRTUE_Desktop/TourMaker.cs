using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
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
    public GameObject normalMenu; // The normal menu to hide
    public GameObject tourMenu;   // Tour controls
    public GameObject tourText;
    public Text titleText;
    public Text bodyText;
    public Text errorText; // for printing errors

    private List<string> tourFiles = new List<string>();
    private TourFile currentTour;
    private int currentSceneIndex = 0;
    private bool inTour = false;
    private ComponentMaker componentMaker;
    private EventLoader eventLoader;
    [SerializeField] private CameraController cameraController;
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
        tourText.SetActive(false);
        inTour = true;
    }

    private void LoadTourFilesIntoDropdown()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Tours");

        if (!Directory.Exists(path))
        {
            if (errorText != null)
                errorText.text = "Tours folder not found in StreamingAssets";

            Debug.LogError("Tours folder not found in StreamingAssets");
            return;
        }

        string[] files = Directory.GetFiles(path, "*.json");

        tourDropdown.ClearOptions();
        tourFiles.Clear();

        List<string> displayNames = new List<string>();
        int defaultIndex = -1;

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);

            tourFiles.Add(file);
            displayNames.Add(name);

            if (name == "ePIC_Tour")
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

        string path = tourFiles[index];
        StartCoroutine(LoadTourCoroutine(path));
    }

    private IEnumerator LoadTourCoroutine(string path)
    {
        string jsonText = "";
        normalMenu.SetActive(false);
        try
        {
            using (StreamReader reader = new StreamReader(path))
            {
                jsonText = reader.ReadToEnd();
            }

            currentTour = JsonUtility.FromJson<TourFile>(jsonText);
        }
        catch (System.Exception e)
        {
            if (errorText != null) errorText.text = "Error reading tour file: " + e.Message;
            yield break;
        }

        // Load model
        if (!string.IsNullOrEmpty(currentTour.header.model_file))
        {
            string modelFilePath = Path.Combine(Application.streamingAssetsPath, "Models", currentTour.header.model_file);
            componentMaker.LoadTourFile(modelFilePath);
            while (componentMaker.loadingModel)
                yield return null;
        }

        // Load events
        if (!string.IsNullOrEmpty(currentTour.header.events_file))
        {
            string eventsFilePath = currentTour.header.events_file;
            yield return new WaitUntil(() => eventLoader.activeCoroutines == 0);
            yield return new WaitUntil(() => eventLoader.loadingEvent == false);
            eventLoader.LoadTourFile(eventsFilePath);
            yield return new WaitUntil(() => eventLoader.loadingTour == false);

        }

        
        tourMenu.SetActive(true);
        tourText.SetActive(true);
        currentSceneIndex = 0;
        
        ShowScene(currentSceneIndex);
    }

    private void ShowScene(int sceneIndex)
    {
        if (sceneIndex < 0 || sceneIndex >= currentTour.scenes.Count)
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
        cameraController?.MoveTargetTo(focusPos);

        // UI text
        titleText.text = scene.text.title;
        bodyText.text = scene.text.body;
    }

    public void ReplayScene()
    {
        if (!playerController.isMoving & !cameraController.isMoving)
        {
            ShowScene(currentSceneIndex);
        }
    }

    public void RestartTour()
    {
        if (!playerController.isMoving & !cameraController.isMoving)
        {
            currentSceneIndex = 0;
            ShowScene(currentSceneIndex);
        }
    }

    public void NextScene()
    {
        if (!playerController.isMoving & !cameraController.isMoving)
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
        if (!playerController.isMoving & !cameraController.isMoving)
        {
            if (currentTour == null) return;
            currentSceneIndex--;
            if (currentSceneIndex < 0)
                currentSceneIndex = 0;

            ShowScene(currentSceneIndex);
        }
    }

    void Update()
    {
        if (inTour)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                NextScene();
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                PreviousScene();
            }
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                RestartTour();
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                ReplayScene();
            }
        }
    }
}
