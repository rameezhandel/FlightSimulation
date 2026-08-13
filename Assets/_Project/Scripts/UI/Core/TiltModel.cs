using System;

namespace Cirrus.Controls
{
    /// <summary>A gravity direction in the device's own frame, as a unit-ish vector.</summary>
    public readonly struct GravityVector
    {
        /// <summary>Screen right.</summary>
        public readonly float X;
        /// <summary>Screen up.</summary>
        public readonly float Y;
        /// <summary>Out of the screen, toward the player.</summary>
        public readonly float Z;

        public GravityVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// Tilt steering: the phone itself becomes the stick.
    ///
    /// Calibration matters more than the math here — nobody holds a phone at a
    /// repeatable angle, so the neutral attitude is whatever the device was doing
    /// when the player last calibrated, and inputs are deviations from it.
    ///
    /// The mapping is a small-angle approximation: rolling the device changes the
    /// screen-right component of gravity, pitching it changes the screen-up
    /// component. Dividing by sin(maxAngle) makes full deflection arrive at the
    /// configured tilt. Good to a few percent out to ~40°, which is well past
    /// anything comfortable to hold.
    /// </summary>
    public struct TiltModel
    {
        public GravityVector Neutral;
        public float MaxTiltDegrees;  // tilt for full deflection
        public float DeadZone;
        public float Expo;
        public bool Calibrated;

        public static TiltModel Default() => new TiltModel
        {
            // A phone held in landscape, screen tipped back toward the face:
            // gravity mostly into the screen. Replaced on first calibration.
            Neutral = new GravityVector(0f, -0.7f, -0.7f),
            MaxTiltDegrees = 30f,
            DeadZone = 0.06f,
            Expo = 0.35f,
            Calibrated = false,
        };

        /// <summary>Adopt the current attitude as neutral — "hold it how you like, then tap".</summary>
        public void Calibrate(in GravityVector gravity)
        {
            Neutral = gravity;
            Calibrated = true;
        }

        /// <summary>Roll and pitch inputs, each in [-1, 1].</summary>
        public (float roll, float pitch) Evaluate(in GravityVector gravity)
        {
            float span = MathF.Sin(MathF.Max(1f, MaxTiltDegrees) * (MathF.PI / 180f));
            float rollRaw = (gravity.X - Neutral.X) / span;
            float pitchRaw = (gravity.Y - Neutral.Y) / span;

            float roll = ControlResponse.Apply(rollRaw, DeadZone, Expo);
            // Tipping the top of the phone away from you lowers screen-up gravity
            // and should push the nose down.
            float pitch = ControlResponse.Apply(pitchRaw, DeadZone, Expo);
            return (roll, pitch);
        }
    }
}
