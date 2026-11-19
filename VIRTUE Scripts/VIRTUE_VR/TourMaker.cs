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
    public Text titleText;
    public Text bodyText;
    public Text errorText; // for printing errors

    private List<string> tourFiles = new List<string>();
    private TourFile currentTour;
    private int currentSceneIndex = 0;
    private string targetVersion = "3.2.3";
    // 3.2.0 tour files remain compatible -- see EventLoader/ComponentMaker's
    // compatibleVersions for why.
    private List<string> compatibleVersions = new List<string> { "3.2.0", "3.2.1", "3.2.2" };

    private ComponentMaker componentMaker;
    private EventLoader eventLoader;

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

        foreach (string file in files)
        {
            tourFiles.Add(file);
            displayNames.Add(Path.GetFileNameWithoutExtension(file));
        }

        tourDropdown.AddOptions(displayNames);
    }

    // Re-reads the tour list from disk (PauseMenu calls this whenever a
    // menu opens), so tours added or removed while the app is running show
    // up. Keeps the currently selected tour selected if it still exists.
    public void RefreshFileList()
    {
        int current = tourDropdown.value;
        string selected = (current >= 0 && current < tourFiles.Count) ? tourFiles[current] : null;

        LoadTourFilesIntoDropdown();

        int index = selected == null
            ? -1
            : tourFiles.FindIndex(f => string.Equals(f, selected, System.StringComparison.OrdinalIgnoreCase));
        tourDropdown.SetValueWithoutNotify(index >= 0 ? index : 0);
        tourDropdown.RefreshShownValue();
    }

    public void StartTour()
    {
        int index = tourDropdown.value;
        if (index < 0 || index >= tourFiles.Count)
            return;

        // The tour file is read and checked here, before any menu is hidden.
        // This used to happen after the normal menu was already hidden, so a
        // missing (deleted since the list was built), unreadable or
        // wrong-version tour file left the user with no menu at all.
        TourFile tour = ReadTourFile(index);
        if (tour == null)
            return;

        currentTour = tour;

        if (activeTourLoadCoroutine != null)
        {
            StopCoroutine(activeTourLoadCoroutine);
            activeTourLoadCoroutine = null;
        }

        tourLoadToken++;
        activeTourLoadCoroutine = StartCoroutine(LoadTourCoroutine(tourLoadToken));
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
            string modelFilePath = Path.Combine(Application.streamingAssetsPath, "Models", currentTour.header.model_file);
            componentMaker.LoadTourFile(modelFilePath);
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

        // UI text
        titleText.text = scene.text.title;
        bodyText.text = scene.text.body;
    }

    public void ReplayScene()
    {

        ShowScene(currentSceneIndex);

    }

    public void RestartTour()
    {

        currentSceneIndex = 0;
        ShowScene(currentSceneIndex);

    }

    public void NextScene()
    {

        if (currentTour == null) return;
        currentSceneIndex++;
        if (currentSceneIndex >= currentTour.scenes.Count)
            currentSceneIndex = currentTour.scenes.Count - 1;

        ShowScene(currentSceneIndex);

    }

    public void PreviousScene()
    {

        if (currentTour == null) return;
            currentSceneIndex--;
        if (currentSceneIndex < 0)
            currentSceneIndex = 0;

        ShowScene(currentSceneIndex);

    }

}
