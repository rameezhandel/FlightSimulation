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
        public float WindmillDragArea;    // m^2 equivalent flat plate when the engine is at idle
        public float PFactorArmPerRad;    // m of lateral thrust-centroid shift per rad of alpha
        public float SwirlFraction;       // tangential slipstream component as a fraction of the axial increment

        public float DiscArea => MathF.PI * Diameter * Diameter * 0.25f;

        public float Thrust(float shaftPower, float axialSpeed, float density)
        {
            if (shaftPower <= 1f)
                return 0f;

            float staticThrust = StaticFigureOfMerit
                                 * MathF.Cbrt(2f * density * DiscArea)
                                 * MathF.Pow(shaftPower, 2f / 3f);
            float flightThrust = PeakEfficiency * shaftPower / MathF.Max(axialSpeed, 0.5f);

            float s3 = staticThrust * staticThrust * staticThrust;
            float f3 = flightThrust * flightThrust * flightThrust;
            return MathF.Cbrt(s3 * f3 / (s3 + f3));
        }

        /// <summary>
        /// Far-slipstream axial velocity increment from actuator-disc momentum theory:
        /// w = sqrt(V^2 + 2T / (rho A)) - V. The tail sees a fraction of this
        /// (per-surface SlipstreamAxial factor).
        /// </summary>
        public float SlipstreamIncrement(float thrust, float axialSpeed, float density)
        {
            if (thrust <= 0f)
                return 0f;
            return MathF.Sqrt(axialSpeed * axialSpeed + 2f * thrust / (density * DiscArea)) - axialSpeed;
        }

        /// <summary>Idle/windmilling propeller drag, fading in as shaft power drops below 5% rated.</summary>
        public float WindmillDrag(float shaftPower, float ratedPower, float axialSpeed, float density)
        {
            float idleness = 1f - Math.Clamp(shaftPower / (0.05f * MathF.Max(ratedPower, 1f)), 0f, 1f);
            if (idleness <= 0f)
                return 0f;
            return idleness * 0.5f * density * axialSpeed * axialSpeed * WindmillDragArea;
        }
    }
}
