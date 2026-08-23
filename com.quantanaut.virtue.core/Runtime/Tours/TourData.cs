using System.Collections.Generic;

namespace VirtueCore.Tours
{
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
}
