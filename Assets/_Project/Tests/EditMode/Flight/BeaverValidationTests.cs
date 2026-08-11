using System;
using System.Numerics;
using Cirrus.Aircraft;
using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    /// <summary>
    /// CLAUDE.md §4 acceptance gates for any flight-model change, checked against
    /// published DHC-2 Beaver (landplane) performance:
    ///   - stall speed within 3 kt of published
    ///   - cruise at 75% power within 5 kt of published
    ///   - sea-level climb within 100 fpm of published
    ///   - hands-off stability when trimmed
    /// Published figures (see BeaverDefinition source notes, [W]):
    ///   stall 60 mph = 52.1 kt (treated as landing configuration, full flap),
    ///   cruise 143 mph = 124.3 kt, climb 1,020 fpm.
    /// </summary>
    [TestFixture]
    public class BeaverValidationTests
    {
        const float PublishedStallKnots = 52.1f;   // 60 mph [W]
        const float PublishedCruiseKnots = 124.3f; // 143 mph [W]
        const float PublishedClimbFpm = 1020f;     // [W]

        AircraftDefinition _beaver = null!;

        [SetUp]
        public void SetUp() => _beaver = BeaverDefinition.Create();

        [Test]
        public void StallSpeed_FullFlap_WithinThreeKnotsOfPublished()
        {
            float vs = TrimSolver.FindStallSpeed(_beaver, altitude: 0f, flap: 1f);
            float knots = vs * MathUtil.MetersPerSecondToKnots;
            Assert.IsFalse(float.IsNaN(vs));
            Assert.AreEqual(PublishedStallKnots, knots, 3f,
                $"full-flap stall speed {knots:F1} kt vs published {PublishedStallKnots} kt");
        }

        [Test]
        public void StallSpeed_Clean_IsPlausiblyAboveFlappedStall()
        {
            float vs0 = TrimSolver.FindStallSpeed(_beaver, 0f, flap: 1f);
            float vs1 = TrimSolver.FindStallSpeed(_beaver, 0f, flap: 0f);
            float vs1Knots = vs1 * MathUtil.MetersPerSecondToKnots;
            Assert.Greater(vs1, vs0 + 2f, "flaps must reduce stall speed materially");
            // No trustworthy published clean figure on hand yet — bracket it. TODO: POH.
            Assert.That(vs1Knots, Is.InRange(55f, 68f), $"clean stall {vs1Knots:F1} kt");
        }

        [Test]
        public void CruiseSpeed_75PercentPower_WithinFiveKnotsOfPublished()
        {
            float v = TrimSolver.FindCruiseSpeed(_beaver, altitude: 1500f, powerFraction: 0.75f);
            float knots = v * MathUtil.MetersPerSecondToKnots;
            Assert.AreEqual(PublishedCruiseKnots, knots, 5f,
                $"cruise {knots:F1} kt vs published {PublishedCruiseKnots} kt");
        }

        [Test]
        public void ClimbRate_SeaLevel_WithinHundredFpmOfPublished()
        {
            (float roc, float vy) = TrimSolver.FindBestClimb(_beaver, altitude: 0f, minSpeed: 30f, maxSpeed: 45f);
            float fpm = roc * MathUtil.MetersPerSecondToFeetPerMinute;
            Assert.AreEqual(PublishedClimbFpm, fpm, 100f,
                $"best climb {fpm:F0} fpm at {vy * MathUtil.MetersPerSecondToKnots:F0} kt");
        }

        [Test]
        public void Trim_ConvergesAcrossTheNormalEnvelope()
        {
            foreach (float knots in new[] { 70f, 90f, 110f, 120f })
            {
                TrimResult trim = TrimSolver.SolveLevelFlight(_beaver, knots * MathUtil.KnotsToMetersPerSecond, 500f);
                Assert.IsTrue(trim.Converged, $"trim failed at {knots} kt");
                Assert.Less(MathF.Abs(trim.Controls.Pitch), 0.8f, $"elevator nearly saturated at {knots} kt");
            }
        }

        /// <summary>CLAUDE.md §4: trimmed, hands-off, the aircraft flies straight and level.</summary>
        [Test]
        public void HandsOff_WhenTrimmed_StaysStraightAndLevel()
        {
            float speed = 105f * MathUtil.KnotsToMetersPerSecond;
            TrimResult trim = TrimSolver.SolveLevelFlight(_beaver, speed, altitude: 300f);
            Assert.IsTrue(trim.Converged, "trim must converge before the hands-off check");

            RigidBodyState state = trim.State;
            SixDofSimulator.Run(_beaver, ref state, trim.Controls, Vector3.Zero, seconds: 30f);

            Assert.IsFalse(float.IsNaN(state.Position.Z), "simulation diverged");

            // Bank: world-space tilt of the body Y axis.
            Vector3 rightWorld = MathUtil.BodyToWorld(state.Orientation, Vector3.UnitY);
            float bankDeg = MathF.Asin(Math.Clamp(rightWorld.Z, -1f, 1f)) * MathUtil.RadToDeg;

            Assert.Less(MathF.Abs(bankDeg), 10f, $"bank drifted to {bankDeg:F1} deg in 30 s");
            Assert.AreEqual(speed, state.Velocity.Length(), 4f, "airspeed must stay near trim");
            Assert.AreEqual(300f, state.Position.Z, 40f, "altitude must stay near trim (phugoid excursions allowed)");
        }

        /// <summary>Longitudinal static stability: a pitch-up disturbance produces a restoring moment.</summary>
        [Test]
        public void PitchDisturbance_ProducesRestoringMoment()
        {
            float speed = 100f * MathUtil.KnotsToMetersPerSecond;
            TrimResult trim = TrimSolver.SolveLevelFlight(_beaver, speed, 500f);
            Assert.IsTrue(trim.Converged);

            AirState air = IsaAtmosphere.AtAltitude(500f);
            RigidBodyState disturbed = RigidBodyState.LevelFlight(speed, 500f, trim.PitchAngle + 3f * MathUtil.DegToRad);
            AircraftForces loads = FlightDynamics.Compute(
                _beaver, in disturbed, trim.Controls, Vector3.Zero, in air, Span<SurfaceForceInfo>.Empty);

            Assert.Greater(loads.Torque.Y, 200f,
                "3 deg pitch-up must produce a clear nose-down (+Y) restoring moment");
        }

        [Test]
        public void Integration_IsStableAtTheProjectTimestep()
        {
            // A divergent integration at 200 Hz would invalidate the whole timestep choice.
            float speed = 90f * MathUtil.KnotsToMetersPerSecond;
            TrimResult trim = TrimSolver.SolveLevelFlight(_beaver, speed, 300f);
            Assert.IsTrue(trim.Converged);

            RigidBodyState state = trim.State;
            state.AngularVelocity = new Vector3(0.2f, 0.1f, 0.05f); // kick it
            SixDofSimulator.Run(_beaver, ref state, trim.Controls, Vector3.Zero, seconds: 20f);

            Assert.IsFalse(float.IsNaN(state.Velocity.Length()));
            Assert.Less(state.AngularVelocity.Length(), 1f, "rates must decay, not diverge");
        }
    }
}
