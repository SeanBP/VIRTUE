using System.Collections.Generic;

namespace VirtueCore.Events
{
    [System.Serializable]
    public class Header
    {
        public string version;
        public string title = "";
        // Legacy alias for "title" from event files written before the
        // 3.2.1 rename; still accepted so older files keep working.
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
}
