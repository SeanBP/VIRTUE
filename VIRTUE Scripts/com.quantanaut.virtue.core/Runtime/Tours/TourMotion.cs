using UnityEngine;

namespace VirtueCore.Tours
{
    // Timing, easing and orientation helpers for tour camera transitions.
    public static class TourMotion
    {
        // How long a transition takes. Grows with how far the camera travels
        // and how far its view has to turn, but levels off: short moves stay
        // close to minSeconds, very long ones approach (never exceed)
        // maxSeconds.
        //
        //   distance      -- metres the camera (or its focus point, whichever
        //                    moves further) travels
        //   turnDegrees   -- angle between the old and new view directions
        //   distanceScale -- metres at which ~63% of the extra time is used;
        //                    smaller values reach the maximum sooner
        //   turnScale     -- degrees of turn worth the same as distanceScale
        public static float Duration(float distance, float turnDegrees,
                                     float minSeconds, float maxSeconds,
                                     float distanceScale, float turnScale = 360f)
        {
            if (maxSeconds < minSeconds) maxSeconds = minSeconds;

            float effort = 0f;
            if (distanceScale > 0f) effort += Mathf.Max(0f, distance) / distanceScale;
            if (turnScale > 0f) effort += Mathf.Max(0f, turnDegrees) / turnScale;

            return minSeconds + (maxSeconds - minSeconds) * (1f - Mathf.Exp(-effort));
        }

        // Ease-in/ease-out ("smootherstep"): starts and ends with zero speed
        // and zero acceleration, so a move neither jolts into motion nor
        // stops abruptly. t is clamped to [0, 1].
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        // Rotation that looks from `from` toward `to`. Unlike
        // Transform.LookAt with a fixed world up, this stays well-defined
        // when looking (nearly) straight up or down -- it then keeps the
        // reference rotation's own up direction instead of flipping.
        // Falls back to `reference` if the two points coincide.
        public static Quaternion LookRotation(Vector3 from, Vector3 to, Quaternion reference)
        {
            Vector3 direction = to - from;
            if (direction.sqrMagnitude < 1e-8f)
                return reference;

            direction.Normalize();
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.995f)
            {
                up = reference * Vector3.up;
                if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.995f)
                    up = reference * Vector3.forward;
            }

            return Quaternion.LookRotation(direction, up);
        }
    }
}
