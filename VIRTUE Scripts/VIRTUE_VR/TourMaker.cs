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

        // The first scene appears in place -- there's nothing meaningful to
        // travel from yet.
        ShowScene(currentSceneIndex, teleport: true);
    }

    // The scene change in progress (user moving, event waiting for them).
    // A new Next/Previous/Restart/Replay replaces it, redirecting the move
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

    // Moves (or teleports) the user to the scene's camera position, then
    // starts its event: the previous scene's event is cleared as they leave,
    // and the new one only plays once they've arrived.
    private IEnumerator SceneTransitionCoroutine(TourScene scene, bool teleport)
    {
        Vector3 headTarget = ToVector(scene.camera_settings.position, new Vector3(10f, 0f, 0f));
        Vector3 focus = ToVector(scene.camera_settings.focus, Vector3.zero);

        eventLoader?.ClearTourEvent();

        if (ResolveRig())
        {
            BeginRigMove(headTarget, focus, teleport);
            while (rigMoving)
                yield return null;
        }

        sceneTransitionCoroutine = null;
        eventLoader?.AnimateEvent(scene.event_settings);
    }

    private static Vector3 ToVector(float[] values, Vector3 fallback)
    {
        if (values == null || values.Length < 3)
            return fallback;
        return new Vector3(values[0], values[1], values[2]);
    }

    // These work mid-transition too: each one retargets the scene change in
    // progress, so pressing Next twice heads straight for the scene after
    // next, carrying on smoothly from the current motion.
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

    // ------------------------------------------------------------------
    // Moving the user between tour scenes
    //
    // In VR the user *is* the camera, so the tour's camera position is where
    // their head is put: the rig (XRRig, which carries the menus on the left
    // controller) is moved so the head lands there, and turned -- about the
    // vertical axis only, pivoting on the head -- so they face the scene's
    // focus point. The rig is never pitched or rolled, and nothing is done
    // to cancel the user's own head motion, both of which are major causes
    // of VR discomfort.
    //
    // Smoothness: the motion starts and ends with zero speed *and* zero
    // acceleration, and a redirect mid-move (Next/Previous pressed again)
    // continues from the current speed and acceleration instead of
    // restarting from rest -- so there's never a jolt. The rig is updated
    // once per frame from Time.deltaTime, and the head offset is measured
    // once at the start of each move (re-measuring it every frame would
    // feed tracking noise back into the rig). Locomotion (CharacterController,
    // joystick move/turn) is paused during the move so nothing fights it.

    [Header("VR tour movement")]
    [Tooltip("The XR rig to move. Defaults to this object.")]
    public Transform rig;
    [Tooltip("The headset camera. Defaults to Camera.main.")]
    public Transform head;
    [Tooltip("Shortest time a move between scenes takes (s).")]
    public float minMoveSeconds = 1.5f;
    [Tooltip("Longest time a move between scenes takes (s); very long moves approach this.")]
    public float maxMoveSeconds = 8f;
    [Tooltip("Distance (m) at which a move uses ~63% of the extra time above the minimum.")]
    public float moveDistanceScale = 50f;
    [Tooltip("Turn the user (about the vertical only) to face each scene's focus point.")]
    public bool turnToFocus = true;

    private VerticalMovement locomotion;

    private bool rigMoving = false;
    private TourMotion.MotionSegment rigSegment;
    private float rigSegmentTime;
    // xyz: where the head should be; w: rig yaw in degrees (unwrapped, so a
    // turn always takes the short way round).
    private Vector4 rigState, rigVelocity, rigAcceleration;
    // The head's position relative to the rig (in the rig's yaw frame),
    // measured at the start of the current move.
    private Vector3 headOffset;

    private bool ResolveRig()
    {
        if (rig == null) rig = transform;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (locomotion == null) locomotion = rig.GetComponent<VerticalMovement>();
        if (locomotion == null) locomotion = FindAnyObjectByType<VerticalMovement>();
        return rig != null && head != null;
    }

    private void BeginRigMove(Vector3 headTarget, Vector3 focus, bool teleport)
    {
        float yaw = rigMoving ? rigState.w : rig.eulerAngles.y;
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        headOffset = Quaternion.Inverse(yawRotation) * (head.position - rig.position);

        Vector4 start = new Vector4(head.position.x, head.position.y, head.position.z, yaw);
        Vector4 end = new Vector4(headTarget.x, headTarget.y, headTarget.z,
                                  TargetYaw(headTarget, focus, yawRotation, yaw));

        if (teleport)
        {
            rigMoving = false;
            rigState = end;
            rigVelocity = Vector4.zero;
            rigAcceleration = Vector4.zero;
            // The CharacterController has to be off while the rig is placed,
            // or it snaps the rig back to where it was.
            locomotion?.SetLocomotionEnabled(false);
            ApplyRigState(end);
            ResumeLocomotion();
            return;
        }

        float distance = Vector3.Distance(head.position, headTarget);
        float turn = Mathf.Abs(end.w - yaw);
        float duration = TourMotion.Duration(distance, turn, minMoveSeconds, maxMoveSeconds, moveDistanceScale);

        // A move already under way carries its motion into the new one.
        Vector4 v0 = rigMoving ? rigVelocity : Vector4.zero;
        Vector4 a0 = rigMoving ? rigAcceleration : Vector4.zero;

        rigSegment = new TourMotion.MotionSegment(start, v0, a0, end, duration);
        rigSegmentTime = 0f;
        rigState = start;
        rigVelocity = v0;
        rigAcceleration = a0;
        rigMoving = true;
        locomotion?.SetLocomotionEnabled(false);
    }

    // Rig yaw (degrees, unwrapped to the nearest equivalent of currentYaw)
    // that leaves the user looking -- horizontally -- at the focus from the
    // head target. Unchanged if turning is off or the focus is straight
    // above/below.
    private float TargetYaw(Vector3 headTarget, Vector3 focus, Quaternion yawRotation, float currentYaw)
    {
        Vector3 look = focus - headTarget;
        look.y = 0f;
        if (!turnToFocus || look.sqrMagnitude < 1e-6f)
            return currentYaw;

        // Account for which way the user is facing within the rig, so it's
        // their view (not just the rig) that ends up facing the focus.
        Vector3 headForward = Quaternion.Inverse(yawRotation) * head.forward;
        headForward.y = 0f;
        float headYaw = headForward.sqrMagnitude > 1e-6f
            ? Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg
            : 0f;

        float desired = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg - headYaw;
        return currentYaw + Mathf.DeltaAngle(currentYaw, desired);
    }

    private void ApplyRigState(Vector4 state)
    {
        Quaternion rotation = Quaternion.Euler(0f, state.w, 0f);
        Vector3 headPosition = new Vector3(state.x, state.y, state.z);
        rig.SetPositionAndRotation(headPosition - rotation * headOffset, rotation);
    }

    void Update()
    {
        if (!rigMoving)
            return;

        if (rig == null || head == null)
        {
            rigMoving = false;
            return;
        }

        rigSegmentTime += Time.deltaTime;
        rigSegment.Evaluate(rigSegmentTime, out rigState, out rigVelocity, out rigAcceleration);
        ApplyRigState(rigState);

        if (rigSegmentTime >= rigSegment.Duration)
        {
            rigMoving = false;
            rigVelocity = Vector4.zero;
            rigAcceleration = Vector4.zero;
            ResumeLocomotion();
        }
    }

    // Physics picks up the rig's new pose before the CharacterController is
    // switched back on, so it starts from where the rig now is.
    private void ResumeLocomotion()
    {
        Physics.SyncTransforms();
        locomotion?.SetLocomotionEnabled(true);
    }

    void OnDisable()
    {
        // Never leave the user stuck without locomotion.
        if (rigMoving)
        {
            rigMoving = false;
            ResumeLocomotion();
        }
    }

}
