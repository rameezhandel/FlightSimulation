using System;
using System.Numerics;

namespace Cirrus.Flight
{
    /// <summary>
    /// Minimal 6-DOF integrator for headless validation, trim verification, and
    /// flight-recorder replay. In the app itself, integration is done by the Unity
    /// Rigidbody (CLAUDE.md §2); this exists so the flight model can be flown in
    /// unit tests without the engine. Semi-implicit Euler at the project-standard
    /// 200 Hz aero timestep.
    /// </summary>
    public static class SixDofSimulator
    {
        /// <summary>The project-wide aerodynamics timestep (CLAUDE.md §4).</summary>
        public const float FixedTimestep = 0.005f;

        public static void Step(
            AircraftDefinition aircraft,
            ref RigidBodyState state,
            in ControlInputs controls,
            in Vector3 wind,
            float dt)
        {
            AirState air = IsaAtmosphere.AtAltitude(state.Position.Z);
            AircraftForces loads = FlightDynamics.Compute(
                aircraft, in state, in controls, in wind, in air, Span<SurfaceForceInfo>.Empty);

            // Linear: aero/thrust force is body frame; gravity added in world frame.
            Vector3 forceWorld = MathUtil.BodyToWorld(state.Orientation, loads.Force)
                                 + new Vector3(0f, 0f, -aircraft.Mass * IsaAtmosphere.Gravity);
            state.Velocity += forceWorld * (dt / aircraft.Mass);
            state.Position += state.Velocity * dt;

            // Angular, body frame: omega_dot = I^-1 (tau - omega x I omega).
            Vector3 inertia = aircraft.InertiaDiagonal;
            Vector3 angularMomentum = inertia * state.AngularVelocity;
            Vector3 tau = loads.Torque - Vector3.Cross(state.AngularVelocity, angularMomentum);
            state.AngularVelocity += new Vector3(tau.X / inertia.X, tau.Y / inertia.Y, tau.Z / inertia.Z) * dt;

            Quaternion qDot = MathUtil.Derivative(state.Orientation, state.AngularVelocity);
            state.Orientation = Quaternion.Normalize(new Quaternion(
                state.Orientation.X + qDot.X * dt,
                state.Orientation.Y + qDot.Y * dt,
                state.Orientation.Z + qDot.Z * dt,
                state.Orientation.W + qDot.W * dt));
        }

        /// <summary>Run whole seconds of simulated time at the fixed aero timestep.</summary>
        public static void Run(
            AircraftDefinition aircraft,
            ref RigidBodyState state,
            in ControlInputs controls,
            in Vector3 wind,
            float seconds)
        {
            int steps = (int)MathF.Round(seconds / FixedTimestep);
            for (int i = 0; i < steps; i++)
                Step(aircraft, ref state, in controls, in wind, FixedTimestep);
        }
    }
}
