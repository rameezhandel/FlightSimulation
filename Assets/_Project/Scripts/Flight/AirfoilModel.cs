using System;

namespace Cirrus.Flight
{
    /// <summary>
    /// Parametric 2D airfoil coefficient model with post-stall continuation.
    ///
    /// Linear region: thin-airfoil lift line with the slope supplied by the caller
    /// (the surface pre-corrects the 2D slope for its own aspect ratio).
    /// Post-stall: flat-plate model, Cn = Cd90 * sin(alpha), so Cl = Cd90*sin*cos falls
    /// past the stall angle and Cd rises toward Cd90 at alpha = 90 deg. The two regimes
    /// are blended over StallBlendRange with a smoothstep.
    ///
    /// The post-stall drop is a hard project requirement (CLAUDE.md §4): a curve that
    /// clamps at CLmax cannot stall.
    ///
    /// Control/flap deflection is modelled as a shift of the zero-lift angle by
    /// tau * delta (classic plain-flap effectiveness), with reduced stall angle,
    /// added profile drag, and the standard nose-down moment increment
    /// dCm ~= -0.25 * dCl (thin airfoil theory, flap at trailing edge).
    ///
    /// Flat-plate post-stall form after Viterna & Corrigan (NASA CP-2230, 1982) as
    /// commonly simplified for real-time simulation.
    /// </summary>
    [Serializable]
    public struct AirfoilModel
    {
        public float ZeroLiftAngle;       // rad; negative for positive camber
        public float StallAnglePositive;  // rad
        public float StallAngleNegative;  // rad (negative value)
        public float StallBlendRange;     // rad, half-width of linear->flat-plate blend
        public float MinDrag;             // Cd at Cl of minimum drag
        public float ClAtMinDrag;
        public float ProfileDragFactor;   // Cd += factor * (Cl - ClAtMinDrag)^2 (profile only; induced drag is added per surface)
        public float MomentCoefficient;   // Cm about quarter chord, linear region
        public float FlapEffectiveness;   // tau: d(alpha_zero_lift)/d(delta), 0..1
        public float FlapDragFactor;      // Cd += factor * delta^2
        public float StallShiftFactor;    // fraction of tau*delta by which flap lowers the stall angle
        public float FlatPlateCd90;       // Cn at alpha = 90 deg; ~1.98 for a flat plate

        public static AirfoilModel Default()
        {
            return new AirfoilModel
            {
                ZeroLiftAngle = 0f,
                StallAnglePositive = 15f * MathUtil.DegToRad,
                StallAngleNegative = -15f * MathUtil.DegToRad,
                StallBlendRange = 3f * MathUtil.DegToRad,
                MinDrag = 0.008f,
                ClAtMinDrag = 0.1f,
                ProfileDragFactor = 0.006f,
                MomentCoefficient = 0f,
                FlapEffectiveness = 0f,
                FlapDragFactor = 0.06f,
                StallShiftFactor = 0.3f,
                FlatPlateCd90 = 1.98f,
            };
        }

        /// <param name="alpha">Angle of attack, rad, any value in [-pi, pi].</param>
        /// <param name="deflection">Control/flap deflection, rad, positive trailing-edge down.</param>
        /// <param name="liftSlope">Lift-curve slope per rad, already corrected for the surface's aspect ratio.</param>
        public void Evaluate(float alpha, float deflection, float liftSlope, out float cl, out float cd, out float cm)
        {
            float tauDelta = FlapEffectiveness * deflection;
            float zeroLift = ZeroLiftAngle - tauDelta;
            float stallPos = StallAnglePositive - StallShiftFactor * tauDelta;
            float stallNeg = StallAngleNegative - StallShiftFactor * tauDelta;
            float dCl0 = liftSlope * tauDelta;

            // Attached-flow (linear) regime.
            float clLinear = liftSlope * (alpha - zeroLift);
            float clExcess = clLinear - ClAtMinDrag;
            float cdLinear = MinDrag + ProfileDragFactor * clExcess * clExcess
                             + FlapDragFactor * deflection * deflection;
            float cmLinear = MomentCoefficient - 0.25f * dCl0;

            // Separated-flow (flat plate) regime, valid for all alpha.
            float sinA = MathF.Sin(alpha);
            float cosA = MathF.Cos(alpha);
            float normalCoefficient = FlatPlateCd90 * sinA;
            float clPlate = normalCoefficient * cosA;
            float cdPlate = FlatPlateCd90 * sinA * sinA + MinDrag;
            float cmPlate = -0.25f * normalCoefficient; // centre of pressure near mid-chord when separated

            // Blend: 0 = attached, 1 = separated. Both stall sides.
            float tPos = MathUtil.Smoothstep(stallPos - StallBlendRange, stallPos + StallBlendRange, alpha);
            float tNeg = MathUtil.Smoothstep(stallNeg + StallBlendRange, stallNeg - StallBlendRange, alpha);
            float t = MathF.Max(tPos, tNeg);

            cl = MathUtil.Lerp(clLinear, clPlate, t);
            cd = MathUtil.Lerp(cdLinear, cdPlate, t);
            cm = MathUtil.Lerp(cmLinear, cmPlate, t);
        }
    }
}
