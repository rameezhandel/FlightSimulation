using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    [TestFixture]
    public class PropulsionTests
    {
        static PistonEngineModel Engine() => new PistonEngineModel
        {
            RatedPower = 335_566f, // 450 hp
            RatedAngularSpeed = 240.9f,
        };

        static PropellerModel Prop() => new PropellerModel
        {
            Diameter = 2.59f,
            PeakEfficiency = 0.8f,
            StaticFigureOfMerit = 0.6f,
        };

        [Test]
        public void FullThrottleSeaLevel_GivesRatedPower()
        {
            Assert.AreEqual(335_566f, Engine().ShaftPower(1f, 1f), 1f);
        }

        [Test]
        public void PowerLapsesWithAltitude()
        {
            PistonEngineModel engine = Engine();
            float sigma3000 = IsaAtmosphere.AtAltitude(3000f).Density / IsaAtmosphere.SeaLevelDensity;
            float lapsed = engine.ShaftPower(1f, sigma3000);
            Assert.Less(lapsed, 0.85f * engine.RatedPower);
            Assert.Greater(lapsed, 0.55f * engine.RatedPower);
        }

        [Test]
        public void IdleProducesNoPower()
        {
            Assert.AreEqual(0f, Engine().ShaftPower(0f, 1f), 1e-3f);
            Assert.AreEqual(0f, Prop().Thrust(0f, 30f, 1.225f), 1e-3f);
        }

        [Test]
        public void StaticThrustIsFiniteAndLarge()
        {
            float thrust = Prop().Thrust(335_566f, 0f, 1.225f);
            Assert.Greater(thrust, 4000f, "a 450 hp bush plane pulls well over 4 kN static");
            Assert.Less(thrust, 12_000f, "static thrust cannot exceed the momentum-theory ideal");
        }

        [Test]
        public void ThrustDecreasesMonotonicallyWithSpeed()
        {
            PropellerModel prop = Prop();
            float previous = float.MaxValue;
            for (float v = 0f; v <= 80f; v += 5f)
            {
                float thrust = prop.Thrust(335_566f, v, 1.225f);
                Assert.Less(thrust, previous + 1e-3f, $"thrust rose at {v} m/s");
                previous = thrust;
            }
        }

        [Test]
        public void CruiseEfficiencyIsRealistic()
        {
            // At cruise speed the blend must approach eta_max, not throw half the power away.
            float v = 64f;
            float thrust = Prop().Thrust(335_566f, v, 1.225f);
            float eta = thrust * v / 335_566f;
            Assert.Greater(eta, 0.65f);
            Assert.Less(eta, 0.85f);
        }
    }
}
