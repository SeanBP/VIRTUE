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
using static EventLoader;



public class EventLoader : MonoBehaviour
{
    [System.Serializable]
    public class Header
    {
        public string version;
        public string experiment = "";
        public List<Particle> particles;
        public TrackerSettings tracker_settings;
        public string energy_unit = "GeV";
        public string color_bar = "";
        public string length_unit = "mm";
        public float scale = 1.0f;
        // Optional: model file (StreamingAssets/Models) to auto-load alongside
        // this event file. Ignored during a tour, which manages its own model
        // via the tour file's own header.model_file.
        public string model_file = "";
        // Optional: same convention as TourMaker's EventSettings (time_before,
        // speed) plus time_after, which the tour format doesn't have. Negative
        // (default) means "not set" -- these populate the UI text fields once
        // when the file loads, but don't touch them afterward, so the user can
        // still type over them. Ignored during a tour, which takes precedence
        // via its own scene event_settings (time_before/speed only).
        public float time_before = -1f;
        public float time_after = -1f;
        public float speed = -1f;
    }

    [System.Serializable]
    public class Particle
    {
        public float[] ip = new float[] { 0f, 0f, 0f };
        public float[] angle_rad;
        public float[] color_rgba = new float[] { 1f, 1f, 1f, 1f };
        public float size = 100f;
    }

    [System.Serializable]
    public class TrackerSettings
    {
        public float B_field_T = 0f;
        public float[] tracker_boundary = new float[] { 100000f, 100000f, 100000f };
        public float segment_ns = 0.05f;
        public float[] B_field_direction = new float[] { 0f, 0f, 1f };
    }

    [System.Serializable]
    public class Event_Data
    {
        public string info_text = "";
        public float[] energy_scale = null;
    }
    [System.Serializable]
    public class Hits
    {
        public float[] position;
        public float time_ns = 0f;
        public float size;
        public float[] color_rgba = new float[] { 1f, 1f, 1f, 1f };
    }

    [System.Serializable]
    public class Clusters
    {
        public float[] position;
        public float granularity;
        public float length;
        public float time_ns = 0f;
        public float[] color_rgba = new float[] { 1f, 1f, 1f, 1f };
    }

    [System.Serializable]
    public class Tracks
    {
        public float qOverP = 0f;
        public float[] angle_rad;
        public float[] vertex = new float[] { 0f, 0f, 0f };
        public float[] duration_ns = new float[] { 0f, 100f };
        public float[] color_rgba = new float[] { 1f, 1f, 1f, 1f };
    }

    [System.Serializable]
    public class Jets
    {
        public float length = 100f;
        public float R_rad;
        public float time_ns = 0f;
        public float[] angle_rad;
        public float[] vertex = new float[] { 0f, 0f, 0f };
        public float[] color_rgba = new float[] { 1f, 1f, 1f, 0.5f };
    }
    [System.Serializable]
    public class Blocks
    {
        public float[] position = new float[] { 0f, 0f, 0f };
        public float[] euler_angles_deg = new float[] { 0f, 0f, 0f };
        public float[] size = new float[] { 1f, 1f, 1f };
        public float time_ns = 0f;
        public float[] color_rgba = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
    }


    [System.Serializable]
    public class EventData
    {
        public Event_Data event_data;
        public List<Hits> hits;
        public List<Clusters> clusters;
        public List<Tracks> tracks;
        public List<Jets> jets;
        public List<Blocks> blocks;
    }

    [System.Serializable]
    public class EventDataWrapper
    {
        public Header header;
        public List<EventData> events;
    }

    public bool autoAnimate = true;

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

    public string filename = "NCDIS_Q2=100_Pythia8.json";
    private string lastFilename = "NCDIS_Q2=100_Pythia8.json";
    private string targetVersion = "3.2.0";
    // Intentionally empty: 3.2.0 is the only accepted event file version, since
    // the toroid format change in ComponentMaker's Components schema means
    // older-format model files are no longer guaranteed compatible, and event
    // files are now held to the same single-version standard for consistency.
    private List<string> compatibleVersions = new List<string>();
    private float trackSegmentLength = 0.05f;
    public float speed = 5f; //speed of light is [speed] m/s
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

    // Tracks the currently-running event-loading coroutine, so a new load
    // request can abort it via CancelAndClearCurrentLoad instead of being
    // silently ignored.
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

        activeLoadCoroutine = StartCoroutine(LoadJSONFile());

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
            
            AnimateHits();
        }
        else
        {
   
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
        loadingTour = true;
        inTour = true;
        yield return new WaitUntil(() => activeCoroutines == 0); 
        yield return new WaitUntil(() => loadingEvent == false); 
        autoAnimate = false; 
        lastFilename = filename; 
        filename = newFilename;
        
        LoadFile();
        SetHUD();
        yield return new WaitUntil(() => loadingEvent == false); 
        loadingTour = false; 
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
        }
        else if (color_bar.Contains("lin"))
        {
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(true);
            colorOn = true;
        }
        else
        {
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(false);
            colorOn = false;
        }

        timeText.text = "";
        if (inTour)
        {
            EventInfo.text = "";
            Experiment.text = "";
            MaxText.text = "";
            MinText.text = "";
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(false);
        }
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
        activeCoroutines++;
        DestroyGameObjects(hitObjects);
        DestroyGameObjects(jetObjects);
        DestroyGameObjects(clusterObjects);
        DestroyGameObjects(trackObjects);
        DestroyGameObjects(blockObjects);
        activeCoroutines--;
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

    private void CreateParticle(Particle particleData, float scale)
    {
        // Create a particle GameObject
        GameObject particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        particle.transform.localScale = new Vector3(particleData.size * totScale, particleData.size * totScale, particleData.size * totScale);
        particle.GetComponent<Collider>().enabled = false;
        particle.GetComponent<Renderer>().enabled = false;

        // Set particle material and color
        Material particleMaterial = MakeMaterial(particleData.color_rgba);
        particleMaterial.renderQueue = -1;
        particle.GetComponent<MeshRenderer>().sharedMaterial = particleMaterial;

        // Store the final position and direction. ip.x is negated to match
        // the same right-handed-to-Unity-left-handed display convention
        // used everywhere else (hits/blocks/tracks all negate x); the
        // direction formula's -Cos(b)*Sin(a) term already carries the
        // matching asymmetric sign (parallel to how track momentum's px
        // is reconstructed), so only the position needed this fix.
        Vector3 ip = new Vector3(-particleData.ip[0] * totScale, particleData.ip[1] * totScale, particleData.ip[2] * totScale);
        Vector3 direction = new Vector3(
            -Mathf.Cos(particleData.angle_rad[1]) * Mathf.Sin(particleData.angle_rad[0]),
            Mathf.Sin(particleData.angle_rad[1]),
            Mathf.Cos(particleData.angle_rad[1]) * Mathf.Cos(particleData.angle_rad[0])
        ).normalized;

        particles.Add(particle);
        finalPositions.Add(ip);
        directions.Add(direction);
    }

    public IEnumerator LoadJSONFile(bool chaseCompanion = true)
    {
        loadingEvent = true;
        string path = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Events");
        string filePath = Path.Combine(path, filename);
        EventDataWrapper eventDataWrapper = null;
        errorText.text = "Reading event file";
        yield return null;
        try
        {

            StreamReader source = new StreamReader(filePath);
            fileContents = source.ReadToEnd();
            source.Close();

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

        if (String.Equals(headerVersion, targetVersion) || compatibleVersions.Contains(headerVersion))
        {
            errorText.text = "Loading events: 0% complete";
            ParseHeader(eventDataWrapper, chaseCompanion);

            int numEvents = eventDataWrapper.events.Count;
            InitializeEventArrays(numEvents);

            if (eventDataWrapper.header.particles != null)
            {
                foreach (Particle particleData in eventDataWrapper.header.particles)
                {
                    CreateParticle(particleData, scale);
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
                    (hitObjects[i], hitTime[i]) = CreateHitObjects(eventDataWrapper.events[i].hits);
                }
                catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                yield return null;

                if (eventOk)
                {
                    try { (blockObjects[i], blockTime[i]) = CreateBlockObjects(eventDataWrapper.events[i].blocks); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (clusterObjects[i], clusterTime[i]) = CreateClusterObjects(eventDataWrapper.events[i].clusters); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (jetObjects[i], jetTime[i]) = CreateJetObjects(eventDataWrapper.events[i].jets); }
                    catch (Exception ex) { eventOk = false; ReportEventLoadError(ex, i); }
                }
                yield return null;

                if (eventOk)
                {
                    try { (trackObjects[i], trackTime[i]) = CreateTrackObjects(eventDataWrapper.events[i].tracks); }
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
        SetHUD(inTour);
        if (autoAnimate)
        {
            LoopAnimation();
        }

    }

    private void ReportEventLoadError(Exception ex, int eventIndex)
    {
        errorText.text = $"Error loading event {eventIndex}: " + ex.Message;
        UnityEngine.Debug.LogError($"Error loading event {eventIndex}: " + ex);
    }

    private void ParseHeader(EventDataWrapper eventDataWrapper, bool chaseCompanion = true)
    {
        headerExperiment = eventDataWrapper.header.experiment;
        headerBField = eventDataWrapper.header.tracker_settings.B_field_T;


        energy_unit = eventDataWrapper.header.energy_unit;
        color_bar = eventDataWrapper.header.color_bar.ToLowerInvariant();
        scale = eventDataWrapper.header.scale;
        string unit = eventDataWrapper.header.length_unit.ToLowerInvariant();
        units = unit switch
        {
            "m" => 1.0f,
            "cm" => 0.01f,
            "mm" => 0.001f,
            _ => 1.0f,
        };
        totScale = scale * units;
        trackerGeometry = eventDataWrapper.header.tracker_settings.tracker_boundary;
        trackSegmentLength = eventDataWrapper.header.tracker_settings.segment_ns;
        Vector3 bDir = new Vector3(
            eventDataWrapper.header.tracker_settings.B_field_direction[0],
            eventDataWrapper.header.tracker_settings.B_field_direction[1],
            eventDataWrapper.header.tracker_settings.B_field_direction[2]
        );
        bFieldDirection = bDir.sqrMagnitude > 0f ? bDir.normalized : Vector3.forward;
        eScale = energy_unit.ToLowerInvariant() switch
        {
            "ev" => Math.Pow(10, 0),
            "kev" => Math.Pow(10, 3),
            "mev" => Math.Pow(10, 6),
            "gev" => Math.Pow(10, 9),
            "tev" => Math.Pow(10, 12),
            "pev" => Math.Pow(10, 15),
            "eev" => Math.Pow(10, 18),
            _ => Math.Pow(10, 9),
        };

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

            if (eventDataWrapper.header.time_before >= 0f)
            {
                beforeField.text = eventDataWrapper.header.time_before.ToString();
            }
            else
            {
                // Not specified in this file -- clear the field so Update()'s
                // per-frame reparse falls through to its own default instead
                // of keeping whatever the previously loaded file set.
                beforeField.text = "";
            }
            if (eventDataWrapper.header.time_after >= 0f)
            {
                afterField.text = eventDataWrapper.header.time_after.ToString();
            }
            else
            {
                afterField.text = "";
            }
            if (eventDataWrapper.header.speed >= 0f)
            {
                speedField.text = eventDataWrapper.header.speed.ToString();
            }
            else
            {
                speedField.text = "";
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
        material.SetFloat("_Mode", 3);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        return material;
    }

    private (GameObject[], float[]) CreateHitObjects(List<Hits> hits)
    {
        // JsonUtility leaves this null (not an empty list) when the JSON
        // simply omits the "hits" key for an event, which is a normal way
        // for an event file to say "no hits this event" -- treat it the
        // same as an empty array instead of throwing.
        hits ??= new List<Hits>();

        int hitSize = hits.Count;
        GameObject[] eventHitObjects = new GameObject[hitSize];
        float[] timeData = new float[hitSize];

        for (int j = 0; j < hitSize; j++)
        {
            Hits JsonHitObject = hits[j];
            timeData[j] = JsonHitObject.time_ns;
            GameObject hitObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hitObject.transform.position = new Vector3(
                -1f * JsonHitObject.position[0] * totScale,
                JsonHitObject.position[1] * totScale,
                JsonHitObject.position[2] * totScale
            );
            hitObject.transform.localScale = new Vector3(
                JsonHitObject.size * totScale,
                JsonHitObject.size * totScale,
                JsonHitObject.size * totScale
            );
            hitObject.GetComponent<Collider>().enabled = false;
            hitObject.GetComponent<Renderer>().enabled = false;

            Material material = MakeMaterial(JsonHitObject.color_rgba);

            hitObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            eventHitObjects[j] = hitObject;
        }

        return (eventHitObjects, timeData);
    }

    private (GameObject[], float[]) CreateBlockObjects(List<Blocks> blocks)
    {

        blocks ??= new List<Blocks>();

        int blockSize = blocks.Count;

        GameObject[] eventBlockObjects = new GameObject[blockSize];
        Blocks JsonBlockObject = new Blocks();
        float[] blockTimeData = new float[blockSize];
        for (int j = 0; j < blockSize; j++)
        {
            JsonBlockObject = blocks[j];

            blockTimeData[j] = JsonBlockObject.time_ns;

            eventBlockObjects[j] = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eventBlockObjects[j].transform.position = new Vector3(-1f * JsonBlockObject.position[0] * totScale, JsonBlockObject.position[1] * totScale, JsonBlockObject.position[2] * totScale);
            eventBlockObjects[j].transform.localScale = new Vector3(JsonBlockObject.size[0] * totScale, JsonBlockObject.size[1] * totScale, JsonBlockObject.size[2] * totScale);
            eventBlockObjects[j].transform.eulerAngles = new Vector3(JsonBlockObject.euler_angles_deg[0], -JsonBlockObject.euler_angles_deg[1], JsonBlockObject.euler_angles_deg[2]);
            eventBlockObjects[j].GetComponent<Collider>().enabled = false;
            eventBlockObjects[j].GetComponent<Renderer>().enabled = false;

            Material material = MakeMaterial(JsonBlockObject.color_rgba);

            material.renderQueue = -1;
            eventBlockObjects[j].GetComponent<MeshRenderer>().sharedMaterial = material;

        }
        return (eventBlockObjects, blockTimeData);
    }

    private (GameObject[], float[]) CreateClusterObjects(List<Clusters> clusters)
    {
        clusters ??= new List<Clusters>();
        int clusterSize = clusters.Count;

        GameObject[] eventClusterObjects = new GameObject[clusterSize];
        Clusters JsonClusterObject = new Clusters();
        float[] clusterTimeData = new float[clusterSize];
        for (int j = 0; j < clusterSize; j++)
        {
            JsonClusterObject = clusters[j];

            clusterTimeData[j] = JsonClusterObject.time_ns;

            // Get the cluster's coordinates and size
            float x = -1f * JsonClusterObject.position[0] * totScale;
            float y = JsonClusterObject.position[1] * totScale;
            float z = JsonClusterObject.position[2] * totScale;
            float granularity = JsonClusterObject.granularity * totScale;
            float length = JsonClusterObject.length * totScale;

            eventClusterObjects[j] = GameObject.CreatePrimitive(PrimitiveType.Cube);

            // Determine the direction vector
            Vector3 direction = new Vector3(x, y, z).normalized;


            // Position the bar so one end is at the designated coordinates (x, y, z)

            Vector3 position = new Vector3(x, y, z) + direction * (length / 2f);
            eventClusterObjects[j].transform.position = position;

            // Scale the cube (make sure its length corresponds to the 'length' value, and its width to 'granularity')
            eventClusterObjects[j].transform.localScale = new Vector3(granularity, granularity, length);

            // Rotate the cube to face away from the origin (point along the direction vector)
            eventClusterObjects[j].transform.rotation = Quaternion.LookRotation(direction);


            eventClusterObjects[j].GetComponent<Collider>().enabled = false;
            eventClusterObjects[j].GetComponent<Renderer>().enabled = false;

            Material material = MakeMaterial(JsonClusterObject.color_rgba);

            eventClusterObjects[j].GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        return (eventClusterObjects, clusterTimeData);
    }

    private (GameObject[], float[]) CreateJetObjects(List<Jets> jets)
    {
        jets ??= new List<Jets>();
        int jetSize = jets.Count;

        GameObject[] eventJetObjects = new GameObject[jetSize];
        Jets JsonJetObject = new Jets();
        float[] jetTimeData = new float[jetSize];
        for (int j = 0; j < jetSize; j++)
        {
            JsonJetObject = jets[j];
            jetTimeData[j] = JsonJetObject.time_ns;

            float x = -1 * JsonJetObject.vertex[0] * totScale;
            float y = JsonJetObject.vertex[1] * totScale;
            float z = JsonJetObject.vertex[2] * totScale;
            float length = JsonJetObject.length * totScale;
            float theta = JsonJetObject.angle_rad[0];
            float phi = JsonJetObject.angle_rad[1];
            float radius = length * Mathf.Tan(JsonJetObject.R_rad / 2f);



            Vector3 direction = new Vector3(
            -1 * Mathf.Sin(theta) * Mathf.Cos(phi),
            Mathf.Sin(theta) * Mathf.Sin(phi),
            Mathf.Cos(theta)
            ).normalized;


            eventJetObjects[j] = new GameObject();
            Mesh coneMesh = CreateConeMesh(radius, length);
            MeshFilter meshFilter = eventJetObjects[j].AddComponent<MeshFilter>();
            meshFilter.mesh = coneMesh;

            MeshRenderer renderer = eventJetObjects[j].AddComponent<MeshRenderer>();

            Material material = MakeMaterial(JsonJetObject.color_rgba);

            renderer.material = material;

            eventJetObjects[j].transform.position = new Vector3(x, y, z);
            eventJetObjects[j].transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90, 0, 0);  // Rotate 90 degrees around the X axis


            eventJetObjects[j].GetComponent<Renderer>().enabled = false;
        }

        return (eventJetObjects, jetTimeData);
    }
    private (List<GameObject>, List<float>) CreateTrackObjects(List<Tracks> tracks)
    {
        tracks ??= new List<Tracks>();
        int trackSize = tracks.Count;

        List<GameObject> eventTracks = new List<GameObject>();
        List<float> eventTrackTimes = new List<float>();

        // Tracker boundaries in detector coordinates (no display scaling)
        float trackerR = trackerGeometry[0] * units;
        float trackerZn = trackerGeometry[1] * units;
        float trackerZp = trackerGeometry[2] * units;
        
        for (int j = 0; j < trackSize; j++)
        {
            Tracks JsonTrackObject = tracks[j];

            Color color = new Color(
                JsonTrackObject.color_rgba[0],
                JsonTrackObject.color_rgba[1],
                JsonTrackObject.color_rgba[2],
                JsonTrackObject.color_rgba[3]
            );

            int q = JsonTrackObject.qOverP < 0 ? -1 : (JsonTrackObject.qOverP > 0 ? 1 : 0);

            float p = 1f;
            double cm = 2.998 * Math.Pow(10, 8);
            if (JsonTrackObject.qOverP != 0)
            {
                p = (float)(eScale / (cm * Math.Abs(JsonTrackObject.qOverP)));
            }

            float theta = (float)JsonTrackObject.angle_rad[0];
            float phi = (float)JsonTrackObject.angle_rad[1];

            float px = -p * Mathf.Sin(theta) * Mathf.Cos(phi);
            float py = p * Mathf.Sin(theta) * Mathf.Sin(phi);
            float pz = p * Mathf.Cos(theta);

            // Vertex in detector coordinates
            float xo = -JsonTrackObject.vertex[0] * units;
            float yo = JsonTrackObject.vertex[1] * units;
            float zo = JsonTrackObject.vertex[2] * units;

            float B = headerBField;

            float startTime = JsonTrackObject.duration_ns[0];
            float endTime = JsonTrackObject.duration_ns[1];

            float c = 0.299792f;  // m/ns

            Vector3 momentum = new Vector3(px, py, pz);
            float P = momentum.magnitude;

            if (q == 0 || B == 0)
            {
                Vector3 direction = momentum.normalized;

                float vx = direction.x * c;
                float vy = direction.y * c;
                float vz = direction.z * c;

                for (float t = 0; t <= endTime; t += trackSegmentLength)
                {
                    Vector3 startPosition = new Vector3(
                        vx * t + xo,
                        vy * t + yo,
                        vz * t + zo
                    );

                    Vector3 endPosition = new Vector3(
                        vx * (t + trackSegmentLength) + xo,
                        vy * (t + trackSegmentLength) + yo,
                        vz * (t + trackSegmentLength) + zo
                    );

                    float posR = Mathf.Sqrt(endPosition.x * endPosition.x + endPosition.y * endPosition.y);

                    if (endPosition.z < trackerZn || endPosition.z > trackerZp || posR > trackerR)
                    {
                        break;
                    }

                    eventTrackTimes.Add(t + trackSegmentLength + startTime);

                    GameObject segment = new GameObject();
                    LineRenderer lineRenderer = segment.AddComponent<LineRenderer>();
                    lineRenderer.positionCount = 2;

                    // Apply display scale only at rendering
                    lineRenderer.SetPosition(0, startPosition * scale);
                    lineRenderer.SetPosition(1, endPosition * scale);

                    lineRenderer.startWidth = 0.04f;
                    lineRenderer.endWidth = 0.04f;
                    lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                    lineRenderer.startColor = color;
                    lineRenderer.endColor = color;

                    segment.GetComponent<Renderer>().enabled = false;
                    eventTracks.Add(segment);
                }
            }
            else
            {
                // px is reconstructed above with a sign flip relative to py/pz
                // (px = -p sin(theta) cos(phi)) -- a long-standing convention,
                // shared with vertex positions (xo = -vertex.x), for the
                // right-handed-to-Unity-left-handed display flip. For straight
                // tracks that's a harmless pure reflection, but the Lorentz
                // force dv/dt = (q/m)(v x B) is only symmetric under an
                // X-only reflection of v if B is reflected too -- as a
                // pseudovector, i.e. with its Y and Z components negated
                // instead of X (verified: this is the unique field transform
                // that keeps R_x(v) x B' = R_x(v x B) an identity for all
                // v, B). Without this correction, curvature around any field
                // with a Y or Z component doesn't correctly correspond to the
                // momentum/field the caller specified.
                Vector3 bFieldEffective = new Vector3(bFieldDirection.x, -bFieldDirection.y, -bFieldDirection.z);

                // Build a right-handed orthonormal basis {e1, e2, bFieldEffective}
                // spanning the plane perpendicular to the field (e1 x e2 =
                // bFieldEffective). For the default bFieldDirection = +Z this
                // reduces to e1 = -X, e2 = +Y.
                Vector3 e1 = Vector3.Cross(Vector3.up, bFieldEffective);
                if (e1.sqrMagnitude < 1e-6f)
                {
                    e1 = Vector3.Cross(Vector3.right, bFieldEffective);
                }
                e1 = e1.normalized;
                Vector3 e2 = Vector3.Cross(bFieldEffective, e1);

                float omega = (q * B / P) * c;
                float vPar = c * Vector3.Dot(momentum, bFieldEffective) / P;
                float a0 = c * Vector3.Dot(momentum, e1) / P;
                float b0 = c * Vector3.Dot(momentum, e2) / P;

                Vector3 vertexPos = new Vector3(xo, yo, zo);

                Vector3 HelixPosition(float ht)
                {
                    float e1Coeff = (a0 * Mathf.Sin(omega * ht) + b0 * (1f - Mathf.Cos(omega * ht))) / omega;
                    float e2Coeff = (a0 * (Mathf.Cos(omega * ht) - 1f) + b0 * Mathf.Sin(omega * ht)) / omega;
                    return vertexPos + e1Coeff * e1 + e2Coeff * e2 + vPar * ht * bFieldEffective;
                }

                for (float t = 0; t <= endTime; t += trackSegmentLength)
                {
                    Vector3 startPosition = HelixPosition(t);
                    Vector3 endPosition = HelixPosition(t + trackSegmentLength);

                    float posR = Mathf.Sqrt(endPosition.x * endPosition.x + endPosition.y * endPosition.y);

                    if (endPosition.z < trackerZn || endPosition.z > trackerZp || posR > trackerR)
                    {
                        break;
                    }

                    eventTrackTimes.Add(t + trackSegmentLength + startTime);

                    GameObject segment = new GameObject();
                    LineRenderer lineRenderer = segment.AddComponent<LineRenderer>();
                    lineRenderer.positionCount = 2;
                    
                    lineRenderer.SetPosition(0, startPosition * scale);
                    lineRenderer.SetPosition(1, endPosition * scale);

                    lineRenderer.startWidth = 0.04f;
                    lineRenderer.endWidth = 0.04f;
                    lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                    lineRenderer.startColor = color;
                    lineRenderer.endColor = color;

                    segment.GetComponent<Renderer>().enabled = false;
                    eventTracks.Add(segment);
                }
            }
        }

        return (eventTracks, eventTrackTimes);
    }




    Mesh CreateConeMesh(float radius, float height)
    {
        Mesh mesh = new Mesh();

        int segments = 20; // Number of segments for the base circle
        int verticesCount = segments + 2; // Tip + base vertices + center of the base
        Vector3[] vertices = new Vector3[verticesCount];
        int[] triangles = new int[segments * 3 * 2]; // Two sets of triangles (side + base)

        // Tip of the cone (vertex at the origin)
        vertices[0] = new Vector3(0, 0, 0);

        // Base circle vertices (at height along the positive Y axis)
        for (int i = 0; i < segments; i++)
        {
            float angle = 2 * Mathf.PI * i / segments;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[i + 1] = new Vector3(x, height, z); // Move base vertices up by 'height' on the Y axis
        }

        // Center of the base
        vertices[verticesCount - 1] = new Vector3(0, height, 0);

        // Side triangles (connecting the tip to the base)
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0; // Tip of the cone
            triangles[i * 3 + 1] = (i == segments - 1) ? 1 : i + 2; // Next base vertex (wrap around at the end)
            triangles[i * 3 + 2] = i + 1; // Current base vertex
        }

        // Base triangles (to close the bottom)
        for (int i = 0; i < segments; i++)
        {
            int baseIndex = segments * 3 + i * 3;
            triangles[baseIndex] = verticesCount - 1; // Center of the base
            triangles[baseIndex + 1] = i + 1; // Current base vertex
            triangles[baseIndex + 2] = (i == segments - 1) ? 1 : i + 2; // Next base vertex (wrap around)
        }

        // Assign vertices and triangles to the mesh
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }

    // Update is called once per frame
    void Update()
    {
        if (loadingEvent == false)
        {
            if (!inTour)
            {
                EventInfo.text = infoTexts[iEvt];
            }

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

            try
            {
                if(!inTour)
                    speed = float.Parse(speedField.text);
            }
            catch
            {
                speed = 0.001f * 5f / totScale;

            }
            if (speed < 0 || speed > 100)
            {
                if (!inTour)
                    speed = 0.001f * 5f / totScale;
            }

            try
            {
                if (!inTour)
                    timeBeforeCollision = float.Parse(beforeField.text);
            }
            catch
            {
                timeBeforeCollision = 0.001f * 15f / totScale;

            }
            if (timeBeforeCollision < 0 || timeBeforeCollision > 9999)
            {
                if (!inTour)
                    timeBeforeCollision = 0.001f * 15f / totScale;
            }

            try
            {
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
        if (inTour)
        {
            EventInfo.text = "";
            Experiment.text = "";
            MaxText.text = "";
            MinText.text = "";
            timeText.text = "";
            LogColorbar.SetActive(false);
            LinColorbar.SetActive(false);
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
        if (loadingEvent == false)
        {
            timeText.text = "";
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
 
        string path = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Events");

        if (!Directory.Exists(path))
        {
            errorText.text = "Events folder not found in StreamingAssets.";
            return;
        }

        // Get all .json files 
        string[] files = Directory.GetFiles(path, "*.json");

        // Clear existing options and populate new ones
        fileDropdown.ClearOptions();
        fileNames.Clear();

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file); // Get the file name only
            string displayName = Path.GetFileNameWithoutExtension(file); // Display name without .json
            fileNames.Add(fileName); // Store for selection handling
            displayNames.Add(displayName); // Store display name for dropdown
        }

        // Add file names to the dropdown
        fileDropdown.AddOptions(displayNames);
    }

    public void OnFileSelected(int index)
    {
        if (index < 0 || index >= fileNames.Count)
            return;

        filename = fileNames[index];

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