using Cirrus.Terrain;
using NUnit.Framework;

namespace Cirrus.Tests.Terrain
{
    [TestFixture]
    public class OriginTrackerTests
    {
        [Test]
        public void ToRegionAddsTheOrigin()
        {
            var tracker = new OriginTracker(new LocalNE(10_000f, -20_000f));
            LocalNE region = tracker.ToRegion(500f, 700f);
            Assert.AreEqual(10_500f, region.North, 1e-3f);
            Assert.AreEqual(-19_300f, region.East, 1e-3f);
        }

        [Test]
        public void NeedsRebaseTriggersAtFiveKilometres()
        {
            var tracker = new OriginTracker(new LocalNE(0f, 0f));
            Assert.IsFalse(tracker.NeedsRebase(3000f, 3000f));   // 4.24 km
            Assert.IsTrue(tracker.NeedsRebase(4000f, 4000f));    // 5.66 km
            Assert.IsFalse(tracker.NeedsRebase(4999f, 0f));
            Assert.IsTrue(tracker.NeedsRebase(5001f, 0f));
        }

        /// <summary>The invariant: a rebase never changes any point's region position.</summary>
        [Test]
        public void RebasePreservesRegionPositions()
        {
            var tracker = new OriginTracker(new LocalNE(40_000f, -60_000f));

            // Some fixed point in the world, currently at these unity-local coords.
            float pointLocalNorth = 7200f, pointLocalEast = -1500f;
            LocalNE before = tracker.ToRegion(pointLocalNorth, pointLocalEast);

            // Focus flew to (6000, 1000): rebase on it.
            (float shiftNorth, float shiftEast) = tracker.Rebase(6000f, 1000f);
            Assert.AreEqual(-6000f, shiftNorth, 1e-3f);
            Assert.AreEqual(-1000f, shiftEast, 1e-3f);

            // The point's unity-local coords move by the shift; region position must not.
            LocalNE after = tracker.ToRegion(pointLocalNorth + shiftNorth, pointLocalEast + shiftEast);
            Assert.AreEqual(before.North, after.North, 1e-2f);
            Assert.AreEqual(before.East, after.East, 1e-2f);
        }

        [Test]
        public void RepeatedRebasesAccumulateWithoutDrift()
        {
            var tracker = new OriginTracker(new LocalNE(0f, 0f));
            // Fly east across most of the region in 5.5 km hops, rebasing each time.
            double flownEast = 0;
            for (int hop = 0; hop < 20; hop++)
            {
                tracker.Rebase(0f, 5500f);
                flownEast += 5500;
            }
            Assert.AreEqual(flownEast, tracker.Origin.East, 0.5f, "20 rebases must not drift");
            Assert.AreEqual(0f, tracker.Origin.North, 1e-3f);
        }

        [Test]
        public void RoundTripLocalRegionLocal()
        {
            var tracker = new OriginTracker(new LocalNE(-120_000f, 90_000f));
            var region = new LocalNE(-118_500f, 91_250f);
            (float north, float east) = tracker.ToLocal(region);
            LocalNE back = tracker.ToRegion(north, east);
            Assert.AreEqual(region.North, back.North, 1e-2f);
            Assert.AreEqual(region.East, back.East, 1e-2f);
        }
    }
}
