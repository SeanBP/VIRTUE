using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using System;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics;
using System.Linq;
using System.Collections.Specialized;
using TMPro;
using System.CodeDom;
using VirtueCore.Events;
using VirtueCore.Tours;
using VirtueCore.Shared;



public class EventLoader : MonoBehaviour
{
    public UnityEngine.UI.Text timeText;
    public UnityEngine.UI.Text errorText;
    public UnityEngine.UI.Text EventInfo;
    public UnityEngine.UI.Text Experiment;
    public UnityEngine.UI.Text MaxText;
    public UnityEngine.UI.Text MinText;
    public GameObject LogColorbar;
    public GameObject LinColorbar;
    private StreamReader source;

    private List<GameObject> particles = new List<GameObject>();
    private List<Vector3> finalPositions = new List<Vector3>();
    private List<Vector3> directions = new List<Vector3>();

    private string[] events;
    private float[][] hitTime;
    private float[][] clusterTime;
    private float[][] jetTime;
    private float[][] blockTime;
    private string[] infoTexts;
    private float?[] maxTexts;
    private float?[] minTexts;
    private double eScale = 1;
    private float scale = 1.0f;
    private float units = 1.0f;
    private float totScale = 1.0f;
    private GameObject[][] hitObjects;
    private GameObject[][] clusterObjects;
    private GameObject[][] jetObjects;
    private GameObject[][] blockObjects;
    private List<GameObject>[] trackObjects;
    private List<float>[] trackTime;


    private int iEvt = 0;
    private int clearingiEvt = -1;
    private string fileContents;
    private string energy_unit;
    private string color_bar;
    private string headerVersion;
    private string headerExperiment;
    private float headerBField;
    private Vector3 bFieldDirection = Vector3.forward;
    private float[] trackerGeometry;

    private string filename = "NCDIS_Q2=100_Pythia8";
    private string lastFilename = "NCDIS_Q2=100_Pythia8";
    private string targetVersion = "3.2.1";
    // 3.2.0 event files remain compatible: the only change since then is the
    // header field rename (experiment -> title), which this parser already
    // accepts via the legacy experiment alias above.
    private List<string> compatibleVersions = new List<string> { "3.2.0" };
    private float trackSegmentLength = 0.05f;
    public float speed = 5; //speed of light is [speed] m/s
    public InputField speedField;
    public InputField beforeField;
    public InputField afterField;
    private List<string> fileNames = new List<string>();
    private List<string> displayNames = new List<string>();
    public TMP_Dropdown fileDropdown;

    private bool animating = false;
    private bool looping = false;

    private float start_time = 0f;

    public UnityEngine.UI.Text loopEvents;

    private float timeBeforeCollision = 10f;  // Time to animate before collision in ns
    private float timeAfterCollision = 50f;  // Time to animate after collision in ns

    public int activeCoroutines = 0;

    private bool colorOn = true;

    public bool loadingEvent = false;
    public bool loadingTour = false;
    public bool inTour = false;
    public bool autoAnimate = true;

    // Tracks whichever event-loading coroutine (dropdown/Resources-driven or
    // file-picker-driven) is currently running, so a new load request can
    // abort it via CancelAndClearCurrentLoad instead of being silently
    // ignored or racing it with a second concurrent coroutine.
    private Coroutine activeLoadCoroutine;

    // Found at runtime the same way TourMaker finds both of these, since
    // EventLoader and ComponentMaker are otherwise independent scripts.
    private ComponentMaker componentMaker;

    // Start is called before the first frame update
    void Start()
    {
        componentMaker = FindAnyObjectByType<ComponentMaker>();

        LoadFilesIntoDropdown();
        int initialIndex = fileNames.IndexOf(filename);
        if (initialIndex != -1)
        {
            fileDropdown.value = initialIndex;
            OnFileSelected(initialIndex);
        }
        else
        {
            filename = fileNames[0];
        }

        StartCoroutine(LoadJSONFile());

    }
    public void AnimateEvent(EventSettings settings)
    {
        StartCoroutine(AnimateEventCoroutine(settings));
    }

    private IEnumerator AnimateEventCoroutine(EventSettings settings)
    {
        yield return new WaitUntil(() => activeCoroutines == 0);

        StartClearHits();

        yield return new WaitUntil(() => activeCoroutines == 0);

        // Apply per-scene event settings
        timeBeforeCollision = settings.time_before;
        speed = settings.speed;

        if (settings.index != -1)
        {
            iEvt = settings.index;
            SetHUD();
            AnimateHits();
        }
        else
        {
            SetHUD(true); // no-event scene
            looping = false;
            animating = false;
        }
    }


    public void LoadTourFile(string newFilename)
    {
        StartCoroutine(LoadTourFileCoroutine(newFilename));
    }

    private IEnumerator LoadTourFileCoroutine(string newFilename)
    {
        timeText.text = "";
        EventInfo.text = "";
        Experiment.text = "";
        MaxText.text = "";
        MinText.text = "";
        LogColorbar.SetActive(false);
        LinColorbar.SetActive(false);
        loadingTour = true;
        inTour = true;
        yield return new WaitUntil(() => activeCoroutines == 0);
        yield return new WaitUntil(() => loadingEvent == false);
        autoAnimate = false;
        lastFilename = filename;
        filename = newFilename;
        filename = Path.GetFileNameWithoutExtension(newFilename);
        LoadFile();
        yield return new WaitUntil(() => loadingEvent == false);
        loadingTour = false;
    }

    public void UploadNewEvents()
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
            lastFilename = "";

            CancelAndClearCurrentLoad();

            activeLoadCoroutine = StartCoroutine(LoadJSONFile(jsonFile));
        }
        else
        {
            UnityEngine.Debug.LogError("Error loading file: File not found");
            errorText.text = "Error loading file: File not found " + path;
        }

        // Ensure coroutine exits properly
        yield break;
    }

    void SetHUD(bool forceHide = false)
    {
        if (forceHide)
        {
            // Hide colorbars and clear all text
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(false);
            colorOn = false;

            timeText.text = "";
            EventInfo.text = "";
            Experiment.text = "";
            MaxText.text = "";
            MinText.text = "";
            return;
        }

        // Otherwise, normal HUD update
        Experiment.text = headerExperiment;
        EventInfo.text = infoTexts[iEvt];
        if (color_bar.Contains("log"))
        {
            LogColorbar.SetActive(true);
            LinColorbar.SetActive(false);
            colorOn = true;
            if (maxTexts[iEvt] != null || minTexts[iEvt] != null)
            {
                MaxText.text = maxTexts[iEvt].Value.ToString("0.0E0") + " " + energy_unit;
                MinText.text = minTexts[iEvt].Value.ToString("0.0E0") + " " + energy_unit;
            }
        }
        else if (color_bar.Contains("lin"))
        {
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(true);
            colorOn = true;
            if (maxTexts[iEvt] != null || minTexts[iEvt] != null)
            {
                MaxText.text = maxTexts[iEvt].Value.ToString("0.0E0") + " " + energy_unit;
                MinText.text = minTexts[iEvt].Value.ToString("0.0E0") + " " + energy_unit;
            }
        }
        else
        {
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(false);
            colorOn = false;
        }
        if (inTour)
        {
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(false);
            timeText.text = "";
            EventInfo.text = "";
            Experiment.text = "";
            MaxText.text = "";
            MinText.text = "";
        }

        timeText.text = "";
    }



    // Kept as a genuine zero-parameter method (not a default-value overload)
    // because it's wired directly to UI Button.onClick in the scene, and
    // Unity's persistent-call resolver only binds to an exact-arity match --
    // a default parameter here would silently break that binding.
    public void LoadFile()
    {
        LoadFile(true);
    }

    // Stops whatever event-loading coroutine is currently running (a no-op
    // if none is) and destroys anything it had created so far, so a new
    // load always starts from a clean slate. Safe to call regardless of
    // whether the previous load finished naturally, was still in progress,
    // or never started -- DestroyGameObjects tolerates null arrays, which
    // covers the case where this runs before any load has ever populated
    // them.
    private void CancelAndClearCurrentLoad()
    {
        if (activeLoadCoroutine != null)
        {
            StopCoroutine(activeLoadCoroutine);
            activeLoadCoroutine = null;
        }

        loadingEvent = false;
        animating = false;
        looping = false;

        start_time = 0f;
        iEvt = 0;
        clearingiEvt = -1;

        DestroyGameObjects(hitObjects);
        DestroyGameObjects(jetObjects);
        DestroyGameObjects(clusterObjects);
        DestroyGameObjects(trackObjects);
        DestroyGameObjects(blockObjects);

        foreach (var obj in particles)
        {
            if (obj != null)
                Destroy(obj);
        }
        particles.Clear();
    }

    // chaseCompanion controls whether a successfully-loaded event's own
    // header.model_file gets auto-loaded afterward. Direct/manual loads
    // (via the parameterless LoadFile() above) chase it; companion loads
    // triggered the other way (LoadEventFile, below) don't, so the two
    // headers can never chase each other in a loop and the file the user
    // actually selected always wins.
    //
    // No longer gated on loadingEvent == false: selecting a different file
    // while one is still loading now aborts and cleans up the in-progress
    // load (via CancelAndClearCurrentLoad) instead of the request being
    // silently ignored.
    private void LoadFile(bool chaseCompanion)
    {
        if (!String.Equals(filename, lastFilename))
        {
            lastFilename = filename;

            CancelAndClearCurrentLoad();

            activeLoadCoroutine = StartCoroutine(LoadJSONFile(chaseCompanion));
        }
    }

    // For external callers (e.g. ComponentMaker.cs, when a model file's own
    // header.event_file names an event) that just want a named event file
    // loaded normally. This event's own header.model_file (if any) is not
    // chased, since the model file that led here already takes precedence.
    public void LoadEventFile(string newFilename)
    {
        filename = newFilename;
        LoadFile(chaseCompanion: false);
    }

    private Material MakeMaterial(float[] color_rgba)
    {
        Material material = new Material(Shader.Find("Transparent/Diffuse"))
        {
            color = new Color(
                    color_rgba[0],
                    color_rgba[1],
                    color_rgba[2],
                    color_rgba[3]
                )
        };
        return material;
    }

    public IEnumerator LoadJSONFile(TextAsset jsonFile, bool chaseCompanion = true)
    {

        loadingEvent = true;
        EventDataWrapper eventDataWrapper = null;
        errorText.text = "Reading event file";
        yield return null;
        try
        {
            fileContents = jsonFile.text;

            // Parse JSON file to EventDataWrapper class
            eventDataWrapper = JsonUtility.FromJson<EventDataWrapper>(fileContents);

            // Store header data into public variables

            headerVersion = eventDataWrapper.header.version;
        }
        catch (Exception ex)
        {
            errorText.text = "Error reading event file: " + ex.Message;
            UnityEngine.Debug.LogError("Error reading event file: " + ex.Message);
            yield break;
        }

        if (VersionCheck.IsCompatible(headerVersion, targetVersion, compatibleVersions))
        {
            errorText.text = "Loading events: 0% complete";
            ParseHeader(eventDataWrapper, chaseCompanion);

            int numEvents = eventDataWrapper.events.Count;
            InitializeEventArrays(numEvents);

            if (eventDataWrapper.header.particles != null)
            {
                foreach (Particle particleData in eventDataWrapper.header.particles)
                {
                    var (particle, finalPosition, direction) = EventGeometry.CreateParticle(particleData, totScale, MakeMaterial);
                    particles.Add(particle);
                    finalPositions.Add(finalPosition);
                    directions.Add(direction);
                }
            }

            for (int i = 0; i < numEvents; i++)
            {
                // yield return isn't allowed inside a try/catch block in C#
                // iterators, so each step below is caught individually
                // (rather than wrapping the whole per-event body) -- without
                // this, one malformed event (e.g. a stray null from a JSON
                // field JsonUtility couldn't populate) would throw
                // uncaught, silently killing this coroutine mid-loop:
                // loadingEvent would never reset to false and errorText
                // would stay stuck at whatever percentage it last reached,
                // with no error message and no way to recover short of
                // restarting the app.
                bool eventOk = true;
                try
                {
                    ParseEnergyScale(eventDataWrapper.events[i].event_data, i);
                    SetHUD();
                    (hitObjects[i], hitTime[i]) = EventGeometry.CreateHitObjects(eventDataWrapper.events[i].hits, totScale, MakeMaterial);
                }
                catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                yield return null;

                if (eventOk)
                {
                    try { (blockObjects[i], blockTime[i]) = EventGeometry.CreateBlockObjects(eventDataWrapper.events[i].blocks, totScale, MakeMaterial); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (clusterObjects[i], clusterTime[i]) = EventGeometry.CreateClusterObjects(eventDataWrapper.events[i].clusters, totScale, MakeMaterial); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (jetObjects[i], jetTime[i]) = EventGeometry.CreateJetObjects(eventDataWrapper.events[i].jets, totScale, MakeMaterial); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (trackObjects[i], trackTime[i]) = EventGeometry.CreateTrackObjects(eventDataWrapper.events[i].tracks, units, scale, trackerGeometry, trackSegmentLength, headerBField, bFieldDirection, eScale); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (!eventOk) break;

                int percentage = Mathf.RoundToInt(((float)(i + 1) / numEvents) * 100f);
                errorText.text = $"Loading events: {percentage}% complete";
            }
            if (!String.IsNullOrEmpty(errorText.text) && errorText.text.StartsWith("Loading events"))
                errorText.text = "";
        }
        else
        {
            errorText.text = "Event JSON File not version " + targetVersion;
            UnityEngine.Debug.LogError("Event JSON File not version " + targetVersion);
        }

        start_time = Time.time;
        loadingEvent = false;
        activeLoadCoroutine = null;

        SetHUD();
        LoopAnimation();
    }

    private void ReportEventLoadError(Exception ex, int eventIndex)
    {
        errorText.text = $"Error loading event {eventIndex}: " + ex.Message;
        UnityEngine.Debug.LogError($"Error loading event {eventIndex}: " + ex);
    }

    public IEnumerator LoadJSONFile(bool chaseCompanion = true)
    {
        loadingEvent = true;

        TextAsset[] files = Resources.LoadAll<TextAsset>("Events");
        int fileIndex = 0;
        for (int i = 0; i < files.Length; i++)
        {
            if (String.Equals(files[i].name, filename))
            {
                fileIndex = i;
            }
        }


        EventDataWrapper eventDataWrapper = null;
        errorText.text = "Reading event file";
        yield return null;
        try
        {

            TextAsset jsonFile = files[fileIndex];
            fileContents = jsonFile.text;

            // Parse JSON file to EventDataWrapper class
            eventDataWrapper = JsonUtility.FromJson<EventDataWrapper>(fileContents);

            // Store header data into public variables

            headerVersion = eventDataWrapper.header.version;
        }
        catch (Exception ex)
        {
            errorText.text = "Error reading event file: " + ex.Message;
            UnityEngine.Debug.LogError("Error reading event file: " + ex.Message);
            yield break;
        }

        if (VersionCheck.IsCompatible(headerVersion, targetVersion, compatibleVersions))
        {
            errorText.text = "Loading events: 0% complete";
            ParseHeader(eventDataWrapper, chaseCompanion);

            int numEvents = eventDataWrapper.events.Count;
            InitializeEventArrays(numEvents);

            if (eventDataWrapper.header.particles != null)
            {
                foreach (Particle particleData in eventDataWrapper.header.particles)
                {
                    var (particle, finalPosition, direction) = EventGeometry.CreateParticle(particleData, totScale, MakeMaterial);
                    particles.Add(particle);
                    finalPositions.Add(finalPosition);
                    directions.Add(direction);
                }
            }
            SetHUD();
            for (int i = 0; i < numEvents; i++)
            {
                // See the matching loop in LoadJSONFile(TextAsset, bool) for
                // why each step is caught individually instead of wrapping
                // the whole per-event body: yield return isn't allowed
                // inside a try/catch block in C# iterators.
                bool eventOk = true;
                try
                {
                    ParseEnergyScale(eventDataWrapper.events[i].event_data, i);
                    SetHUD();
                    (hitObjects[i], hitTime[i]) = EventGeometry.CreateHitObjects(eventDataWrapper.events[i].hits, totScale, MakeMaterial);
                }
                catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                yield return null;

                if (eventOk)
                {
                    try { (blockObjects[i], blockTime[i]) = EventGeometry.CreateBlockObjects(eventDataWrapper.events[i].blocks, totScale, MakeMaterial); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (clusterObjects[i], clusterTime[i]) = EventGeometry.CreateClusterObjects(eventDataWrapper.events[i].clusters, totScale, MakeMaterial); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (jetObjects[i], jetTime[i]) = EventGeometry.CreateJetObjects(eventDataWrapper.events[i].jets, totScale, MakeMaterial); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (trackObjects[i], trackTime[i]) = EventGeometry.CreateTrackObjects(eventDataWrapper.events[i].tracks, units, scale, trackerGeometry, trackSegmentLength, headerBField, bFieldDirection, eScale); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (!eventOk) break;

                int percentage = Mathf.RoundToInt(((float)(i + 1) / numEvents) * 100f);
                errorText.text = $"Loading events: {percentage}% complete";
            }
            if (!String.IsNullOrEmpty(errorText.text) && errorText.text.StartsWith("Loading events"))
                errorText.text = "";
        }
        else
        {
            errorText.text = "Event JSON File not version " + targetVersion;
            UnityEngine.Debug.LogError("Event JSON File not version " + targetVersion);
        }

        start_time = Time.time;
        loadingEvent = false;
        activeLoadCoroutine = null;

        SetHUD();
        LoopAnimation();
    }

    private void ParseHeader(EventDataWrapper eventDataWrapper, bool chaseCompanion = true)
    {
        headerExperiment = LegacyField.Resolve(eventDataWrapper.header.title, eventDataWrapper.header.experiment);
        headerBField = eventDataWrapper.header.tracker_settings.B_field_T;


        energy_unit = eventDataWrapper.header.energy_unit;
        color_bar = eventDataWrapper.header.color_bar.ToLowerInvariant();
        scale = eventDataWrapper.header.scale;
        string unit = eventDataWrapper.header.length_unit.ToLowerInvariant();
        units = EventHeaderMath.LengthUnitToScale(unit);
        totScale = scale * units;
        trackerGeometry = eventDataWrapper.header.tracker_settings.tracker_boundary;
        trackSegmentLength = eventDataWrapper.header.tracker_settings.segment_ns;
        bFieldDirection = EventHeaderMath.NormalizeBField(eventDataWrapper.header.tracker_settings.B_field_direction);
        eScale = EventHeaderMath.EnergyUnitToScale(energy_unit.ToLowerInvariant());

        // A tour manages its own model (tour header.model_file) and timing
        // (per-scene event_settings.time_before/speed) -- both take
        // precedence over anything set here, so skip entirely while inTour.
        // chaseCompanion is also false when this event file was itself
        // auto-loaded via a model file's header.event_file -- we don't chase
        // this event's own header.model_file back, so the model the user
        // actually selected always wins and the two headers can't loop.
        if (!inTour && chaseCompanion)
        {
            if (!string.IsNullOrEmpty(eventDataWrapper.header.model_file) && componentMaker != null)
            {
                componentMaker.LoadModelFile(eventDataWrapper.header.model_file);
            }

            // beforeField/afterField/speedField are unassigned in this
            // project's scene -- unlike Lite/Desktop, Mobile has no visible
            // InputField UI for time_before/time_after/speed. When the
            // field exists, keep the original behavior of writing its text
            // (Update() parses that back into timeBeforeCollision/speed each
            // frame, same as Lite/Desktop). When it doesn't, apply the
            // value directly to the runtime state instead, the same way
            // AnimateEventCoroutine already does for tours (timeBeforeCollision
            // = settings.time_before; speed = settings.speed) -- the setting
            // still takes effect, it just has nothing to display it in.
            if (eventDataWrapper.header.time_before >= 0f)
            {
                if (beforeField != null)
                    beforeField.text = eventDataWrapper.header.time_before.ToString();
                else
                    timeBeforeCollision = eventDataWrapper.header.time_before;
            }
            else
            {
                // Not specified in this file -- reset back to the same
                // default Update() falls back to when beforeField exists,
                // instead of leaving whatever the previous file set.
                if (beforeField != null)
                    beforeField.text = "";
                else
                    timeBeforeCollision = 0.001f * 15f / totScale;
            }
            if (eventDataWrapper.header.time_after >= 0f)
            {
                if (afterField != null)
                    afterField.text = eventDataWrapper.header.time_after.ToString();
                else
                    timeAfterCollision = eventDataWrapper.header.time_after;
            }
            else
            {
                if (afterField != null)
                    afterField.text = "";
                else
                    timeAfterCollision = 0.001f * 60f / totScale;
            }
            if (eventDataWrapper.header.speed >= 0f)
            {
                if (speedField != null)
                    speedField.text = eventDataWrapper.header.speed.ToString();
                else
                    speed = eventDataWrapper.header.speed;
            }
            else
            {
                if (speedField != null)
                    speedField.text = "";
                else
                    speed = 0.001f * 5f / totScale;
            }
        }
    }

    private void InitializeEventArrays(int numEvents)
    {
        minTexts = new float?[numEvents];
        maxTexts = new float?[numEvents];
        infoTexts = new string[numEvents];
        hitObjects = new GameObject[numEvents][];
        blockObjects = new GameObject[numEvents][];
        clusterObjects = new GameObject[numEvents][];
        jetObjects = new GameObject[numEvents][];
        trackObjects = new List<GameObject>[numEvents];
        hitTime = new float[numEvents][];
        blockTime = new float[numEvents][];
        clusterTime = new float[numEvents][];
        jetTime = new float[numEvents][];
        trackTime = new List<float>[numEvents];

        // Pre-filled with empty (non-null) placeholders rather than left
        // null, so that if the per-event loading loop bails out early on a
        // malformed event, every later index is still safe for Update()
        // and event navigation to iterate over instead of null-refing.
        for (int i = 0; i < numEvents; i++)
        {
            hitObjects[i] = Array.Empty<GameObject>();
            blockObjects[i] = Array.Empty<GameObject>();
            clusterObjects[i] = Array.Empty<GameObject>();
            jetObjects[i] = Array.Empty<GameObject>();
            trackObjects[i] = new List<GameObject>();
            hitTime[i] = Array.Empty<float>();
            blockTime[i] = Array.Empty<float>();
            clusterTime[i] = Array.Empty<float>();
            jetTime[i] = Array.Empty<float>();
            trackTime[i] = new List<float>();
            infoTexts[i] = "";
        }

        particles = new List<GameObject>();
        finalPositions = new List<Vector3>();
        directions = new List<Vector3>();
    }

    private void ParseEnergyScale(Event_Data eventData, int index)
    {
        // JsonUtility leaves this null (not a default-constructed instance)
        // when an event's JSON simply omits the "event_data" key.
        eventData ??= new Event_Data();

        if (eventData.energy_scale != null && eventData.energy_scale.Length == 2)
        {
            minTexts[index] = eventData.energy_scale[0];
            maxTexts[index] = eventData.energy_scale[1];
        }
        else
        {
            minTexts[index] = null;
            maxTexts[index] = null;
        }

        infoTexts[index] = eventData.info_text;
    }

    // Update is called once per frame
    void Update()
    {
        if (loadingEvent == false)
        {


            if (colorOn)
            {
                if (maxTexts[iEvt] != null || minTexts[iEvt] != null)
                {
                    MaxText.text = maxTexts[iEvt].Value.ToString("0.0E0") + " " + energy_unit;
                    MinText.text = minTexts[iEvt].Value.ToString("0.0E0") + " " + energy_unit;
                }
                else
                {
                    MaxText.text = "";
                    MinText.text = "";
                }
            }
            else
            {
                MaxText.text = "";
                MinText.text = "";
            }

            // speedField/beforeField/afterField are null on Mobile (no UI
            // for them) -- skip re-reading from the field entirely rather
            // than letting float.Parse throw on a null .text access every
            // frame, which used to silently reset speed/timeBeforeCollision/
            // timeAfterCollision back to a hardcoded default every frame,
            // permanently overwriting whatever ParseHeader had just applied
            // directly from the event file's header.
            try
            {
                if (!inTour && speedField != null)
                    speed = float.Parse(speedField.text);
            }
            catch
            {
                speed = 0.001f * 5f / totScale;

            }
            if (speed < 0 || speed > 100)
            {
                if (!inTour && speedField != null)
                    speed = 0.001f * 5f / totScale;
            }

            try
            {
                if (!inTour && beforeField != null)
                    timeBeforeCollision = float.Parse(beforeField.text);
            }
            catch
            {
                timeBeforeCollision = 0.001f * 15f / totScale;

            }
            if (timeBeforeCollision < 0 || timeBeforeCollision > 9999)
            {
                if (!inTour && beforeField != null)
                    timeBeforeCollision = 0.001f * 15f / totScale;
            }

            try
            {
                if (afterField != null)
                    timeAfterCollision = float.Parse(afterField.text);
            }
            catch
            {
                timeAfterCollision = 0.001f * 60f / totScale;

            }
            if (timeAfterCollision < 5 || timeAfterCollision > 9999)
            {
                timeAfterCollision = 0.001f * 5f / totScale;
            }

            if (looping)
            {
                loopEvents.text = "Stop Event Loop";
            }
            else
            {
                loopEvents.text = "Start Event Loop";
            }

            float c = 0.299792f;  // Speed of light in meters per nanosecond

            if (activeCoroutines == 0)
            {
                if (animating)
                {
                    float elapsedTime = (Time.time - start_time) * speed / c;

                    timeText.text = string.Format("{0:f0}", Math.Round(elapsedTime - timeBeforeCollision)) + " ns";

                    if (looping)
                    {

                        if ((activeCoroutines == 0) && (elapsedTime > timeBeforeCollision + timeAfterCollision))
                        {
                            StartCoroutine(ClearHitsCoroutine(iEvt));
                            start_time = Time.time;
                            elapsedTime = (Time.time - start_time) * speed / c;
                            iEvt++;
                            if (iEvt == hitObjects.Length)
                            {
                                iEvt = 0;
                            }
                        }
                    }

                    if (activeCoroutines == 0)
                    {
                        for (int i = 0; i < hitObjects[iEvt].Length; i++)
                        {
                            if (hitTime[iEvt][i] <= elapsedTime - timeBeforeCollision)
                            {
                                hitObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
                            }
                        }

                        for (int i = 0; i < blockObjects[iEvt].Length; i++)
                        {
                            if (blockTime[iEvt][i] <= elapsedTime - timeBeforeCollision)
                            {
                                blockObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
                            }
                        }

                        for (int i = 0; i < clusterObjects[iEvt].Length; i++)
                        {
                            if (clusterTime[iEvt][i] <= elapsedTime - timeBeforeCollision)
                            {
                                clusterObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
                            }
                        }

                        for (int i = 0; i < jetObjects[iEvt].Length; i++)
                        {
                            if (jetTime[iEvt][i] <= elapsedTime - timeBeforeCollision)
                            {
                                jetObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
                            }
                        }

                        for (int i = 0; i < trackObjects[iEvt].Count; i++)
                        {
                            if (trackTime[iEvt][i] <= elapsedTime - timeBeforeCollision)
                            {
                                trackObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
                            }
                        }


                        for (int i = 0; i < particles.Count; i++)
                        {
                            GameObject particle = particles[i];

                            if (elapsedTime < timeBeforeCollision)
                            {
                                particle.GetComponent<Renderer>().enabled = true;

                                // Calculate the displacement based on the current timeBeforeCollision
                                float displacement = timeBeforeCollision * c;

                                // Calculate the initial position dynamically
                                Vector3 initialPosition = finalPositions[i] - (directions[i] * displacement);

                                // Update the current position
                                float traveledDisplacement = (elapsedTime * c);
                                Vector3 currentPosition = initialPosition + (directions[i] * traveledDisplacement);

                                particle.transform.position = currentPosition * scale;
                            }
                            else
                            {
                                particle.GetComponent<Renderer>().enabled = false;
                            }
                        }
                    }
                }
            }
        }
        if (!inTour && infoTexts != null)
        {
            EventInfo.text = infoTexts[iEvt];
        }
        else if(infoTexts != null)
        {
            timeText.text = "";
            EventInfo.text = "";
            Experiment.text = "";
            MaxText.text = "";
            MinText.text = "";
        }
    }

    public void NextEvent()
    {
        if (activeCoroutines == 0 && loadingEvent == false)
        {
            looping = false;
            StartCoroutine(ClearHitsCoroutine(iEvt));


            iEvt++;
            if (iEvt == hitObjects.Length)
            {
                iEvt = 0;
            }

            if (animating == false)
            {
                LoadHits();
            }
            else
            {
                start_time = Time.time;
            }
        }
    }

    public void PreviousEvent()
    {
        if (activeCoroutines == 0 && loadingEvent == false)
        {
            looping = false;
            StartCoroutine(ClearHitsCoroutine(iEvt));

            iEvt--;
            if (iEvt == -1)
            {
                iEvt = hitObjects.Length - 1;
            }

            if (animating == false)
            {
                LoadHits();
            }
            else
            {
                start_time = Time.time;
            }
        }
    }

    public void LoopAnimation()
    {
        if (activeCoroutines == 0 && loadingEvent == false)
        {
            start_time = Time.time;
            if (looping == true)
            {
                StartCoroutine(ClearHitsCoroutine(iEvt));
                looping = false;
            }
            else
            {
                StartCoroutine(ClearHitsCoroutine(iEvt));
                looping = true;
                animating = true;
            }
        }
    }

    public void AnimateHits()
    {
        if (activeCoroutines == 0 && loadingEvent == false)
        {
            StartCoroutine(ClearHitsCoroutine(iEvt));
            looping = false;
            animating = true;
            start_time = Time.time;
        }
    }

    public void LoadHits()
    {
        timeText.text = "0 ns";
        animating = false;
        looping = false;

        for (int i = 0; i < hitObjects[iEvt].Length; i++)
        {
            hitObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
        }
        for (int i = 0; i < blockObjects[iEvt].Length; i++)
        {
            blockObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
        }
        for (int i = 0; i < clusterObjects[iEvt].Length; i++)
        {
            clusterObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
        }
        for (int i = 0; i < trackObjects[iEvt].Count; i++)
        {
            trackObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
        }
        for (int i = 0; i < jetObjects[iEvt].Length; i++)
        {
            jetObjects[iEvt][i].GetComponent<Renderer>().enabled = true;
        }
        for (int i = 0; i < particles.Count; i++)
        {
            particles[i].GetComponent<Renderer>().enabled = false;
        }

    }

    public void StartClearHits()
    {
        if (activeCoroutines == 0 && loadingEvent == false)
        {
            start_time = Time.time;
            animating = false;
            looping = false;

            StartCoroutine(ClearHitsCoroutine(iEvt));

            for (int i = 0; i < particles.Count; i++)
            {
                particles[i].GetComponent<Renderer>().enabled = false;
            }
            timeText.text = "";
        }
    }

    public void EnterField()
    {
        if (activeCoroutines == 0 && loadingEvent == false)
        {
            start_time = Time.time;

            StartCoroutine(ClearHitsCoroutine(iEvt));

            for (int i = 0; i < particles.Count; i++)
            {
                particles[i].GetComponent<Renderer>().enabled = false;
            }

        }
    }

    void LoadFilesIntoDropdown()
    {
        // Load all JSON files from the Resources/Models folder
        TextAsset[] files = Resources.LoadAll<TextAsset>("Events");

        if (files.Length == 0)
        {
            errorText.text = "No files found in Resources/Models.";
            return;
        }

        fileDropdown.ClearOptions();
        fileNames.Clear();
        displayNames.Clear();

        foreach (TextAsset file in files)
        {
            string fileName = file.name; // Name without extension
            fileNames.Add(fileName);
            displayNames.Add(fileName);
        }

        fileDropdown.AddOptions(displayNames);
    }

    public void OnFileSelected(int index)
    {
        if (index < 0 || index >= fileNames.Count)
            return;

        filename = fileNames[index];

    }

    void DestroyGameObjects(List<GameObject>[] gameObjectsList)
    {
        if (gameObjectsList == null) return;

        foreach (var objects in gameObjectsList)
        {
            foreach (var obj in objects)
            {
                if (obj != null)
                    Destroy(obj);
            }
            objects.Clear();
        }
    }
    void DestroyGameObjects(GameObject[][] objectsArray)
    {
        if (objectsArray == null) return;

        foreach (var objects in objectsArray)
        {
            foreach (var obj in objects)
            {
                if (obj != null)
                    Destroy(obj);
            }
        }
    }

    private IEnumerator ClearHitsCoroutine(int ievt)
    {
        activeCoroutines++;
        bool hitsCleared = false;
        bool clustersCleared = false;
        bool tracksCleared = false;
        bool jetsCleared = false;
        bool blocksCleared = false;

        int localHitIndex = 0;
        int localClusterIndex = 0;
        int localBlockIndex = 0;
        int localTrackIndex = 0;
        int localJetIndex = 0;

        for (int i = 0; i < particles.Count; i++)
        {
            particles[i].GetComponent<Renderer>().enabled = false;
        }

        while (!hitsCleared || !clustersCleared || !tracksCleared || !jetsCleared)
        {
            if (!hitsCleared && localHitIndex >= hitObjects[ievt].Length)
            {
                hitsCleared = true;
            }
            else if (!hitsCleared)
            {
                for (int i = localHitIndex; i < hitObjects[ievt].Length && i < localHitIndex + 1000; i++)
                {
                    hitObjects[ievt][i].GetComponent<Renderer>().enabled = false;
                }
                localHitIndex += 1000;
                yield return null; // Yield after processing each batch
            }

            if (!clustersCleared && localClusterIndex >= clusterObjects[ievt].Length)
            {
                clustersCleared = true;
            }
            else if (!clustersCleared)
            {
                for (int i = localClusterIndex; i < clusterObjects[ievt].Length && i < localClusterIndex + 1000; i++)
                {
                    clusterObjects[ievt][i].GetComponent<Renderer>().enabled = false;
                }
                localClusterIndex += 1000;
                yield return null;
            }

            if (!blocksCleared && localBlockIndex >= blockObjects[ievt].Length)
            {
                blocksCleared = true;
            }
            else if (!blocksCleared)
            {
                for (int i = localBlockIndex; i < blockObjects[ievt].Length && i < localBlockIndex + 1000; i++)
                {
                    blockObjects[ievt][i].GetComponent<Renderer>().enabled = false;
                }
                localBlockIndex += 1000;
                yield return null;
            }

            if (!jetsCleared && localJetIndex >= jetObjects[ievt].Length)
            {
                jetsCleared = true;
            }
            else if (!jetsCleared)
            {
                for (int i = localJetIndex; i < jetObjects[ievt].Length && i < localJetIndex + 1000; i++)
                {
                    jetObjects[ievt][i].GetComponent<Renderer>().enabled = false;
                }
                localJetIndex += 1000;
                yield return null;
            }

            if (!tracksCleared && localTrackIndex >= trackObjects[ievt].Count)
            {
                tracksCleared = true;
            }
            else if (!tracksCleared)
            {
                for (int i = localTrackIndex; i < trackObjects[ievt].Count && i < localTrackIndex + 1000; i++)
                {
                    trackObjects[ievt][i].GetComponent<Renderer>().enabled = false;
                }
                localTrackIndex += 1000;
                yield return null;
            }
        }

        // When done, set clearing to false to allow moving to the next event

        clearingiEvt = ievt;
        activeCoroutines--;
    }
}
