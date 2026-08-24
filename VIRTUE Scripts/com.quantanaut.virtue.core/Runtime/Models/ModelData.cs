namespace VirtueCore.Models
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
        public string title;
        // Legacy alias for "title" from model files written before the
        // 3.2.1 rename; still accepted so older files keep working.
        public string detector;
        public string length_unit = "m";
        public float scale = 1.0f;
        // Optional: event file (StreamingAssets/Events) to auto-load alongside
        // this model file, mirroring EventLoader's header.model_file. Only
        // followed on a direct/manual model load -- if this model was itself
        // auto-loaded via an event file's header.model_file, we don't chase
        // this back, so the event file the user actually selected always
        // takes precedence and the two headers can't loop off each other.
        public string event_file = "";
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
        public float[] length = new float[] { -1f, -1f };
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
}
