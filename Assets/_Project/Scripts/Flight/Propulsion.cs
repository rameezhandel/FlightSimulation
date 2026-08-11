using System;

namespace Cirrus.Flight
{
    /// <summary>
    /// Quasi-static piston engine: shaft power = rated * throttle * altitude lapse.
    /// Lapse per Gagg &amp; Ferrar (1934), the standard normally-aspirated approximation:
    /// P/P0 = (sigma - 0.117) / 0.883. RPM dynamics, mixture, and carb heat are
    /// deliberately out of scope for M1 (see docs/flight-model.md §limitations).
    /// </summary>
    [Serializable]
    public struct PistonEngineModel
    {
        public float RatedPower;        // W
        public float RatedAngularSpeed; // rad/s at rated RPM (for reaction torque)

        public float ShaftPower(float throttle, float densityRatio)
        {
            float lapse = MathF.Max(0f, (densityRatio - 0.117f) / 0.883f);
            return RatedPower * Math.Clamp(throttle, 0f, 1f) * lapse;
        }
    }

    /// <summary>
    /// Fixed-pitch propeller thrust from shaft power.
    /// Two asymptotes, smoothly blended (p-norm, n = 3):
    ///   static:  T0 = FoM * (2 * rho * A)^(1/3) * P^(2/3)   (momentum theory ideal
    ///            static thrust scaled by a figure of merit)
    ///   flight:  Tv = eta_max * P / V
    /// This is an M1 approximation; a proper blade-element propeller (with windmilling
    /// drag and P-factor) is planned before the milestone closes. Constants are tuned
    /// against published DHC-2 climb/cruise performance in BeaverValidationTests.
    /// </summary>
    [Serializable]
    public struct PropellerModel
    {
        public float Diameter;            // m
        public float PeakEfficiency;      // ~0.75-0.85
        public float StaticFigureOfMerit; // ~0.5-0.7

        public float Thrust(float shaftPower, float axialSpeed, float density)
        {
            if (shaftPower <= 1f)
                return 0f;

            float area = MathF.PI * Diameter * Diameter * 0.25f;
            float staticThrust = StaticFigureOfMerit
                                 * MathF.Cbrt(2f * density * area)
                                 * MathF.Pow(shaftPower, 2f / 3f);
            float flightThrust = PeakEfficiency * shaftPower / MathF.Max(axialSpeed, 0.5f);

            float s3 = staticThrust * staticThrust * staticThrust;
            float f3 = flightThrust * flightThrust * flightThrust;
            return MathF.Cbrt(s3 * f3 / (s3 + f3));
        }
    }
}
