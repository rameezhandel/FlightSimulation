using System;
using System.Numerics;
using Cirrus.Atmosphere;
using NUnit.Framework;

namespace Cirrus.Tests.Atmosphere
{
    [TestFixture]
    public class WindFieldTests
    {
        static WeatherPreset SteadyOnly(float speed, float directionDeg) => new WeatherPreset
        {
            Name = "test",
            SurfaceWindSpeed = speed,
            SurfaceWindDirection = directionDeg,
            GustIntensity = 0f,
            TurbulenceIntensity = 0f,
        };

        [Test]
        public void CalmIsExactlyZero()
        {
            var wind = new WindField(WeatherPreset.Calm);
            Assert.AreEqual(Vector3.Zero, wind.Sample(new Vector3(1000f, -2000f, 500f), 123.4f));
        }

        [Test]
        public void SampleIsDeterministic()
        {
            var a = new WindField(WeatherPreset.Storm, seed: 9);
            var b = new WindField(WeatherPreset.Storm, seed: 9);
            var position = new Vector3(4000f, -1500f, 800f);
            for (float t = 0f; t < 10f; t += 0.37f)
                Assert.AreEqual(a.Sample(position, t), b.Sample(position, t));
        }

        [Test]
        public void MetConvention_WindFromWest_BlowsTowardEast()
        {
            // Wind FROM 270 (west) blows toward the east: +Y in the core frame.
            var wind = new WindField(SteadyOnly(10f, 270f));
            Vector3 v = wind.Sample(new Vector3(0f, 0f, 10f), 0f);
            Assert.AreEqual(10f, v.Y, 0.05f);
            Assert.AreEqual(0f, v.X, 0.05f);
            Assert.AreEqual(0f, v.Z, 1e-3f);
        }

        [Test]
        public void MetConvention_WindFromNorth_BlowsTowardSouth()
        {
            var wind = new WindField(SteadyOnly(8f, 0f));
            Vector3 v = wind.Sample(new Vector3(0f, 0f, 10f), 0f);
            Assert.AreEqual(-8f, v.X, 0.05f);
            Assert.AreEqual(0f, v.Y, 0.05f);
        }

        [Test]
        public void SpeedAtReferenceHeightMatchesThePreset()
        {
            var wind = new WindField(SteadyOnly(6f, 135f));
            Assert.AreEqual(6f, wind.Sample(new Vector3(0f, 0f, 10f), 0f).Length(), 0.05f);
        }

        [Test]
        public void BoundaryLayerProfileGrowsWithAltitudeAndCaps()
        {
            var wind = new WindField(SteadyOnly(10f, 90f));
            float low = wind.Sample(new Vector3(0f, 0f, 5f), 0f).Length();
            float mid = wind.Sample(new Vector3(0f, 0f, 300f), 0f).Length();
            float high = wind.Sample(new Vector3(0f, 0f, 8000f), 0f).Length();
            Assert.Less(low, 10f);
            Assert.Greater(mid, 10f);
            Assert.LessOrEqual(high, 18.01f, "profile must cap at 1.8x");
            Assert.Greater(high, mid - 0.01f);
        }

        [Test]
        public void GustsStayWithinIntensityBounds()
        {
            var preset = SteadyOnly(10f, 200f);
            preset.GustIntensity = 3f;
            var wind = new WindField(preset, seed: 4);

            float min = float.MaxValue, max = float.MinValue;
            for (float t = 0f; t < 300f; t += 0.25f)
            {
                float speed = wind.Sample(new Vector3(0f, 0f, 10f), t).Length();
                min = MathF.Min(min, speed);
                max = MathF.Max(max, speed);
            }
            Assert.That(max, Is.InRange(10.5f, 13.05f), "gusts must add up to ~GustIntensity");
            Assert.That(min, Is.InRange(6.95f, 9.5f), "lulls must subtract up to ~GustIntensity");
        }

        [Test]
        public void TurbulenceIsBoundedAndVariesInSpaceAndTime()
        {
            var preset = WeatherPreset.Calm;
            preset.TurbulenceIntensity = 2f;
            var wind = new WindField(preset, seed: 12);

            float sumSq = 0f;
            int samples = 0;
            Vector3 first = wind.Sample(new Vector3(0f, 0f, 500f), 0f);
            bool varies = false;
            for (float t = 0f; t < 60f; t += 0.2f)
            {
                Vector3 v = wind.Sample(new Vector3(0f, 0f, 500f), t);
                sumSq += v.LengthSquared();
                samples++;
                if ((v - first).Length() > 0.2f) varies = true;
                Assert.Less(v.Length(), 3f * 2f + 0.001f, "turbulence must stay bounded");
            }
            float rms = MathF.Sqrt(sumSq / samples);
            Assert.That(rms, Is.InRange(0.3f, 4f), $"rms {rms:F2} vs intensity 2");
            Assert.IsTrue(varies, "turbulence must actually vary over time");
        }

        [Test]
        public void WindIsTemporallyContinuous()
        {
            var wind = new WindField(WeatherPreset.Storm, seed: 2);
            var position = new Vector3(100f, 200f, 400f);
            Vector3 previous = wind.Sample(position, 0f);
            for (float t = 0.005f; t < 20f; t += 0.005f)
            {
                Vector3 current = wind.Sample(position, t);
                Assert.Less((current - previous).Length(), 0.6f,
                    $"wind jumped at t={t:F3} — flight model needs continuity at 200 Hz");
                previous = current;
            }
        }
    }
}
