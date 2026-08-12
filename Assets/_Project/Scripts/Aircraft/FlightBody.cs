using System;
using Cirrus.Core;
using Cirrus.Flight;
using UnityEngine;

namespace Cirrus.Aircraft
{
    /// <summary>
    /// MonoBehaviour glue between the pure flight core and the Unity Rigidbody.
    ///
    /// Each FixedUpdate (200 Hz, ProjectSettings/TimeManager.asset): converts the
    /// Rigidbody state into the core frame, evaluates FlightDynamics, and applies
    /// the result as per-surface AddForceAtPosition calls (CLAUDE.md §4 — never one
    /// lumped force), plus thrust at the propeller, fuselage drag at the CG, and the
    /// residual torque (section pitching moments + engine reaction) as AddTorque.
    /// Gravity stays with the Rigidbody. Zero per-frame heap allocation.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FlightBody : MonoBehaviour
    {
        public ControlInputs Controls;

        /// <summary>World-frame wind, core axes. Weather system plugs in here (M3).</summary>
        public System.Numerics.Vector3 WindCore;

        /// <summary>
        /// Terrain elevation (m) under the aircraft, for ground effect. M1 flies over
        /// the flat plane at 0; the M2 terrain streamer replaces this delegate.
        /// </summary>
        public Func<UnityEngine.Vector3, float> GroundElevationProvider = _ => 0f;

        public AircraftDefinition Aircraft { get; private set; } = null!;
        public SurfaceForceInfo[] SurfaceForces { get; private set; } = Array.Empty<SurfaceForceInfo>();
        public AircraftForces LastLoads { get; private set; }
        public float AngleOfAttack { get; private set; } // rad, body-axis alpha
        public float TrueAirspeed { get; private set; }  // m/s

        Rigidbody _body = null!;

        void Awake()
        {
            // Definition comes from code for M1; becomes a ScriptableObject when there
            // is more than one aircraft to tune.
            Aircraft = BeaverDefinition.Create();
            SurfaceForces = new SurfaceForceInfo[Aircraft.Surfaces.Length];

            _body = GetComponent<Rigidbody>();
            _body.mass = Aircraft.Mass;
            _body.useGravity = true;
            _body.linearDamping = 0f;
            _body.angularDamping = 0f; // all damping is aerodynamic, from the strips
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.maxAngularVelocity = 20f;
            _body.automaticCenterOfMass = false;
            _body.centerOfMass = UnityEngine.Vector3.zero; // definition origin IS the CG
            _body.automaticInertiaTensor = false;
            _body.inertiaTensor = CoreFrame.InertiaToUnity(Aircraft.InertiaDiagonal);
            _body.inertiaTensorRotation = UnityEngine.Quaternion.identity;
        }

        void FixedUpdate()
        {
            System.Numerics.Quaternion orientation = CoreFrame.ToCore(transform.rotation);
            var state = new RigidBodyState
            {
                Position = CoreFrame.ToCore(transform.position), // origin rebasing hooks in here (M2)
                Velocity = CoreFrame.ToCore(_body.linearVelocity),
                Orientation = orientation,
                AngularVelocity = CoreFrame.AngularVelocityToCoreBody(_body.angularVelocity, orientation),
            };

            var env = new FlightEnvironment
            {
                Air = IsaAtmosphere.AtAltitude(transform.position.y),
                Wind = WindCore,
                GroundElevation = GroundElevationProvider(transform.position),
            };
            AircraftForces loads = FlightDynamics.Compute(
                Aircraft, in state, in Controls, in env, SurfaceForces);
            LastLoads = loads;

            System.Numerics.Vector3 airRelative =
                MathUtil.WorldToBody(orientation, state.Velocity - WindCore);
            TrueAirspeed = airRelative.Length();
            AngleOfAttack = TrueAirspeed > 1f ? MathF.Atan2(-airRelative.Z, airRelative.X) : 0f;

            // Per-surface forces at their aerodynamic centres.
            System.Numerics.Vector3 appliedForce = default;
            System.Numerics.Vector3 appliedTorque = default;
            for (int i = 0; i < SurfaceForces.Length; i++)
            {
                ref readonly SurfaceForceInfo info = ref SurfaceForces[i];
                UnityEngine.Vector3 worldForce =
                    CoreFrame.ToUnity(MathUtil.BodyToWorld(orientation, info.Force));
                UnityEngine.Vector3 worldPoint = transform.TransformPoint(CoreFrame.ToUnity(info.Position));
                _body.AddForceAtPosition(worldForce, worldPoint, ForceMode.Force);
                appliedForce += info.Force;
                appliedTorque += System.Numerics.Vector3.Cross(info.Position, info.Force);
            }

            // Thrust at the propeller disc.
            System.Numerics.Vector3 thrustBody = loads.Thrust * Aircraft.PropellerAxis;
            _body.AddForceAtPosition(
                CoreFrame.ToUnity(MathUtil.BodyToWorld(orientation, thrustBody)),
                transform.TransformPoint(CoreFrame.ToUnity(Aircraft.PropellerPosition)),
                ForceMode.Force);
            appliedForce += thrustBody;
            appliedTorque += System.Numerics.Vector3.Cross(Aircraft.PropellerPosition, thrustBody);

            // Fuselage drag acts at the CG: whatever force total remains unapplied.
            System.Numerics.Vector3 fuselageForce = loads.Force - appliedForce;
            _body.AddForce(CoreFrame.ToUnity(MathUtil.BodyToWorld(orientation, fuselageForce)), ForceMode.Force);

            // Residual torque = section Cm moments + engine reaction torque.
            System.Numerics.Vector3 residualTorque = loads.Torque - appliedTorque;
            _body.AddTorque(CoreFrame.ToUnity(MathUtil.BodyToWorld(orientation, residualTorque)), ForceMode.Force);
        }
    }
}
