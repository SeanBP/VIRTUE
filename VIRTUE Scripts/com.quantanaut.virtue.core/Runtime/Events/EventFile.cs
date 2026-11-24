using System;
using System.Collections.Generic;
using UnityEngine;
using VirtueCore.Shared;

namespace VirtueCore.Events
{
    public static class EventFile
    {
        // Parses an event JSON file. JsonUtility reads everything it can; the
        // one field it can't (a hexahedron's nested "vertices" array) is then
        // read with MiniJson and copied onto the matching hexahedron. The
        // second pass only runs when the file actually has "hexahedrons", so
        // files without them (including large hit-only files) cost nothing
        // extra.
        public static EventDataWrapper FromJson(string json)
        {
            EventDataWrapper wrapper = JsonUtility.FromJson<EventDataWrapper>(json);
            if (wrapper?.events == null || json.IndexOf("\"hexahedrons\"", StringComparison.Ordinal) < 0)
                return wrapper;

            if (!(MiniJson.Parse(json) is Dictionary<string, object> root) ||
                !root.TryGetValue("events", out object eventsValue) ||
                !(eventsValue is List<object> events))
                return wrapper;

            // Both parsers walk the same arrays in file order, so entry i/j
            // here is wrapper.events[i].hexahedrons[j].
            int eventCount = Math.Min(events.Count, wrapper.events.Count);
            for (int i = 0; i < eventCount; i++)
            {
                List<Hexahedrons> parsed = wrapper.events[i]?.hexahedrons;
                if (parsed == null ||
                    !(events[i] is Dictionary<string, object> evt) ||
                    !evt.TryGetValue("hexahedrons", out object hexValue) ||
                    !(hexValue is List<object> hexahedrons))
                    continue;

                int count = Math.Min(hexahedrons.Count, parsed.Count);
                for (int j = 0; j < count; j++)
                {
                    if (hexahedrons[j] is Dictionary<string, object> hex &&
                        hex.TryGetValue("vertices", out object verticesValue))
                    {
                        parsed[j].vertices = MiniJson.ReadPoints(verticesValue);
                    }
                }
            }

            return wrapper;
        }
    }
}
