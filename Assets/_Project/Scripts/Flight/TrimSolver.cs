using System;
using System.Numerics;

namespace Cirrus.Flight
{
    public struct TrimResult
    {
        public bool Converged;
        public float PitchAngle;  // rad, nose-up positive
        public ControlInputs Controls;
        public RigidBodyState State;
    }

    /// <summary>
    /// Quasi-static trim and performance analysis, driven by the full blade-element
    /// model (never by closed-form approximations), so published-performance
    /// validation tests exercise the same code path the game runs.
    /// Newton iteration with numeric Jacobians; robust enough for conventional
    /// aircraft in the normal envelope, which is all it is used for.
    /// </summary>
    public static class TrimSolver
    {
        /// <summary>
        /// Solve straight-and-level trim at the given true airspeed and altitude:
        /// pitch attitude, elevator, and throttle for zero net force and pitch moment,
        /// then aileron/rudder for zero roll/yaw moment (engine torque compensation).
        /// </summary>
        public static TrimResult SolveLevelFlight(AircraftDefinition aircraft, float speed, float altitude, float flap = 0f)
        {
            AirState air = IsaAtmosphere.AtAltitude(altitude);
            var controls = new ControlInputs { Flap = flap, Throttle = 0.5f };
            float theta = EstimateInitialPitch(aircraft, speed, air);

            bool converged = false;
            for (int outer = 0; outer < 3 && !converged; outer++)
            {
                converged = SolveLongitudinal(aircraft, speed, altitude, air, ref theta, ref controls);
                SolveLateral(aircraft, speed, altitude, air, theta, ref controls);
            }

            bool valid = converged
                         && controls.Throttle >= 0f && controls.Throttle <= 1.001f
                         && MathF.Abs(controls.Pitch) <= 1.001f;

            return new TrimResult
            {
                Converged = valid,
                PitchAngle = theta,
                Controls = controls,
                State = RigidBodyState.LevelFlight(speed, altitude, theta),
            };
        }

        /// <summary>
        /// Power-off stall speed: the lowest speed at which moment-trimmed maximum
        /// lift still supports the weight in level flight.
        /// </summary>
        public static float FindStallSpeed(AircraftDefinition aircraft, float altitude, float flap = 0f)
        {
            AirState air = IsaAtmosphere.AtAltitude(altitude);
            float weight = aircraft.Mass * IsaAtmosphere.Gravity;

            float lo = 15f, hi = 60f;
            if (MaxTrimmedLift(aircraft, hi, altitude, air, flap) < weight)
                return float.NaN; // cannot sustain level flight at all — definition bug
            for (int i = 0; i < 40 && hi - lo > 0.01f; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (MaxTrimmedLift(aircraft, mid, altitude, air, flap) >= weight) hi = mid;
                else lo = mid;
            }
            return hi;
        }

        /// <summary>Best steady rate of climb at full throttle, m/s, and the speed it occurs at.</summary>
        public static (float rateOfClimb, float speed) FindBestClimb(AircraftDefinition aircraft, float altitude, float minSpeed, float maxSpeed)
        {
            AirState air = IsaAtmosphere.AtAltitude(altitude);
            float densityRatio = air.Density / IsaAtmosphere.SeaLevelDensity;
            float weight = aircraft.Mass * IsaAtmosphere.Gravity;

            float bestRoc = float.MinValue, bestSpeed = minSpeed;
            for (float v = minSpeed; v <= maxSpeed; v += 0.5f)
            {
                TrimResult trim = SolveLevelFlight(aircraft, v, altitude);
                if (!trim.Converged) continue;

                // Excess-thrust method: thrust required is what level trim consumed;
                // small-angle approximation in gamma (error < ~2% at GA climb angles).
                float powerRequired = aircraft.Engine.ShaftPower(trim.Controls.Throttle, densityRatio);
                float thrustRequired = aircraft.Propeller.Thrust(powerRequired, v, air.Density);
                float powerAvailable = aircraft.Engine.ShaftPower(1f, densityRatio);
                float thrustAvailable = aircraft.Propeller.Thrust(powerAvailable, v, air.Density);
                float roc = (thrustAvailable - thrustRequired) * v / weight;
                if (roc > bestRoc) { bestRoc = roc; bestSpeed = v; }
            }
            return (bestRoc, bestSpeed);
        }

        /// <summary>Level-flight speed at which required power equals the given fraction of rated power.</summary>
        public static float FindCruiseSpeed(AircraftDefinition aircraft, float altitude, float powerFraction)
        {
            float lo = 30f, hi = 90f;
            for (int i = 0; i < 40 && hi - lo > 0.01f; i++)
            {
                float mid = 0.5f * (lo + hi);
                TrimResult trim = SolveLevelFlight(aircraft, mid, altitude);
                float required = trim.Converged ? trim.Controls.Throttle : 2f;
                if (required > powerFraction) hi = mid;
                else lo = mid;
            }
            return lo;
        }

        // ---------------------------------------------------------------- internals

        static float EstimateInitialPitch(AircraftDefinition aircraft, float speed, in AirState air)
        {
            float cl = aircraft.Mass * IsaAtmosphere.Gravity
                       / (0.5f * air.Density * speed * speed * aircraft.ReferenceArea);
            return Math.Clamp(cl / 5f - 2f * MathUtil.DegToRad, -0.1f, 0.25f);
        }

        static Vector3 LongitudinalResidual(AircraftDefinition aircraft, float speed, float altitude, in AirState air, float theta, in ControlInputs controls)
        {
            RigidBodyState state = RigidBodyState.LevelFlight(speed, altitude, theta);
            AircraftForces loads = FlightDynamics.Compute(aircraft, in state, in controls, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);
            Vector3 forceWorld = MathUtil.BodyToWorld(state.Orientation, loads.Force);
            return new Vector3(
                forceWorld.X,
                forceWorld.Z - aircraft.Mass * IsaAtmosphere.Gravity,
                loads.Torque.Y);
        }

        static bool SolveLongitudinal(AircraftDefinition aircraft, float speed, float altitude, in AirState air, ref float theta, ref ControlInputs controls)
        {
            const float tolerance = 0.5f; // N and N m
            for (int iter = 0; iter < 40; iter++)
            {
                Vector3 r = LongitudinalResidual(aircraft, speed, altitude, air, theta, controls);
                if (MathF.Abs(r.X) < tolerance && MathF.Abs(r.Y) < tolerance && MathF.Abs(r.Z) < tolerance)
                    return true;

                // Numeric Jacobian, forward differences: unknowns (theta, pitch input, throttle).
                const float h = 1e-3f;
                ControlInputs c = controls;

                Vector3 rTheta = LongitudinalResidual(aircraft, speed, altitude, air, theta + h, c);
                c = controls; c.Pitch += h;
                Vector3 rPitch = LongitudinalResidual(aircraft, speed, altitude, air, theta, c);
                c = controls; c.Throttle += h;
                Vector3 rThrottle = LongitudinalResidual(aircraft, speed, altitude, air, theta, c);

                Vector3 j0 = (rTheta - r) / h;
                Vector3 j1 = (rPitch - r) / h;
                Vector3 j2 = (rThrottle - r) / h;

                if (!Solve3x3(j0, j1, j2, r, out Vector3 delta))
                    return false;

                theta = Math.Clamp(theta - Math.Clamp(delta.X, -0.1f, 0.1f), -0.35f, 0.45f);
                controls.Pitch = Math.Clamp(controls.Pitch - Math.Clamp(delta.Y, -0.25f, 0.25f), -1.5f, 1.5f);
                controls.Throttle = Math.Clamp(controls.Throttle - Math.Clamp(delta.Z, -0.25f, 0.25f), 0f, 1.2f);
            }
            return false;
        }

        static void SolveLateral(AircraftDefinition aircraft, float speed, float altitude, in AirState air, float theta, ref ControlInputs controls)
        {
            for (int iter = 0; iter < 20; iter++)
            {
                RigidBodyState state = RigidBodyState.LevelFlight(speed, altitude, theta);
                AircraftForces loads = FlightDynamics.Compute(aircraft, in state, in controls, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);
                float rx = loads.Torque.X, rz = loads.Torque.Z;
                if (MathF.Abs(rx) < 0.5f && MathF.Abs(rz) < 0.5f)
                    return;

                const float h = 1e-3f;
                ControlInputs c = controls; c.Roll += h;
                AircraftForces lr = FlightDynamics.Compute(aircraft, in state, in c, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);
                c = controls; c.Yaw += h;
                AircraftForces ly = FlightDynamics.Compute(aircraft, in state, in c, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);

                float a = (lr.Torque.X - rx) / h, b = (ly.Torque.X - rx) / h;
                float d = (lr.Torque.Z - rz) / h, e = (ly.Torque.Z - rz) / h;
                float det = a * e - b * d;
                if (MathF.Abs(det) < 1e-6f) return;

                float dRoll = (rx * e - b * rz) / det;
                float dYaw = (a * rz - rx * d) / det;
                controls.Roll = Math.Clamp(controls.Roll - Math.Clamp(dRoll, -0.2f, 0.2f), -1f, 1f);
                controls.Yaw = Math.Clamp(controls.Yaw - Math.Clamp(dYaw, -0.2f, 0.2f), -1f, 1f);
            }
        }

        /// <summary>Max moment-trimmed steady lift (world Z aero force) achievable at speed, power off.</summary>
        static float MaxTrimmedLift(AircraftDefinition aircraft, float speed, float altitude, in AirState air, float flap)
        {
            float best = float.MinValue;
            var controls = new ControlInputs { Flap = flap, Throttle = 0f };

            for (float theta = -2f * MathUtil.DegToRad; theta < 30f * MathUtil.DegToRad; theta += 0.5f * MathUtil.DegToRad)
            {
                // Inner 1-var Newton: elevator for zero pitch moment at this attitude.
                for (int iter = 0; iter < 15; iter++)
                {
                    RigidBodyState s = RigidBodyState.LevelFlight(speed, altitude, theta);
                    AircraftForces l = FlightDynamics.Compute(aircraft, in s, in controls, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);
                    if (MathF.Abs(l.Torque.Y) < 1f) break;

                    const float h = 1e-3f;
                    ControlInputs c = controls; c.Pitch += h;
                    AircraftForces lp = FlightDynamics.Compute(aircraft, in s, in c, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);
                    float slope = (lp.Torque.Y - l.Torque.Y) / h;
                    if (MathF.Abs(slope) < 1e-4f) break;
                    controls.Pitch = Math.Clamp(controls.Pitch - l.Torque.Y / slope, -1f, 1f);
                }

                RigidBodyState st = RigidBodyState.LevelFlight(speed, altitude, theta);
                AircraftForces loads = FlightDynamics.Compute(aircraft, in st, in controls, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);
                float lift = MathUtil.BodyToWorld(st.Orientation, loads.Force).Z;
                if (lift > best) best = lift;
            }
            return best;
        }

        static bool Solve3x3(in Vector3 c0, in Vector3 c1, in Vector3 c2, in Vector3 rhs, out Vector3 x)
        {
            // Columns c0..c2, solve A x = rhs by Cramer's rule.
            float det = Vector3.Dot(c0, Vector3.Cross(c1, c2));
            if (MathF.Abs(det) < 1e-9f) { x = default; return false; }
            x = new Vector3(
                Vector3.Dot(rhs, Vector3.Cross(c1, c2)) / det,
                Vector3.Dot(c0, Vector3.Cross(rhs, c2)) / det,
                Vector3.Dot(c0, Vector3.Cross(c1, rhs)) / det);
            return true;
        }
    }
}
