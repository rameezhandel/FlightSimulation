using System;
using System.Numerics;

namespace Cirrus.Flight
{
    /// <summary>Everything about the world the aircraft flies through at this instant.</summary>
    public struct FlightEnvironment
    {
        public AirState Air;
        public Vector3 Wind;          // m/s, world frame
        public float GroundElevation; // world Z of the terrain directly below; -inf = free air

        public static FlightEnvironment FreeAir(in AirState air)
            => new FlightEnvironment { Air = air, Wind = Vector3.Zero, GroundElevation = float.NegativeInfinity };

        public static FlightEnvironment FreeAirAtAltitude(float altitude)
            => FreeAir(IsaAtmosphere.AtAltitude(altitude));
    }

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
        /// <param name="perSurface">Optional; pass Span.Empty or a span at least Surfaces.Length long.</param>
        public static AircraftForces Compute(
            AircraftDefinition aircraft,
            in RigidBodyState state,
            in ControlInputs controls,
            in FlightEnvironment env,
            Span<SurfaceForceInfo> perSurface)
        {
            bool record = perSurface.Length >= aircraft.Surfaces.Length;

            // Velocity of the airframe relative to the air mass, body frame.
            Vector3 vBody = MathUtil.WorldToBody(state.Orientation, state.Velocity - env.Wind);
            Vector3 omega = state.AngularVelocity;

            // ---- Propulsion first: the slipstream feeds the tail strips below.
            float densityRatio = env.Air.Density / IsaAtmosphere.SeaLevelDensity;
            float power = aircraft.Engine.ShaftPower(controls.Throttle, densityRatio);
            float axialSpeed = MathF.Max(0f, Vector3.Dot(vBody, aircraft.PropellerAxis));
            float thrust = aircraft.Propeller.Thrust(power, axialSpeed, env.Air.Density);
            float slipstream = aircraft.Propeller.SlipstreamIncrement(thrust, axialSpeed, env.Air.Density);

            // Ground effect: induced drag reduction near the surface.
            // McCormick (1979): phi = (16 h/b)^2 / (1 + (16 h/b)^2), h = wing height above ground.
            float inducedDragFactor = 1f;
            if (!float.IsNegativeInfinity(env.GroundElevation) && aircraft.ReferenceSpan > 1f)
            {
                float h = MathF.Max(0.5f, state.Position.Z - env.GroundElevation);
                float r = 16f * h / aircraft.ReferenceSpan;
                float r2 = r * r;
                inducedDragFactor = r2 / (1f + r2);
            }

            Vector3 force = Vector3.Zero;
            Vector3 torque = Vector3.Zero;

            for (int i = 0; i < aircraft.Surfaces.Length; i++)
            {
                ref readonly AeroSurfaceDefinition s = ref aircraft.Surfaces[i];

                // Local velocity of this strip through the air, including rotation and
                // the propeller slipstream (air pushed aft raises the strip's relative
                // axial flow; swirl adds a lateral component at the fin).
                Vector3 vLocal = vBody + Vector3.Cross(omega, s.Position);
                if (slipstream > 0.01f && s.SlipstreamAxial > 0f)
                {
                    vLocal += s.SlipstreamAxial * slipstream * aircraft.PropellerAxis;
                    if (s.SlipstreamSwirl != 0f)
                        vLocal -= s.SlipstreamSwirl * slipstream * Vector3.UnitY;
                }

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
                cd += inducedDragFactor * cl * cl / (MathF.PI * s.AspectRatio * s.OswaldFactor);

                float q = 0.5f * env.Air.Density * speedSq;
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
                force += -0.5f * env.Air.Density * vMag * aircraft.EquivalentFlatPlateArea * vBody;

            // Thrust, with P-factor: at positive alpha the descending (right, for
            // US-rotation engines) blade sees higher AoA, shifting the thrust centroid
            // right and yawing the nose left. Modelled as a lateral offset of the
            // thrust application point proportional to alpha.
            if (thrust > 0.01f)
            {
                float bodyAlpha = vMag > 1f ? MathF.Atan2(-vBody.Z, vBody.X) : 0f;
                Vector3 application = aircraft.PropellerPosition
                                      + aircraft.Propeller.PFactorArmPerRad * bodyAlpha * Vector3.UnitY;
                Vector3 thrustForce = thrust * aircraft.PropellerAxis;
                force += thrustForce;
                torque += Vector3.Cross(application, thrustForce);
            }

            // Windmilling/idle propeller drag disc.
            float windmillDrag = aircraft.Propeller.WindmillDrag(power, aircraft.Engine.RatedPower, axialSpeed, env.Air.Density);
            if (windmillDrag > 0.01f)
            {
                Vector3 dragForce = -windmillDrag * aircraft.PropellerAxis;
                force += dragForce;
                torque += Vector3.Cross(aircraft.PropellerPosition, dragForce);
            }

            // Engine reaction torque. US-convention rotation (clockwise from the cockpit)
            // puts the reaction about +X = left-rolling tendency.
            if (aircraft.Engine.RatedAngularSpeed > 1f)
                torque += (power / aircraft.Engine.RatedAngularSpeed) * aircraft.PropellerAxis;

            return new AircraftForces(force, torque, thrust);
        }
    }
}
