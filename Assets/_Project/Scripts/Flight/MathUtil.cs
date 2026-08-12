using System;
using System.Numerics;

namespace Cirrus.Flight
{
    /// <summary>
    /// Small math helpers for the flight core. No Unity types anywhere in this layer;
    /// System.Numerics with right-handed cross products is the single convention
    /// (see docs/decisions/0001-pure-csharp-aero-core.md for the frame definition).
    /// </summary>
    public static class MathUtil
    {
        public const float DegToRad = MathF.PI / 180f;
        public const float RadToDeg = 180f / MathF.PI;

        public const float KnotsToMetersPerSecond = 0.514444f;
        public const float MetersPerSecondToKnots = 1f / KnotsToMetersPerSecond;
        public const float FeetPerMinuteToMetersPerSecond = 0.00508f;
        public const float MetersPerSecondToFeetPerMinute = 1f / FeetPerMinuteToMetersPerSecond;

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Hermite smoothstep; edges may be given in descending order to invert the ramp.</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Rotate a vector from body frame to world frame.</summary>
        public static Vector3 BodyToWorld(in Quaternion orientation, in Vector3 v)
            => Vector3.Transform(v, orientation);

        /// <summary>Rotate a vector from world frame to body frame.</summary>
        public static Vector3 WorldToBody(in Quaternion orientation, in Vector3 v)
            => Vector3.Transform(v, Quaternion.Conjugate(orientation));

        /// <summary>
        /// Quaternion time derivative for angular velocity expressed in the BODY frame:
        /// q_dot = 0.5 * q ⊗ (omega, 0). Hamilton product written out explicitly so the
        /// convention does not depend on library operator definitions.
        /// </summary>
        public static Quaternion Derivative(in Quaternion q, in Vector3 omegaBody)
        {
            float ox = omegaBody.X, oy = omegaBody.Y, oz = omegaBody.Z;
            return new Quaternion(
                0.5f * (q.W * ox + q.Y * oz - q.Z * oy),
                0.5f * (q.W * oy + q.Z * ox - q.X * oz),
                0.5f * (q.W * oz + q.X * oy - q.Y * ox),
                0.5f * (-q.X * ox - q.Y * oy - q.Z * oz));
        }
    }
}
