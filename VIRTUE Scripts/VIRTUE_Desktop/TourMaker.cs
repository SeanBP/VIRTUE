using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using TMPro;
using VirtueCore.Tours;
using VirtueCore.Shared;

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
    private string targetVersion = "3.2.3";
    // 3.2.0 tour files remain compatible -- see EventLoader/ComponentMaker's
    // compatibleVersions for why.
    private List<string> compatibleVersions = new List<string> { "3.2.0", "3.2.1", "3.2.2" };
    private ComponentMaker componentMaker;
    private EventLoader eventLoader;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private PlayerController playerController;

    [Header("Scene Transitions")]
    // Every scene change takes at least minMoveSeconds; longer moves (and
    // bigger turns) take longer, levelling off toward maxMoveSeconds so
    // even a move across a whole accelerator never drags on.
    [SerializeField] private float minMoveSeconds = 1f;
    [SerializeField] private float maxMoveSeconds = 5f;
    // Metres of travel at which a move uses ~63% of the extra time between
    // min and max; smaller values reach the maximum sooner.
    [SerializeField] private float moveDistanceScale = 30f;

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

        // Tours in Tours and its subfolders; subfolder tours are labelled
        // with their folder ("LHC/ATLAS_Tour").
        List<string> files = FileListing.ListFiles(path,
            file => string.Equals(Path.GetExtension(file), ".json", System.StringComparison.OrdinalIgnoreCase));

        tourDropdown.ClearOptions();
        tourFiles.Clear();

        List<string> displayNames = new List<string>();
        int defaultIndex = -1;

        foreach (string relative in files)
        {
            string name = FileListing.DisplayName(relative);

            tourFiles.Add(FileListing.FullPath(path, relative));
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

    // Re-reads the tour list from disk (PauseMenu calls this whenever the
    // menu opens), so tours added or removed while the app is running show
    // up. Keeps the currently selected tour selected if it still exists.
    public void RefreshFileList()
    {
        int current = tourDropdown.value;
        string selected = (current >= 0 && current < tourFiles.Count) ? tourFiles[current] : null;

        LoadTourFilesIntoDropdown();

        if (selected != null)
        {
            int index = tourFiles.FindIndex(f => string.Equals(f, selected, System.StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                tourDropdown.SetValueWithoutNotify(index);
                tourDropdown.RefreshShownValue();
            }
        }
    }

    // Whether the most recent StartTour() actually started a tour. The tour
    // start button also calls PauseMenu.StartTour() (after this), which
    // checks this so a tour that failed to open doesn't switch the pause
    // menu into tour mode.
    public bool TourActive { get; private set; }

    public void StartTour()
    {
        int index = tourDropdown.value;
        if (index < 0 || index >= tourFiles.Count)
        {
            TourActive = false;
            return;
        }

        // The tour file is read and checked here, before any menu is hidden.
        // This used to happen after the normal menu was already hidden, so a
        // missing (deleted since the list was built), unreadable or
        // wrong-version tour file left the user with no menu at all.
        TourFile tour = ReadTourFile(index);
        if (tour == null)
        {
            TourActive = false;
            FindAnyObjectByType<PauseMenu>()?.CancelTour();
            return;
        }

        // Remember what the user had, so ExitTour can put it back. Skipped
        // if a tour is somehow already running, so its saved state (from
        // before that tour) isn't overwritten with tour state.
        if (!TourActive)
        {
            componentMaker.CaptureTourReturnState();
            eventLoader.CaptureTourReturnState();
            playerController?.CaptureTourReturnState();
            cameraController?.CaptureTourReturnState();
        }

        // An event file still loading is dropped now rather than waited for
        // (the return state above already recorded it, so ExitTour reloads it).
        eventLoader.CancelLoadForTour();

        TourActive = true;
        currentTour = tour;

        if (activeTourLoadCoroutine != null)
        {
            StopCoroutine(activeTourLoadCoroutine);
            activeTourLoadCoroutine = null;
        }

        tourLoadToken++;
        activeTourLoadCoroutine = StartCoroutine(LoadTourCoroutine(tourLoadToken));
    }

    // Leaves the tour and puts the user back where they were before it
    // started -- same model and event file, camera position and view mode,
    // menus -- without reloading the scene, so everything the tour didn't
    // touch (volume, HUD, sliders, typed fields...) is simply left as is.
    public void ExitTour()
    {
        if (!TourActive)
            return;

        // Stop the tour's own loading and any scene transition in progress
        // before restoring, so nothing from the tour lands afterwards.
        if (activeTourLoadCoroutine != null)
        {
            StopCoroutine(activeTourLoadCoroutine);
            activeTourLoadCoroutine = null;
        }
        tourLoadToken++;
        if (sceneTransitionCoroutine != null)
        {
            StopCoroutine(sceneTransitionCoroutine);
            sceneTransitionCoroutine = null;
        }

        TourActive = false;
        currentTour = null;
        currentSceneIndex = 0;

        tourMenu.SetActive(false);
        tourText.SetActive(false);
        titleText.text = "";
        bodyText.text = "";
        normalMenu.SetActive(true);

        playerController?.RestoreTourReturnState();
        cameraController?.RestoreTourReturnState();
        componentMaker.RestoreTourReturnState();
        eventLoader.RestoreTourReturnState();
    }

    // Reads and validates the tour at tourFiles[index]. Shows an error and
    // returns null if it can't be used. A file that no longer exists is also
    // removed from the tour list.
    private TourFile ReadTourFile(int index)
    {
        string path = tourFiles[index];

        if (!File.Exists(path))
        {
            ShowError("Tour file \"" + Path.GetFileNameWithoutExtension(path) + "\" no longer exists.");
            RemoveTourOption(index);
            return null;
        }

        TourFile tour;
        try
        {
            tour = JsonUtility.FromJson<TourFile>(File.ReadAllText(path));
        }
        catch (System.Exception e)
        {
            ShowError("Error reading tour file: " + e.Message);
            return null;
        }

        if (tour == null || tour.header == null || tour.scenes == null)
        {
            ShowError("Error reading tour file: not a valid tour.");
            return null;
        }

        if (!VersionCheck.IsCompatible(tour.header.version, targetVersion, compatibleVersions))
        {
            ShowError("Tour JSON file is not version " + targetVersion + ".");
            Debug.LogError("Tour JSON file is not version " + targetVersion + ".");
            return null;
        }

        return tour;
    }

    private void RemoveTourOption(int index)
    {
        tourFiles.RemoveAt(index);
        tourDropdown.options.RemoveAt(index);
        tourDropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, Mathf.Max(0, tourFiles.Count - 1)));
        tourDropdown.RefreshShownValue();
    }

    private void ShowError(string message)
    {
        if (errorText != null) errorText.text = message;
    }

    private IEnumerator LoadTourCoroutine(int token)
    {
        normalMenu.SetActive(false);

        // Load model
        if (!string.IsNullOrEmpty(currentTour.header.model_file))
        {
            // Resolved by ComponentMaker like any other model reference, so
            // it can be a plain name or include a subfolder.
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
            string eventsFilePath = currentTour.header.events_file;
            yield return new WaitUntil(() => eventLoader.activeCoroutines == 0);
            yield return new WaitUntil(() => eventLoader.loadingEvent == false);
            eventLoader.LoadTourFile(eventsFilePath);
            yield return new WaitUntil(() => eventLoader.loadingTour == false);

        }

        if (token != tourLoadToken)
            yield break;

        tourMenu.SetActive(true);
        tourText.SetActive(true);
        currentSceneIndex = 0;

        activeTourLoadCoroutine = null;

        // The first scene appears in place -- there's nothing meaningful to
        // fly from yet.
        ShowScene(currentSceneIndex, teleport: true);
    }

    // The scene change in progress (camera moving, event waiting for it).
    // A new Next/Previous/Restart/Replay replaces it, redirecting the camera
    // from wherever it has got to.
    private Coroutine sceneTransitionCoroutine;

    private void ShowScene(int sceneIndex, bool teleport = false)
    {
        if (currentTour == null || sceneIndex < 0 || sceneIndex >= currentTour.scenes.Count)
            return;

        TourScene scene = currentTour.scenes[sceneIndex];

        // Model fade and text change straight away.
        componentMaker?.ActivateComponents(scene.model_settings);
        titleText.text = scene.text.title;
        bodyText.text = scene.text.body;

        if (sceneTransitionCoroutine != null)
            StopCoroutine(sceneTransitionCoroutine);
        sceneTransitionCoroutine = StartCoroutine(SceneTransitionCoroutine(scene, teleport));
    }

    // Moves (or teleports) the camera to the scene, then starts its event:
    // the previous scene's event is cleared as the camera leaves, and the new
    // one only plays once the camera is in position.
    private IEnumerator SceneTransitionCoroutine(TourScene scene, bool teleport)
    {
        // Camera position and focus
        Vector3 playerPos = new Vector3(
            scene.camera_settings.position[0],
            scene.camera_settings.position[1],
            scene.camera_settings.position[2]
        );
        Vector3 focusPos = new Vector3(
            scene.camera_settings.focus[0],
            scene.camera_settings.focus[1],
            scene.camera_settings.focus[2]
        );

        eventLoader?.ClearTourEvent();

        if (teleport)
        {
            playerController?.TeleportPlayerTo(playerPos);
            cameraController?.SnapTargetTo(focusPos, playerPos);
        }
        else
        {
            // One duration for both, so the camera arrives and finishes
            // turning at the same moment.
            float duration = TransitionDuration(playerPos, focusPos);
            playerController?.MovePlayerTo(playerPos, duration);
            cameraController?.MoveTargetTo(focusPos, duration, playerPos);

            yield return null;
            yield return new WaitUntil(() =>
                (playerController == null || !playerController.isMoving) &&
                (cameraController == null || !cameraController.isMoving));
        }

        sceneTransitionCoroutine = null;
        eventLoader?.AnimateEvent(scene.event_settings);
    }

    // How long the move to a scene's camera position/focus should take:
    // based on how far the camera and its focus travel and how far the view
    // turns (see TourMotion.Duration).
    private float TransitionDuration(Vector3 endPosition, Vector3 endFocus)
    {
        if (playerController == null || cameraController == null)
            return minMoveSeconds;

        Vector3 startPosition = playerController.player.transform.position;
        float travel = Mathf.Max(
            Vector3.Distance(startPosition, endPosition),
            Vector3.Distance(cameraController.FocusPosition, endFocus));

        Vector3 endDirection = endFocus - endPosition;
        float turn = endDirection.sqrMagnitude > 1e-8f
            ? Vector3.Angle(cameraController.transform.forward, endDirection)
            : 0f;

        return TourMotion.Duration(travel, turn, minMoveSeconds, maxMoveSeconds, moveDistanceScale);
    }

    // These work mid-transition too: each one retargets the scene change in
    // progress, so pressing Next twice skips straight to the scene after
    // next, with the camera turning toward it from wherever it is.
    public void ReplayScene()
    {
        if (currentTour == null) return;
        ShowScene(currentSceneIndex);
    }

    public void RestartTour()
    {
        if (currentTour == null) return;
        currentSceneIndex = 0;
        ShowScene(currentSceneIndex);
    }

    public void NextScene()
    {
        if (currentTour == null) return;
        if (currentSceneIndex >= currentTour.scenes.Count - 1) return;

        currentSceneIndex++;
        ShowScene(currentSceneIndex);
    }

    public void PreviousScene()
    {
        if (currentTour == null) return;
        if (currentSceneIndex <= 0) return;

        currentSceneIndex--;
        ShowScene(currentSceneIndex);
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
