using System;
using System.Numerics;

namespace Cirrus.Flight
{
    /// <summary>Per-surface result, exposed for the dev overlay and the flight recorder.</summary>
    public struct SurfaceForceInfo
    {
        public Vector3 Force;          // N, body frame
        public Vector3 Position;       // m, body frame
        public float AngleOfAttack;    // rad
        public float LiftCoefficient;
        public float DynamicPressure;  // Pa
    }

    /// <summary>Total aerodynamic + propulsive loads, body frame, about the CG. Gravity is NOT included.</summary>
    public readonly struct AircraftForces
    {
        public readonly Vector3 Force;   // N
        public readonly Vector3 Torque;  // N m
        public readonly float Thrust;    // N, for instrumentation

        public AircraftForces(Vector3 force, Vector3 torque, float thrust)
        {
            Force = force;
            Torque = torque;
            Thrust = thrust;
        }
    }

    /// <summary>
    /// Blade-element force computation. Pure function of definition + state + inputs;
    /// no allocation, no Unity types, deterministic — this is the layer under unit test.
    ///
    /// Sign conventions (body frame: X nose, Y right wing, Z up, right-handed):
    ///   nose-up pitch torque   = -Y
    ///   right-roll torque      = -X
    ///   nose-right yaw torque  = +Z
    /// </summary>
    public static class FlightDynamics
    {
        /// <param name="wind">Wind velocity, world frame, m/s (air mass motion).</param>
        /// <param name="perSurface">Optional; pass Span.Empty or a span at least Surfaces.Length long.</param>
        public static AircraftForces Compute(
            AircraftDefinition aircraft,
            in RigidBodyState state,
            in ControlInputs controls,
            in Vector3 wind,
            in AirState air,
            Span<SurfaceForceInfo> perSurface)
        {
            bool record = perSurface.Length >= aircraft.Surfaces.Length;

            // Velocity of the airframe relative to the air mass, body frame.
            Vector3 vBody = MathUtil.WorldToBody(state.Orientation, state.Velocity - wind);
            Vector3 omega = state.AngularVelocity;

            Vector3 force = Vector3.Zero;
            Vector3 torque = Vector3.Zero;

            for (int i = 0; i < aircraft.Surfaces.Length; i++)
            {
                ref readonly AeroSurfaceDefinition s = ref aircraft.Surfaces[i];

                // Local velocity of this strip through the air, including rotation.
                Vector3 vLocal = vBody + Vector3.Cross(omega, s.Position);

                // Strip theory: only the flow component in the chord plane (forward/up)
                // generates section lift/drag; spanwise flow is ignored.
                float vf = Vector3.Dot(vLocal, s.Forward);
                float vu = Vector3.Dot(vLocal, s.Up);
                float speedSq = vf * vf + vu * vu;

                if (speedSq < 0.01f)
                {
                    if (record) perSurface[i] = new SurfaceForceInfo { Position = s.Position };
                    continue;
                }

                float speed = MathF.Sqrt(speedSq);
                float alpha = MathF.Atan2(-vu, vf);
                float deflection = controls.DeflectionFor(in s);
                float liftSlope = s.CorrectedLiftSlope(aircraft.SectionLiftSlope);

                s.Airfoil.Evaluate(alpha, deflection, liftSlope, out float cl, out float cd, out float cm);
                cd += cl * cl / (MathF.PI * s.AspectRatio * s.OswaldFactor); // induced drag

                float q = 0.5f * air.Density * speedSq;
                float inv = 1f / speed;
                Vector3 liftDir = (-vu * s.Forward + vf * s.Up) * inv;   // perpendicular to local flow
                Vector3 dragDir = -(vf * s.Forward + vu * s.Up) * inv;   // along local flow

                Vector3 f = q * s.Area * (cl * liftDir + cd * dragDir);
                force += f;
                torque += Vector3.Cross(s.Position, f);
                // Section pitching moment about its own quarter chord; positive Cm = nose up.
                torque += q * s.Area * s.Chord * cm * Vector3.Cross(s.Forward, s.Up);

                if (record)
                {
                    perSurface[i] = new SurfaceForceInfo
                    {
                        Force = f,
                        Position = s.Position,
                        AngleOfAttack = alpha,
                        LiftCoefficient = cl,
                        DynamicPressure = q,
                    };
                }
            }

            // Fuselage and interference drag: equivalent flat plate at the CG.
            float vMag = vBody.Length();
            if (vMag > 0.1f)
                force += -0.5f * air.Density * vMag * aircraft.EquivalentFlatPlateArea * vBody;

            // Propulsion.
            float densityRatio = air.Density / IsaAtmosphere.SeaLevelDensity;
            float power = aircraft.Engine.ShaftPower(controls.Throttle, densityRatio);
            float axialSpeed = MathF.Max(0f, Vector3.Dot(vBody, aircraft.PropellerAxis));
            float thrust = aircraft.Propeller.Thrust(power, axialSpeed, air.Density);

            Vector3 thrustForce = thrust * aircraft.PropellerAxis;
            force += thrustForce;
            torque += Vector3.Cross(aircraft.PropellerPosition, thrustForce);

            // Engine reaction torque. US-convention rotation (clockwise from the cockpit)
            // puts the reaction about +X = left-rolling tendency.
            if (aircraft.Engine.RatedAngularSpeed > 1f)
                torque += (power / aircraft.Engine.RatedAngularSpeed) * aircraft.PropellerAxis;

            return new AircraftForces(force, torque, thrust);
        }
    }
}
