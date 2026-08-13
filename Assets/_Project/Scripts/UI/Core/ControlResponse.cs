using System;

namespace Cirrus.Controls
{
    /// <summary>
    /// Shaping between a raw control displacement and the input the flight model
    /// sees. Two knobs, both of which matter on a touchscreen:
    ///
    ///   dead zone — a thumb resting on glass is never exactly centred, and a
    ///               trimmed aircraft must be able to actually stay trimmed.
    ///   expo      — cubic blending gives fine authority near centre (where you
    ///               make small corrections) without giving up full deflection at
    ///               the edges. Standard RC-transmitter practice.
    /// </summary>
    public static class ControlResponse
    {
        /// <param name="raw">Displacement in [-1, 1].</param>
        /// <param name="deadZone">Fraction of travel ignored around centre, [0, 1).</param>
        /// <param name="expo">0 = linear, 1 = fully cubic.</param>
        public static float Apply(float raw, float deadZone, float expo)
        {
            float clamped = Math.Clamp(raw, -1f, 1f);
            float magnitude = MathF.Abs(clamped);
            if (magnitude <= deadZone)
                return 0f;

            // Rescale so travel just outside the dead zone starts from zero,
            // rather than jumping.
            float scaled = (magnitude - deadZone) / (1f - deadZone);
            float shaped = (1f - expo) * scaled + expo * scaled * scaled * scaled;
            return MathF.Sign(clamped) * shaped;
        }

        /// <summary>Move a value toward a target at a bounded rate (per second).</summary>
        public static float MoveToward(float current, float target, float maxDelta)
        {
            float difference = target - current;
            if (MathF.Abs(difference) <= maxDelta)
                return target;
            return current + MathF.Sign(difference) * maxDelta;
        }
    }
}
