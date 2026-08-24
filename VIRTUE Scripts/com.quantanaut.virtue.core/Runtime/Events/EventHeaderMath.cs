using System;
using UnityEngine;

namespace VirtueCore.Events
{
    public static class EventHeaderMath
    {
        public static float LengthUnitToScale(string unit)
        {
            return unit switch
            {
                "m" => 1.0f,
                "cm" => 0.01f,
                "mm" => 0.001f,
                _ => 1.0f,
            };
        }

        public static double EnergyUnitToScale(string unit)
        {
            return unit switch
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
        }

        public static Vector3 NormalizeBField(float[] direction)
        {
            Vector3 bDir = new Vector3(direction[0], direction[1], direction[2]);
            return bDir.sqrMagnitude > 0f ? bDir.normalized : Vector3.forward;
        }
    }
}
