using System;
using Cirrus.Navigation;
using Cirrus.Terrain;
using NUnit.Framework;

namespace Cirrus.Tests.Navigation
{
    [TestFixture]
    public class BearingsTests
    {
        [Test]
        public void CardinalBearings()
        {
            var origin = new LocalNE(0f, 0f);
            Assert.AreEqual(0f, Bearings.TrueBearing(origin, new LocalNE(1000f, 0f)), 1e-3f, "north");
            Assert.AreEqual(90f, Bearings.TrueBearing(origin, new LocalNE(0f, 1000f)), 1e-3f, "east");
            Assert.AreEqual(180f, Bearings.TrueBearing(origin, new LocalNE(-1000f, 0f)), 1e-3f, "south");
            Assert.AreEqual(270f, Bearings.TrueBearing(origin, new LocalNE(0f, -1000f)), 1e-3f, "west");
            Assert.AreEqual(45f, Bearings.TrueBearing(origin, new LocalNE(1000f, 1000f)), 1e-3f, "northeast");
        }

        [Test]
        public void NormalizeWrapsBothDirections()
        {
            Assert.AreEqual(10f, Bearings.Normalize(370f), 1e-4f);
            Assert.AreEqual(350f, Bearings.Normalize(-10f), 1e-4f);
            Assert.AreEqual(0f, Bearings.Normalize(720f), 1e-4f);
        }

        [Test]
        public void DifferenceTakesTheShortWayRound()
        {
            Assert.AreEqual(20f, Bearings.Difference(350f, 10f), 1e-4f);
            Assert.AreEqual(-20f, Bearings.Difference(10f, 350f), 1e-4f);
            Assert.AreEqual(180f, MathF.Abs(Bearings.Difference(0f, 180f)), 1e-4f);
        }

        [Test]
        public void MagneticConversionRoundTrips()
        {
            for (float trueBearing = 0f; trueBearing < 360f; trueBearing += 37f)
            {
                float back = Bearings.MagneticToTrue(Bearings.TrueToMagnetic(trueBearing));
                Assert.AreEqual(trueBearing, back, 1e-3f);
            }
        }

        [Test]
        public void EastVariationMakesMagneticLessThanTrue()
        {
            // "Variation east, magnetic least."
            Assert.Less(Bearings.TrueToMagnetic(100f), 100f);
            Assert.AreEqual(100f - Bearings.MagneticVariationEast, Bearings.TrueToMagnetic(100f), 1e-3f);
        }

        [Test]
        public void OffsetAndBearingAreInverses()
        {
            var start = new LocalNE(4000f, -7000f);
            LocalNE moved = Bearings.Offset(start, 237f, 12_500f);
            Assert.AreEqual(237f, Bearings.TrueBearing(start, moved), 1e-2f);
            Assert.AreEqual(12_500f, Bearings.DistanceMeters(start, moved), 1e-1f);
        }
    }

    [TestFixture]
    public class RunwayTests
    {
        [Test]
        public void DesignatorRoundTripsFromTrueHeading()
        {
            // Internal consistency: a runway built from magnetic designator N must
            // report designator N back after the true-heading conversion.
            foreach (int designator in new[] { 1, 8, 11, 18, 26, 29, 36 })
            {
                var runway = new Runway(Bearings.MagneticToTrue(designator * 10f), 2000f, 45f, 10f);
                Assert.AreEqual(designator, runway.PrimaryDesignator, $"designator {designator}");
            }
        }

        [Test]
        public void ReciprocalIsOppositeAndEighteenApart()
        {
            var runway = new Runway(Bearings.MagneticToTrue(80f), 2713f, 46f, 6.4f);
            Assert.AreEqual(8, runway.PrimaryDesignator);
            Assert.AreEqual(26, runway.ReciprocalDesignator);
            Assert.AreEqual("08/26", runway.Name);
            Assert.AreEqual(180f, MathF.Abs(Bearings.Difference(runway.TrueHeading, runway.ReciprocalTrueHeading)), 1e-3f);
        }

        [Test]
        public void ThresholdsAreLengthApartAndStraddleTheCentre()
        {
            var center = new LocalNE(1000f, 2000f);
            var runway = new Runway(90f, 2000f, 45f, 5f);
            LocalNE primary = runway.PrimaryThreshold(center);
            LocalNE reciprocal = runway.ReciprocalThreshold(center);

            Assert.AreEqual(2000f, Bearings.DistanceMeters(primary, reciprocal), 0.1f);
            // Departing runway 09 (true 090) means starting west and running east.
            Assert.Less(primary.East, center.East);
            Assert.Greater(reciprocal.East, center.East);
            Assert.AreEqual(90f, Bearings.TrueBearing(primary, reciprocal), 1e-2f,
                "taking off from the primary threshold flies the runway heading");
        }

        [Test]
        public void PreferredLandingHeadingFacesTheWind()
        {
            Airport juneau = SoutheastAlaskaAirports.Juneau;
            Runway runway = juneau.Runways[0];

            // Wind straight down the primary heading -> land on the primary end.
            Assert.AreEqual(runway.TrueHeading, juneau.PreferredLandingHeading(runway.TrueHeading), 1e-3f);
            // Wind from the other side -> land on the reciprocal.
            Assert.AreEqual(runway.ReciprocalTrueHeading,
                juneau.PreferredLandingHeading(runway.ReciprocalTrueHeading), 1e-3f);
        }
    }

    [TestFixture]
    public class NavigationDatabaseTests
    {
        [Test]
        public void AllRegionAirportsAreInsideTheRegion()
        {
            foreach (Airport airport in SoutheastAlaskaAirports.All)
                Assert.IsTrue(RegionProjection.IsInsideRegion(airport.Local),
                    $"{airport.Icao} projected outside the region");
        }

        [Test]
        public void RegionHasExactlyTheFiveTowns()
        {
            // CLAUDE.md §1 names five. More would be scope creep; fewer, a gap.
            Assert.AreEqual(5, SoutheastAlaskaAirports.All.Count);
            foreach (string icao in new[] { "PAJN", "PASI", "PAGY", "PAHN", "PAGS" })
                Assert.IsNotNull(NavigationDatabase.Region.ByIcao(icao), icao);
        }

        [Test]
        public void LookupIsCaseInsensitiveAndMissesReturnNull()
        {
            Assert.AreSame(SoutheastAlaskaAirports.Juneau, NavigationDatabase.Region.ByIcao("pajn"));
            Assert.IsNull(NavigationDatabase.Region.ByIcao("KSFO"));
        }

        [Test]
        public void NearestFindsTheObviousAirport()
        {
            LocalNE nearSkagway = Bearings.Offset(SoutheastAlaskaAirports.Skagway.Local, 45f, 3000f);
            (Airport airport, float distanceMeters)? nearest = NavigationDatabase.Region.Nearest(nearSkagway);
            Assert.IsNotNull(nearest);
            Assert.AreEqual("PAGY", nearest!.Value.airport.Icao);
            Assert.AreEqual(3000f, nearest.Value.distanceMeters, 1f);
        }

        [Test]
        public void WithinReturnsNearestFirst()
        {
            var list = NavigationDatabase.Region.Within(SoutheastAlaskaAirports.Juneau.Local, 300_000f);
            Assert.AreEqual("PAJN", list[0].Icao, "the airport you are standing on comes first");
            for (int i = 1; i < list.Count; i++)
            {
                float previous = Bearings.DistanceMeters(SoutheastAlaskaAirports.Juneau.Local, list[i - 1].Local);
                float current = Bearings.DistanceMeters(SoutheastAlaskaAirports.Juneau.Local, list[i].Local);
                Assert.LessOrEqual(previous, current);
            }
        }

        /// <summary>
        /// Gross-error check only. The hand-entered [OA] coordinates put Juneau
        /// 130 km from Skagway; commonly quoted air distances up the Lynn Canal sit
        /// around 90 statute miles (~145 km), so the bracket is deliberately wide —
        /// it catches a sign flip or a degree typo, and nothing finer. Tightening
        /// this is part of replacing the catalogue with OurAirports data.
        /// </summary>
        [Test]
        public void JuneauToSkagwayIsPlausiblySpaced()
        {
            float distance = Bearings.DistanceMeters(
                SoutheastAlaskaAirports.Juneau.Local, SoutheastAlaskaAirports.Skagway.Local);
            Assert.That(distance, Is.InRange(100_000f, 160_000f), $"{distance / 1000f:F1} km");
        }
    }

    [TestFixture]
    public class FlightPlanTests
    {
        static FlightPlan ThreeLegPlan() => new FlightPlan(new[]
        {
            new Waypoint("START", new LocalNE(0f, 0f)),
            new Waypoint("EAST", new LocalNE(0f, 10_000f)),
            new Waypoint("NORTHEAST", new LocalNE(10_000f, 10_000f)),
        });

        [Test]
        public void LegGeometryIsRight()
        {
            FlightPlan plan = ThreeLegPlan();
            Assert.AreEqual(2, plan.LegCount);
            Assert.AreEqual(10_000f, plan.LegDistance(0), 0.1f);
            Assert.AreEqual(90f, plan.LegCourseTrue(0), 1e-3f);
            Assert.AreEqual(0f, plan.LegCourseTrue(1), 1e-3f);
            Assert.AreEqual(20_000f, plan.TotalDistanceMeters, 0.1f);
        }

        [Test]
        public void OnCourseHasNoCrossTrackError()
        {
            LegProgress progress = ThreeLegPlan().Progress(0, new LocalNE(0f, 4000f));
            Assert.AreEqual(0f, progress.CrossTrackMeters, 1e-2f);
            Assert.AreEqual(4000f, progress.AlongTrackMeters, 1e-2f);
            Assert.AreEqual(6000f, progress.RemainingMeters, 1e-2f);
        }

        [Test]
        public void CrossTrackIsPositiveRightOfCourse()
        {
            // Flying east; being SOUTH of the course line is being right of it.
            LegProgress south = ThreeLegPlan().Progress(0, new LocalNE(-500f, 4000f));
            Assert.Greater(south.CrossTrackMeters, 0f);
            Assert.AreEqual(500f, south.CrossTrackMeters, 1e-2f);

            LegProgress north = ThreeLegPlan().Progress(0, new LocalNE(500f, 4000f));
            Assert.Less(north.CrossTrackMeters, 0f);
        }

        [Test]
        public void PastTheEndRemainingClampsToZero()
        {
            LegProgress progress = ThreeLegPlan().Progress(0, new LocalNE(0f, 12_000f));
            Assert.AreEqual(0f, progress.RemainingMeters, 1e-3f);
            Assert.Greater(progress.AlongTrackMeters, 10_000f, "along-track keeps counting past the waypoint");
        }

        [Test]
        public void RemainingDistanceAddsUpTheLaterLegs()
        {
            FlightPlan plan = ThreeLegPlan();
            float remaining = plan.RemainingDistance(0, new LocalNE(0f, 2000f));
            Assert.AreEqual(8000f + 10_000f, remaining, 0.1f);
        }

        [Test]
        public void InvalidLegThrows()
        {
            FlightPlan plan = ThreeLegPlan();
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.Progress(5, new LocalNE(0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.LegDistance(-1));
        }

        [Test]
        public void TheM2GateFlightIsFlyable()
        {
            FlightPlan plan = FlightPlan.JuneauToSkagway();
            Assert.AreEqual(1, plan.LegCount);
            Assert.That(plan.TotalDistanceMeters, Is.InRange(100_000f, 160_000f));
            // North-northwest, up the Lynn Canal.
            Assert.That(plan.LegCourseTrue(0), Is.InRange(315f, 355f));
        }
    }

    [TestFixture]
    public class RadioNavigationTests
    {
        static readonly Navaid Station = new Navaid(
            "TST", "Test", NavaidKind.VorDme, new GeoPoint(58.25, -135.0), 110.0f, serviceRangeMeters: 100_000f);

        [Test]
        public void RadialIsTheMagneticBearingFromTheStation()
        {
            LocalNE north = Bearings.Offset(Station.Local, 0f, 20_000f);
            Assert.AreEqual(Bearings.TrueToMagnetic(0f), Station.RadialFrom(north), 1e-2f);

            LocalNE east = Bearings.Offset(Station.Local, 90f, 20_000f);
            Assert.AreEqual(Bearings.TrueToMagnetic(90f), Station.RadialFrom(east), 1e-2f);
        }

        [Test]
        public void BearingToIsTheReciprocalOfTheRadial()
        {
            LocalNE position = Bearings.Offset(Station.Local, 137f, 30_000f);
            float delta = Bearings.Difference(Station.RadialFrom(position), Station.BearingTo(position));
            Assert.AreEqual(180f, MathF.Abs(delta), 1e-2f);
        }

        [Test]
        public void RelativeBearingIsZeroWhenPointedAtTheStation()
        {
            LocalNE position = Bearings.Offset(Station.Local, 200f, 25_000f);
            float headingAtStation = Bearings.TrueBearing(position, Station.Local);
            Assert.AreEqual(0f, Station.RelativeBearing(position, headingAtStation), 1e-2f);
            // Turn 90 deg left: the needle swings to the right side, 90 deg.
            Assert.AreEqual(90f, Station.RelativeBearing(position, headingAtStation - 90f), 1e-2f);
        }

        [Test]
        public void DmeIsSlantRangeNotGroundRange()
        {
            LocalNE position = Bearings.Offset(Station.Local, 0f, 3000f);
            Assert.AreEqual(3000f, Station.SlantRange(position, 0f), 1f);
            Assert.AreEqual(5000f, Station.SlantRange(position, 4000f), 1f, "3-4-5 triangle overhead-ish");
        }

        [Test]
        public void OutOfRangeFlagsTheIndicatorOff()
        {
            LocalNE faraway = Bearings.Offset(Station.Local, 45f, 150_000f);
            CourseDeviation deviation = RadioNavigation.Deviation(Station, faraway, 1000f, 45f);
            Assert.IsFalse(deviation.Valid);
        }

        [Test]
        public void OnTheSelectedRadialTheNeedleIsCentredWithFromFlag()
        {
            float trueRadial = 60f;
            LocalNE position = Bearings.Offset(Station.Local, trueRadial, 30_000f);
            float selected = Bearings.TrueToMagnetic(trueRadial);

            CourseDeviation deviation = RadioNavigation.Deviation(Station, position, 1500f, selected);
            Assert.IsTrue(deviation.Valid);
            Assert.AreEqual(0f, deviation.Dots, 1e-2f);
            Assert.IsFalse(deviation.To, "flying outbound on the radial reads FROM");
        }

        [Test]
        public void SelectingTheInboundCourseReadsTo()
        {
            float trueRadial = 60f;
            LocalNE position = Bearings.Offset(Station.Local, trueRadial, 30_000f);
            float selectedInbound = Bearings.Normalize(Bearings.TrueToMagnetic(trueRadial) + 180f);

            CourseDeviation deviation = RadioNavigation.Deviation(Station, position, 1500f, selectedInbound);
            Assert.IsTrue(deviation.To);
            Assert.AreEqual(0f, deviation.Dots, 1e-2f);
        }

        [Test]
        public void NeedleDeflectsTowardTheCourseAndSaturates()
        {
            float trueRadial = 60f;
            float selected = Bearings.TrueToMagnetic(trueRadial);

            // Sitting on the 65-deg radial with 60 selected: the course lies to the
            // right of the aircraft's radial, so the needle goes right.
            LocalNE offRight = Bearings.Offset(Station.Local, trueRadial + 5f, 30_000f);
            CourseDeviation five = RadioNavigation.Deviation(Station, offRight, 1500f, selected);
            Assert.Less(five.Dots, 0f);
            Assert.AreEqual(-1.25f, five.Dots, 0.05f, "5 deg of 10 deg full scale = half of 2.5 dots");

            LocalNE wayOff = Bearings.Offset(Station.Local, trueRadial + 40f, 30_000f);
            CourseDeviation saturated = RadioNavigation.Deviation(Station, wayOff, 1500f, selected);
            Assert.AreEqual(-RadioNavigation.VorFullScaleDots, saturated.Dots, 1e-3f, "pegged at full scale");
        }
    }
}
