using System;
using System.Numerics;

namespace Cirrus.Flight
{
    public enum ControlAxis : byte
    {
        None = 0,
        Pitch = 1,  // input +1 = pull / nose up
        Roll = 2,   // input +1 = stick right / roll right
        Yaw = 3,    // input +1 = right pedal / nose right
        Flap = 4,   // input 0..1 = flap extension
    }

    /// <summary>
    /// One blade element / strip of a lifting surface, defined in the body frame
    /// (X forward, Y right, Z up, right-handed; origin at aircraft CG).
    /// Immutable geometry; per-frame state lives on the stack in FlightDynamics.
    /// </summary>
    [Serializable]
    public struct AeroSurfaceDefinition
    {
        public string Name;
        public Vector3 Position;   // aerodynamic centre, m from CG
        public Vector3 Forward;    // chord line, unit vector toward the nose
        public Vector3 Up;         // lift-positive normal, unit vector
        public float Area;         // m^2
        public float Chord;        // m
        public float AspectRatio;  // of the whole lifting surface this strip belongs to
        public float OswaldFactor;
        public AirfoilModel Airfoil;
        public ControlAxis Control;
        public float MaxControlDeflection; // rad
        public float ControlGain;          // signed; mirrored -1 for e.g. the right aileron

        /// <summary>Fraction of the propeller slipstream's axial increment this surface sees (0 = outside the tube).</summary>
        public float SlipstreamAxial;

        /// <summary>
        /// Signed lateral (+Y) air velocity this surface sees per unit of slipstream
        /// increment, from swirl. Positive = air pushed toward the right wing.
        /// </summary>
        public float SlipstreamSwirl;

        /// <summary>
        /// 2D lift slope corrected for finite aspect ratio,
        /// a = a0 / (1 + a0 / (pi * AR * e)) — lifting-line approximation applied
        /// uniformly to every strip of the surface (see docs/flight-model.md).
        /// </summary>
        public float CorrectedLiftSlope(float twoDimensionalSlope)
            => twoDimensionalSlope / (1f + twoDimensionalSlope / (MathF.PI * AspectRatio * OswaldFactor));
    }

    /// <summary>Pilot/autopilot inputs. All axes normalized.</summary>
    public struct ControlInputs
    {
        public float Pitch;    // -1..1, +1 = pull (nose up)
        public float Roll;     // -1..1, +1 = stick right (roll right)
        public float Yaw;      // -1..1, +1 = right pedal (nose right)
        public float Flap;     //  0..1
        public float Throttle; //  0..1

        public float DeflectionFor(in AeroSurfaceDefinition surface)
        {
            float input = surface.Control switch
            {
                ControlAxis.Pitch => Pitch,
                ControlAxis.Roll => Roll,
                ControlAxis.Yaw => Yaw,
                ControlAxis.Flap => Flap,
                _ => 0f,
            };
            return input * surface.ControlGain * surface.MaxControlDeflection;
        }
    }
}
