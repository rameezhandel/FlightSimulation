using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    [TestFixture]
    public class IsaAtmosphereTests
    {
        // Reference values: U.S. Standard Atmosphere 1976 tables.

        [Test]
        public void SeaLevel_MatchesStandardValues()
        {
            AirState air = IsaAtmosphere.AtAltitude(0f);
            Assert.AreEqual(1.225f, air.Density, 0.001f);
            Assert.AreEqual(101_325f, air.Pressure, 10f);
            Assert.AreEqual(288.15f, air.Temperature, 0.01f);
            Assert.AreEqual(340.3f, air.SpeedOfSound, 0.5f);
        }

        [Test]
        public void FiveThousandMeters_MatchesStandardValues()
        {
            AirState air = IsaAtmosphere.AtAltitude(5000f);
            Assert.AreEqual(0.7364f, air.Density, 0.002f);
            Assert.AreEqual(54_048f, air.Pressure, 100f);
            Assert.AreEqual(255.65f, air.Temperature, 0.05f);
        }

        [Test]
        public void Tropopause_MatchesStandardValues()
        {
            AirState air = IsaAtmosphere.AtAltitude(11_000f);
            Assert.AreEqual(216.65f, air.Temperature, 0.05f);
            Assert.AreEqual(0.3639f, air.Density, 0.002f);
        }

        [Test]
        public void DensityDecreasesMonotonicallyWithAltitude()
        {
            float previous = float.MaxValue;
            for (float h = 0f; h <= 11_000f; h += 500f)
            {
                float density = IsaAtmosphere.AtAltitude(h).Density;
                Assert.Less(density, previous, $"density not decreasing at {h} m");
                previous = density;
            }
        }
    }
}
