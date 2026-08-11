using System;
using System.Numerics;
using Cirrus.Aircraft;
using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    /// <summary>
    /// Sign-convention and sanity tests on the assembled force model.
    /// Body frame reminder: nose-up = -Y torque, right-roll = -X torque, nose-right = +Z torque.
    /// </summary>
    [TestFixture]
    public class FlightDynamicsTests
    {
        AircraftDefinition _beaver = null!;

        [SetUp]
        public void SetUp() => _beaver = BeaverDefinition.Create();

        static AircraftForces Forces(AircraftDefinition aircraft, in RigidBodyState state, in ControlInputs controls)
        {
            FlightEnvironment env = FlightEnvironment.FreeAirAtAltitude(state.Position.Z);
            return FlightDynamics.Compute(aircraft, in state, in controls, in env, Span<SurfaceForceInfo>.Empty);
        }

        static RigidBodyState Cruise(float pitchDeg = 2f)
            => RigidBodyState.LevelFlight(50f, 100f, pitchDeg * MathUtil.DegToRad);

        [Test]
        public void ZeroAirspeed_ProducesNoAeroForce()
        {
            var state = new RigidBodyState { Orientation = Quaternion.Identity, Position = new Vector3(0, 0, 10f) };
            AircraftForces loads = Forces(_beaver, state, new ControlInputs());
            Assert.Less(loads.Force.Length(), 1f);
            Assert.Less(loads.Torque.Length(), 1f);
        }

        [Test]
        public void LevelFlight_ProducesUpwardLift()
        {
            RigidBodyState state = Cruise(pitchDeg: 4f);
            AircraftForces loads = Forces(_beaver, state, new ControlInputs());
            Vector3 world = MathUtil.BodyToWorld(state.Orientation, loads.Force);
            Assert.Greater(world.Z, 0.5f * _beaver.Mass * IsaAtmosphere.Gravity,
                "at 50 m/s and 4 deg the Beaver should carry most of its weight");
        }

        [Test]
        public void PowerOff_DragOpposesMotion()
        {
            RigidBodyState state = Cruise();
            AircraftForces loads = Forces(_beaver, state, new ControlInputs());
            Vector3 world = MathUtil.BodyToWorld(state.Orientation, loads.Force);
            Assert.Less(world.X, -500f, "net world-X force must oppose forward motion power-off");
        }

        [Test]
        public void PullingBack_PitchesNoseUp()
        {
            RigidBodyState state = Cruise();
            float baseline = Forces(_beaver, state, new ControlInputs()).Torque.Y;
            float pulled = Forces(_beaver, state, new ControlInputs { Pitch = 0.5f }).Torque.Y;
            Assert.Less(pulled, baseline, "pull must add nose-up (-Y) pitch torque");
        }

        [Test]
        public void StickRight_RollsRight()
        {
            RigidBodyState state = Cruise();
            float baseline = Forces(_beaver, state, new ControlInputs()).Torque.X;
            float deflected = Forces(_beaver, state, new ControlInputs { Roll = 0.5f }).Torque.X;
            Assert.Less(deflected, baseline, "stick right must add right-roll (-X) torque");
        }

        [Test]
        public void RightRudder_YawsNoseRight()
        {
            RigidBodyState state = Cruise();
            float baseline = Forces(_beaver, state, new ControlInputs()).Torque.Z;
            float deflected = Forces(_beaver, state, new ControlInputs { Yaw = 0.5f }).Torque.Z;
            Assert.Greater(deflected, baseline, "right rudder must add nose-right (+Z) torque");
        }

        [Test]
        public void SideslipWeathervanesNoseIntoWind()
        {
            // Slipping right (velocity has +Y component, nose straight ahead):
            // the fin must push the nose right, toward the relative wind.
            var state = Cruise();
            state.Velocity = new Vector3(50f, 5f, 0f);
            AircraftForces loads = Forces(_beaver, state, new ControlInputs());
            Assert.Greater(loads.Torque.Z, 100f, "directional stability: yaw into the slip");
        }

        [Test]
        public void RollRateIsDamped()
        {
            var state = Cruise();
            state.AngularVelocity = new Vector3(0.5f, 0f, 0f); // rolling left (+X)
            AircraftForces loads = Forces(_beaver, state, new ControlInputs());
            Assert.Less(loads.Torque.X, -500f, "wing strips must oppose the roll rate");
        }

        [Test]
        public void ThrottleProducesThrustAndLeftRollingTorque()
        {
            RigidBodyState state = Cruise();
            AircraftForces off = Forces(_beaver, state, new ControlInputs());
            AircraftForces on = Forces(_beaver, state, new ControlInputs { Throttle = 1f });
            Assert.Greater(on.Thrust, 2000f, "full throttle at 50 m/s should exceed 2 kN");
            Assert.AreEqual(0f, off.Thrust, 1e-3f);
            Assert.Greater(on.Torque.X - off.Torque.X, 500f,
                "engine reaction torque must roll left (+X) with US-rotation prop");
        }

        [Test]
        public void PerSurfaceBreakdownMatchesTotals()
        {
            RigidBodyState state = Cruise();
            var perSurface = new SurfaceForceInfo[_beaver.Surfaces.Length];
            FlightEnvironment env = FlightEnvironment.FreeAirAtAltitude(state.Position.Z);
            var controls = new ControlInputs();
            AircraftForces loads = FlightDynamics.Compute(_beaver, in state, in controls, in env, perSurface);

            Vector3 sum = Vector3.Zero;
            foreach (SurfaceForceInfo info in perSurface) sum += info.Force;

            // Totals additionally include fuselage flat-plate drag and the idle
            // windmilling-prop drag (no thrust at idle), so the surface sum must
            // match totals up to those known drag terms.
            float vMag = state.Velocity.Length();
            float fuselageDrag = 0.5f * env.Air.Density * vMag * vMag * _beaver.EquivalentFlatPlateArea;
            System.Numerics.Vector3 vBody = MathUtil.WorldToBody(state.Orientation, state.Velocity);
            float axial = MathF.Max(0f, vBody.X);
            float windmillDrag = _beaver.Propeller.WindmillDrag(0f, _beaver.Engine.RatedPower, axial, env.Air.Density);
            Assert.AreEqual(0f, (loads.Force - sum).Length() - fuselageDrag - windmillDrag, 10f);
        }

        [Test]
        public void HeadwindIncreasesLift()
        {
            RigidBodyState state = Cruise();
            FlightEnvironment calmEnv = FlightEnvironment.FreeAirAtAltitude(state.Position.Z);
            FlightEnvironment windyEnv = calmEnv;
            windyEnv.Wind = new Vector3(-10f, 0f, 0f);
            var controls = new ControlInputs();
            AircraftForces calm = FlightDynamics.Compute(_beaver, in state, in controls, in calmEnv, Span<SurfaceForceInfo>.Empty);
            AircraftForces windy = FlightDynamics.Compute(_beaver, in state, in controls, in windyEnv, Span<SurfaceForceInfo>.Empty);
            Assert.Greater(
                MathUtil.BodyToWorld(state.Orientation, windy.Force).Z,
                MathUtil.BodyToWorld(state.Orientation, calm.Force).Z);
        }
    }
}
