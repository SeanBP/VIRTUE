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

        // A smooth move from a starting state (value, velocity, acceleration)
        // to a resting end value over a fixed duration (a quintic Hermite
        // curve). It ends with zero velocity and acceleration. Starting from
        // rest it is exactly the Ease curve above. Starting from the state of a move
        // already under way, it continues that motion without any jump in
        // speed or acceleration -- so a move can be redirected mid-flight
        // without a jolt (which matters in VR, where the user is the camera).
        // The four components are free for any values to animate together
        // (e.g. a position plus an angle).
        public struct MotionSegment
        {
            private Vector4 p0, v0, a0, p1;
            private float duration;

            public float Duration { get { return duration; } }

            public MotionSegment(Vector4 start, Vector4 startVelocity, Vector4 startAcceleration,
                                 Vector4 end, float duration)
            {
                this.duration = Mathf.Max(duration, 1e-4f);
                p0 = start;
                p1 = end;
                // Velocity and acceleration in "per unit of the curve
                // parameter" terms.
                v0 = startVelocity * this.duration;
                a0 = startAcceleration * (this.duration * this.duration);
            }

            // State at `time` seconds in; clamped to the end once finished.
            public void Evaluate(float time, out Vector4 value, out Vector4 velocity, out Vector4 acceleration)
            {
                if (time >= duration)
                {
                    value = p1;
                    velocity = Vector4.zero;
                    acceleration = Vector4.zero;
                    return;
                }

                float s = Mathf.Max(0f, time / duration);
                float s2 = s * s, s3 = s2 * s, s4 = s3 * s, s5 = s4 * s;

                // Basis functions (end velocity/acceleration are zero).
                float h0 = 1f - 10f * s3 + 15f * s4 - 6f * s5;
                float h1 = s - 6f * s3 + 8f * s4 - 3f * s5;
                float h2 = 0.5f * s2 - 1.5f * s3 + 1.5f * s4 - 0.5f * s5;
                float h5 = 10f * s3 - 15f * s4 + 6f * s5;

                float d0 = -30f * s2 + 60f * s3 - 30f * s4;
                float d1 = 1f - 18f * s2 + 32f * s3 - 15f * s4;
                float d2 = s - 4.5f * s2 + 6f * s3 - 2.5f * s4;
                float d5 = 30f * s2 - 60f * s3 + 30f * s4;

                float e0 = -60f * s + 180f * s2 - 120f * s3;
                float e1 = -36f * s + 96f * s2 - 60f * s3;
                float e2 = 1f - 9f * s + 18f * s2 - 10f * s3;
                float e5 = 60f * s - 180f * s2 + 120f * s3;

                value = h0 * p0 + h1 * v0 + h2 * a0 + h5 * p1;
                velocity = (d0 * p0 + d1 * v0 + d2 * a0 + d5 * p1) / duration;
                acceleration = (e0 * p0 + e1 * v0 + e2 * a0 + e5 * p1) / (duration * duration);
            }
        }
    }
}
