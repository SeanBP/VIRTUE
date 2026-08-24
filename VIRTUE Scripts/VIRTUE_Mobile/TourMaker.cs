using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VirtueCore.Tours;
using VirtueCore.Shared;

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
    private string targetVersion = "3.2.1";
    // 3.2.0 tour files remain compatible -- see EventLoader/ComponentMaker's
    // compatibleVersions for why.
    private List<string> compatibleVersions = new List<string> { "3.2.0" };

    private ComponentMaker componentMaker;
    private EventLoader eventLoader;

    [SerializeField] private PlayerController playerController;

    // Tracks the currently-running tour-loading coroutine, so selecting a
    // new tour while one is still loading aborts it via StopCoroutine
    // instead of racing it. tourLoadToken is a second line of defense,
    // checked right before the tour is actually applied (tourMenu shown,
    // ShowScene called) -- mirrors the modelLoadToken pattern in
    // ComponentMaker, in case this coroutine is ever kicked off some other
    // way that bypasses the StopCoroutine guard in StartTour().
    private Coroutine activeTourLoadCoroutine;
    private int tourLoadToken = 0;

    void Start()
    {
        componentMaker = FindAnyObjectByType<ComponentMaker>();
        eventLoader = FindAnyObjectByType<EventLoader>();

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

        if (activeTourLoadCoroutine != null)
        {
            StopCoroutine(activeTourLoadCoroutine);
            activeTourLoadCoroutine = null;
        }

        tourLoadToken++;
        activeTourLoadCoroutine = StartCoroutine(LoadTourCoroutine(tourFiles[index], tourLoadToken));

    }

    private IEnumerator LoadTourCoroutine(TextAsset jsonAsset, int token)
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

        if (!VersionCheck.IsCompatible(currentTour.header.version, targetVersion, compatibleVersions))
        {
            if (errorText != null)
                errorText.text = "Tour JSON File not version " + targetVersion;

            Debug.LogError("Tour JSON File not version " + targetVersion);
            yield break;
        }

        // Load model
        if (!string.IsNullOrEmpty(currentTour.header.model_file))
        {
            componentMaker.LoadTourFile(currentTour.header.model_file);

            while (componentMaker.loadingModel)
                yield return null;
        }

        // A newer tour selection has since started -- this load was
        // effectively aborted. Bail out before touching the shared UI/state
        // below so it can't clobber whatever the newer selection already
        // applied.
        if (token != tourLoadToken)
            yield break;

        // Load events
        if (!string.IsNullOrEmpty(currentTour.header.events_file))
        {
            yield return new WaitUntil(() => eventLoader.activeCoroutines == 0);
            yield return new WaitUntil(() => eventLoader.loadingEvent == false);

            eventLoader.LoadTourFile(currentTour.header.events_file);

            yield return new WaitUntil(() => eventLoader.loadingTour == false);
        }

        if (token != tourLoadToken)
            yield break;

        tourMenu.SetActive(true);

        currentSceneIndex = 0;
        activeTourLoadCoroutine = null;
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
