using System;

namespace Cirrus.Controls
{
    /// <summary>
    /// Thumb-stick math. The stick is *floating*: it centres wherever the thumb
    /// first lands inside its zone, which is what makes a screen-edge control
    /// usable without looking at it. Displacement from that origin, clamped to a
    /// disc of Radius pixels, becomes roll (x) and pitch (y).
    ///
    /// Pitch sign follows the flight-model convention: pulling the thumb DOWN the
    /// screen is a pull on the stick, which is +pitch (nose up).
    /// </summary>
    public struct VirtualStickModel
    {
        public float Radius;    // px of travel for full deflection
        public float DeadZone;  // fraction of Radius
        public float Expo;

        public static VirtualStickModel Default() => new VirtualStickModel
        {
            Radius = 140f,
            DeadZone = 0.08f,
            Expo = 0.45f,
        };

        /// <summary>Roll and pitch inputs, each in [-1, 1].</summary>
        public (float roll, float pitch) Evaluate(in ScreenPoint origin, in ScreenPoint current)
        {
            if (Radius <= 0f)
                return (0f, 0f);

            ScreenPoint delta = current - origin;
            float x = delta.X / Radius;
            float y = delta.Y / Radius;

            // Clamp to the unit disc so diagonals cannot exceed full deflection.
            float magnitude = MathF.Sqrt(x * x + y * y);
            if (magnitude > 1f)
            {
                x /= magnitude;
                y /= magnitude;
                magnitude = 1f;
            }

            if (magnitude <= DeadZone)
                return (0f, 0f);

            // Shape the magnitude, keep the direction: expo applied per-axis would
            // bend the stick's diagonal response into a square.
            float shaped = ControlResponse.Apply(magnitude, DeadZone, Expo);
            float scale = shaped / magnitude;

            // Screen +y is up; thumb up = push = nose down, so pitch is negated.
            return (x * scale, -y * scale);
        }

        /// <summary>Knob position for drawing: the clamped displacement from the origin.</summary>
        public ScreenPoint KnobOffset(in ScreenPoint origin, in ScreenPoint current)
        {
            ScreenPoint delta = current - origin;
            float length = delta.Length;
            return length <= Radius || length <= 0f ? delta : delta * (Radius / length);
        }
    }
}
