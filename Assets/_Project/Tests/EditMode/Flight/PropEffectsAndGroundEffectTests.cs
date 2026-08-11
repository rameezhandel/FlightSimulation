using System;
using System.Numerics;
using Cirrus.Aircraft;
using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    /// <summary>
    /// Slipstream, P-factor, windmilling, and ground effect — the M1 flight-model
    /// completion effects. Sign reminders: nose-up = -Y, right-roll = -X, nose-right = +Z.
    /// </summary>
    [TestFixture]
    public class PropEffectsAndGroundEffectTests
    {
        AircraftDefinition _beaver = null!;

        [SetUp]
        public void SetUp() => _beaver = BeaverDefinition.Create();

        static AircraftForces Forces(AircraftDefinition aircraft, in RigidBodyState state, in ControlInputs controls, float groundElevation = float.NegativeInfinity)
        {
            var env = new FlightEnvironment
            {
                Air = IsaAtmosphere.AtAltitude(state.Position.Z),
                Wind = Vector3.Zero,
                GroundElevation = groundElevation,
            };
            return FlightDynamics.Compute(aircraft, in state, in controls, in env, Span<SurfaceForceInfo>.Empty);
        }

        [Test]
        public void PowerAtClimbSpeed_RequiresRightRudderToTrim()
        {
            // Slipstream swirl + P-factor yaw the nose left under power; the lateral
            // trim must carry right rudder (positive yaw input) — the classic
            // full-power climb behaviour. 40 m/s: slow and powered, but the wing is
            // still cleanly attached (35 m/s sits at the clean-stall edge, where
            // aileron authority realistically collapses).
            TrimResult trim = TrimSolver.SolveLevelFlight(_beaver, 40f, 300f);
            Assert.IsTrue(trim.Converged);
            Assert.Greater(trim.Controls.Yaw, 0.01f,
                $"expected right rudder in a powered climb trim, got {trim.Controls.Yaw:F3}");
        }

        [Test]
        public void SlipstreamIncreasesElevatorAuthorityAtLowSpeed()
        {
            // At 25 m/s, elevator pitch authority must be materially stronger with
            // full power than at idle because the tail sits in the slipstream.
            RigidBodyState state = RigidBodyState.LevelFlight(25f, 100f, 5f * MathUtil.DegToRad);

            float AuthorityAt(float throttle)
            {
                AircraftForces neutral = Forces(_beaver, state, new ControlInputs { Throttle = throttle });
                AircraftForces pulled = Forces(_beaver, state, new ControlInputs { Throttle = throttle, Pitch = 0.5f });
                return MathF.Abs(pulled.Torque.Y - neutral.Torque.Y);
            }

            Assert.Greater(AuthorityAt(1f), 1.25f * AuthorityAt(0f),
                "full-power slipstream should boost elevator authority by >25% at 25 m/s");
        }

        [Test]
        public void WindmillingPropAddsDragAtIdle()
        {
            var withWindmill = BeaverDefinition.Create();
            var without = BeaverDefinition.Create();
            var prop = without.Propeller;
            prop.WindmillDragArea = 0f;
            without.Propeller = prop;

            RigidBodyState state = RigidBodyState.LevelFlight(50f, 200f, 2f * MathUtil.DegToRad);
            var idle = new ControlInputs();
            Vector3 dragWith = Forces(withWindmill, state, idle).Force;
            Vector3 dragWithout = Forces(without, state, idle).Force;

            Assert.Less(dragWith.X, dragWithout.X - 100f,
                "idle prop must add noticeable axial drag");
        }

        [Test]
        public void WindmillDragFadesOutUnderPower()
        {
            PropellerModel prop = _beaver.Propeller;
            float atIdle = prop.WindmillDrag(0f, _beaver.Engine.RatedPower, 50f, 1.225f);
            float atPower = prop.WindmillDrag(0.5f * _beaver.Engine.RatedPower, _beaver.Engine.RatedPower, 50f, 1.225f);
            Assert.Greater(atIdle, 100f);
            Assert.AreEqual(0f, atPower, 1e-3f);
        }

        [Test]
        public void GroundEffectReducesDragNearTheSurface()
        {
            // Same flight state; only the ground distance differs. 2 m CG height is
            // flare height — McCormick's factor only bites below ~0.2 spans.
            RigidBodyState state = RigidBodyState.LevelFlight(30f, 2f, 6f * MathUtil.DegToRad);
            var controls = new ControlInputs();

            Vector3 nearGround = Forces(_beaver, state, controls, groundElevation: 0f).Force;
            Vector3 freeAir = Forces(_beaver, state, controls).Force;

            Assert.Greater(nearGround.X, freeAir.X + 50f,
                "induced drag must shrink in ground effect (less negative axial force)");
            // Lift stays essentially unchanged (only the induced-drag term is modified).
            Assert.AreEqual(freeAir.Z, nearGround.Z, 0.05f * MathF.Abs(freeAir.Z));
        }

        [Test]
        public void GroundEffectVanishesAboveOneSpan()
        {
            RigidBodyState state = RigidBodyState.LevelFlight(30f, 2f * _beaver.ReferenceSpan, 6f * MathUtil.DegToRad);
            var controls = new ControlInputs();
            Vector3 high = Forces(_beaver, state, controls, groundElevation: 0f).Force;
            Vector3 free = Forces(_beaver, state, controls).Force;
            Assert.AreEqual(free.X, high.X, 20f, "two spans up, ground effect should be negligible");
        }

        [Test]
        public void PFactorYawsLeftAtHighAlphaUnderPower()
        {
            var withPFactor = BeaverDefinition.Create();
            var without = BeaverDefinition.Create();
            var prop = without.Propeller;
            prop.PFactorArmPerRad = 0f;
            without.Propeller = prop;

            // High alpha, full power: velocity below the nose direction.
            RigidBodyState state = RigidBodyState.LevelFlight(28f, 300f, 9f * MathUtil.DegToRad);
            var controls = new ControlInputs { Throttle = 1f };

            float yawWith = Forces(withPFactor, state, controls).Torque.Z;
            float yawWithout = Forces(without, state, controls).Torque.Z;
            Assert.Less(yawWith, yawWithout - 50f,
                "P-factor must add nose-left (-Z) yaw at positive alpha under power");
        }
    }
}
