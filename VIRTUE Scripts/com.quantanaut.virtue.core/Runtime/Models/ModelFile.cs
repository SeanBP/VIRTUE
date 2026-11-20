using System;
using System.Collections.Generic;
using UnityEngine;
using VirtueCore.Shared;

namespace VirtueCore.Models
{
    public static class ModelFile
    {
        // Parses a model JSON file. JsonUtility reads everything it can; the
        // one field it can't (a hexahedron's nested "vertices" array) is then
        // read with MiniJson and copied onto the matching component. The
        // second pass only runs when the file actually mentions "vertices",
        // so files without hexahedra cost nothing extra.
        public static ComponentListWrapper FromJson(string json)
        {
            ComponentListWrapper wrapper = JsonUtility.FromJson<ComponentListWrapper>(json);
            if (wrapper?.components == null || json.IndexOf("\"vertices\"", StringComparison.Ordinal) < 0)
                return wrapper;

            if (!(MiniJson.Parse(json) is Dictionary<string, object> root) ||
                !root.TryGetValue("components", out object componentsValue) ||
                !(componentsValue is List<object> components))
                return wrapper;

            // Both parsers walk the same "components" array in file order,
            // so entry i here is wrapper.components[i].
            int count = Math.Min(components.Count, wrapper.components.Length);
            for (int i = 0; i < count; i++)
            {
                if (components[i] is Dictionary<string, object> component &&
                    component.TryGetValue("vertices", out object verticesValue))
                {
                    wrapper.components[i].vertices = ReadPoints(verticesValue);
                }
            }

            return wrapper;
        }

        // [[x, y, z], ...] -> float[][]; anything that isn't a list of
        // numeric lists becomes null so the geometry code can report it.
        private static float[][] ReadPoints(object value)
        {
            if (!(value is List<object> list)) return null;

            var points = new float[list.Count][];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is List<object> point)) return null;
                points[i] = new float[point.Count];
                for (int j = 0; j < point.Count; j++)
                {
                    if (!(point[j] is double number)) return null;
                    points[i][j] = (float)number;
                }
            }
            return points;
        }
    }
}
