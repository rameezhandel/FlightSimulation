using System;
using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    [TestFixture]
    public class AirfoilModelTests
    {
        const float LiftSlope = 5f; // representative AR-corrected slope, per rad

        static AirfoilModel Cambered()
        {
            var airfoil = AirfoilModel.Default();
            airfoil.ZeroLiftAngle = -4f * MathUtil.DegToRad;
            airfoil.StallAnglePositive = 14f * MathUtil.DegToRad;
            airfoil.StallAngleNegative = -12f * MathUtil.DegToRad;
            return airfoil;
        }

        [Test]
        public void LiftIsZeroAtZeroLiftAngle()
        {
            var airfoil = Cambered();
            airfoil.Evaluate(airfoil.ZeroLiftAngle, 0f, LiftSlope, out float cl, out _, out _);
            Assert.AreEqual(0f, cl, 1e-4f);
        }

        [Test]
        public void LiftSlopeInLinearRegionMatches()
        {
            var airfoil = Cambered();
            airfoil.Evaluate(2f * MathUtil.DegToRad, 0f, LiftSlope, out float cl1, out _, out _);
            airfoil.Evaluate(6f * MathUtil.DegToRad, 0f, LiftSlope, out float cl2, out _, out _);
            float slope = (cl2 - cl1) / (4f * MathUtil.DegToRad);
            Assert.AreEqual(LiftSlope, slope, 0.05f);
        }

        /// <summary>CLAUDE.md §4: the lift curve must go PAST the stall angle and come back down.</summary>
        [Test]
        public void LiftDropsAfterStall()
        {
            var airfoil = Cambered();
            airfoil.Evaluate(airfoil.StallAnglePositive, 0f, LiftSlope, out float clAtStall, out _, out _);
            airfoil.Evaluate(airfoil.StallAnglePositive + 8f * MathUtil.DegToRad, 0f, LiftSlope, out float clPastStall, out _, out _);
            airfoil.Evaluate(airfoil.StallAnglePositive + 15f * MathUtil.DegToRad, 0f, LiftSlope, out float clDeepStall, out _, out _);

            Assert.Less(clPastStall, 0.85f * clAtStall, "Cl must drop past the stall angle");
            // Deep stall follows the flat plate: lift partially recovers toward 45 deg,
            // but must stay far below the unstalled line and below CLmax.
            Assert.Less(clDeepStall, clAtStall, "deep-stall Cl must stay below CLmax");
            Assert.Greater(clPastStall, 0f, "post-stall lift should not vanish instantly");
        }

        [Test]
        public void NegativeStallAlsoDrops()
        {
            var airfoil = Cambered();
            float alphaPast = airfoil.StallAngleNegative - 10f * MathUtil.DegToRad;
            airfoil.Evaluate(airfoil.StallAngleNegative, 0f, LiftSlope, out float clAtStall, out _, out _);
            airfoil.Evaluate(alphaPast, 0f, LiftSlope, out float clPastStall, out _, out _);
            // Past negative stall, Cl must break away from the linear lift line toward
            // the (much smaller magnitude) flat-plate value.
            float clLinearExtrapolated = LiftSlope * (alphaPast - airfoil.ZeroLiftAngle);
            Assert.Greater(clPastStall, 0.75f * clLinearExtrapolated,
                "negative-side Cl must be limited by stall, not follow the linear line");
            Assert.Less(clAtStall, 0f);
            Assert.Less(clPastStall, 0f);
        }

        [Test]
        public void DragRisesSteeplyPastStall()
        {
            var airfoil = Cambered();
            airfoil.Evaluate(5f * MathUtil.DegToRad, 0f, LiftSlope, out _, out float cdAttached, out _);
            airfoil.Evaluate(30f * MathUtil.DegToRad, 0f, LiftSlope, out _, out float cdStalled, out _);
            airfoil.Evaluate(90f * MathUtil.DegToRad, 0f, LiftSlope, out _, out float cdPlate, out _);

            Assert.Less(cdAttached, 0.05f);
            Assert.Greater(cdStalled, 0.3f);
            Assert.AreEqual(airfoil.FlatPlateCd90 + airfoil.MinDrag, cdPlate, 0.05f);
        }

        [Test]
        public void FlapIncreasesLiftAtSameAlpha()
        {
            var airfoil = Cambered();
            airfoil.FlapEffectiveness = 0.5f;
            airfoil.Evaluate(4f * MathUtil.DegToRad, 0f, LiftSlope, out float clClean, out float cdClean, out float cmClean);
            airfoil.Evaluate(4f * MathUtil.DegToRad, 30f * MathUtil.DegToRad, LiftSlope, out float clFlapped, out float cdFlapped, out float cmFlapped);

            Assert.Greater(clFlapped, clClean + 0.5f, "30 deg of effective flap should add substantial lift");
            Assert.Greater(cdFlapped, cdClean, "flap adds drag");
            Assert.Less(cmFlapped, cmClean, "flap adds nose-down pitching moment");
        }

        [Test]
        public void CoefficientsAreFiniteEverywhere()
        {
            var airfoil = Cambered();
            airfoil.FlapEffectiveness = 0.5f;
            for (float alpha = -MathF.PI; alpha <= MathF.PI; alpha += 0.02f)
            {
                airfoil.Evaluate(alpha, 0.3f, LiftSlope, out float cl, out float cd, out float cm);
                Assert.IsFalse(float.IsNaN(cl) || float.IsInfinity(cl), $"cl at {alpha}");
                Assert.IsFalse(float.IsNaN(cd) || float.IsInfinity(cd), $"cd at {alpha}");
                Assert.IsFalse(float.IsNaN(cm) || float.IsInfinity(cm), $"cm at {alpha}");
                Assert.GreaterOrEqual(cd, 0f, $"negative drag at {alpha}");
            }
        }
    }
}
