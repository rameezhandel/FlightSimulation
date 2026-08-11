using System;
using System.Numerics;
using Cirrus.Flight;

namespace Cirrus.Aircraft
{
    /// <summary>
    /// de Havilland Canada DHC-2 Beaver, landplane configuration.
    ///
    /// Sources for constants:
    ///  [W]   DHC-2 published type data (Wikipedia spec sheet, landplane):
    ///        MTOW 5,100 lb; span 48 ft; wing area 250 sq ft; cruise 143 mph;
    ///        stall 60 mph; climb 1,020 ft/min; P&amp;W R-985, 450 hp @ 2,300 rpm.
    ///        TODO: verify every [W] figure against a DHC-2 POH/AFM before M1 close.
    ///  [FDC] Moments of inertia from the DHC-2 "Beaver" dataset of the FDC 1.2
    ///        flight-dynamics toolbox (M.O. Rauw), derived from NRC flight tests.
    ///        TODO: verify units/values against the FDC manual.
    ///  [EST] Estimated from 3-view drawings or typical GA values; tuned so the
    ///        validation suite (BeaverValidationTests) matches [W] performance.
    ///        These are the knobs to touch when re-tuning — never touch [W] targets.
    ///
    /// The float variant (project reference aircraft, CLAUDE.md §4) will extend this
    /// with float mass + drag once float-specific POH performance data is on hand;
    /// validating against solid landplane numbers first.
    /// </summary>
    public static class BeaverDefinition
    {
        const float WingSpan = 14.63f;        // m (48 ft) [W]
        const float WingArea = 23.23f;        // m^2 (250 sq ft) [W]
        const float WingChord = WingArea / WingSpan;
        const float WingAspectRatio = WingSpan * WingSpan / WingArea; // 9.22
        const float WingIncidence = 2f * MathUtil.DegToRad;   // [EST]
        const float Dihedral = 1f * MathUtil.DegToRad;        // [EST]
        const float WingForeOffset = 0.24f;   // m, wing AC ahead of CG -> ~20% static margin with this tail [EST]
        const float WingHeight = 1.1f;        // m above CG (high wing) [EST]

        const float TailArm = 5.2f;           // m, CG to horizontal tail AC [EST]
        const float HTailArea = 4.6f;         // m^2 total [EST]
        const float HTailAspectRatio = 4.8f;  // [EST]
        const float HTailIncidence = -1.5f * MathUtil.DegToRad; // [EST]
        const float FinArea = 2.8f;           // m^2 [EST]
        const float FinAspectRatio = 1.8f;    // [EST]

        public static AircraftDefinition Create()
        {
            var wingAirfoil = AirfoilModel.Default();
            wingAirfoil.ZeroLiftAngle = -4f * MathUtil.DegToRad;  // cambered section [EST]
            wingAirfoil.StallAnglePositive = 15f * MathUtil.DegToRad; // [EST] tuned: wing CLmax ~1.64 clean
            wingAirfoil.StallAngleNegative = -12f * MathUtil.DegToRad;
            wingAirfoil.MinDrag = 0.009f;
            wingAirfoil.ClAtMinDrag = 0.3f;
            wingAirfoil.MomentCoefficient = -0.08f;               // [EST]

            var flapAirfoil = wingAirfoil;
            flapAirfoil.FlapEffectiveness = 0.5f;                 // wide slotted flap [EST]
            flapAirfoil.StallShiftFactor = 0.25f;                 // slotted flaps keep flow attached longer [EST]

            var aileronAirfoil = wingAirfoil;
            aileronAirfoil.FlapEffectiveness = 0.4f;              // [EST]

            var tailAirfoil = AirfoilModel.Default();             // symmetric section
            tailAirfoil.FlapEffectiveness = 0.55f;                // elevator/rudder chord fraction [EST]

            var surfaces = new AeroSurfaceDefinition[13];
            int index = 0;

            // Wing: 5 strips per side, equal width. Inner 3 carry flap, outer 2 aileron.
            // (Beaver flaps span most of the wing and the ailerons droop with them;
            // droop is not modelled yet — [EST], see docs/flight-model.md.)
            float stripWidth = 0.5f * WingSpan / 5f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                {
                    float y = side * (stripWidth * (i + 0.5f));
                    bool isFlap = i < 3;
                    surfaces[index++] = new AeroSurfaceDefinition
                    {
                        Name = (side < 0 ? "WingL" : "WingR") + i,
                        Position = new Vector3(WingForeOffset, y, WingHeight),
                        Forward = OrientForward(WingIncidence),
                        Up = OrientUp(WingIncidence, side * Dihedral),
                        Area = WingChord * stripWidth,
                        Chord = WingChord,
                        AspectRatio = WingAspectRatio,
                        OswaldFactor = 0.8f,
                        Airfoil = isFlap ? flapAirfoil : aileronAirfoil,
                        Control = isFlap ? ControlAxis.Flap : ControlAxis.Roll,
                        // Flaps: trailing edge down on both sides. Ailerons: stick right
                        // (+input) drops the LEFT aileron and raises the right one.
                        MaxControlDeflection = (isFlap ? 50f : 20f) * MathUtil.DegToRad, // [EST]
                        ControlGain = isFlap ? 1f : -side,
                    };
                }
            }

            // Horizontal tail, one strip per side, elevator across the span.
            // Pull (+pitch input) deflects the elevator trailing-edge UP -> gain -1.
            for (int side = -1; side <= 1; side += 2)
            {
                surfaces[index++] = new AeroSurfaceDefinition
                {
                    Name = side < 0 ? "HTailL" : "HTailR",
                    Position = new Vector3(-TailArm, side * 1.15f, 0.5f),
                    Forward = OrientForward(HTailIncidence),
                    Up = OrientUp(HTailIncidence, 0f),
                    Area = 0.5f * HTailArea,
                    Chord = 1.0f,
                    AspectRatio = HTailAspectRatio,
                    OswaldFactor = 0.8f,
                    Airfoil = tailAirfoil,
                    Control = ControlAxis.Pitch,
                    MaxControlDeflection = 25f * MathUtil.DegToRad, // [EST]
                    ControlGain = -1f,
                };
            }

            // Vertical fin + rudder. Up = -Y (lift-positive toward the left) so that
            // positive rudder input (+yaw, nose right) pushes the tail left.
            surfaces[index] = new AeroSurfaceDefinition
            {
                Name = "Fin",
                Position = new Vector3(-5.0f, 0f, 1.0f),
                Forward = Vector3.UnitX,
                Up = -Vector3.UnitY,
                Area = FinArea,
                Chord = 1.25f,
                AspectRatio = FinAspectRatio,
                OswaldFactor = 0.8f,
                Airfoil = tailAirfoil,
                Control = ControlAxis.Yaw,
                MaxControlDeflection = 25f * MathUtil.DegToRad, // [EST]
                ControlGain = 1f,
            };

            return new AircraftDefinition
            {
                Name = "DHC-2 Beaver",
                Mass = 2313f,                                     // kg, MTOW 5,100 lb [W]
                InertiaDiagonal = new Vector3(5368f, 6929f, 11159f), // kg m^2 [FDC]
                Surfaces = surfaces,
                EquivalentFlatPlateArea = 0.70f,                  // m^2, tuned for total CD0 ~0.042 [EST]
                Engine = new PistonEngineModel
                {
                    RatedPower = 335_566f,                        // W, 450 hp [W]
                    RatedAngularSpeed = 240.9f,                   // rad/s, 2,300 rpm [W]
                },
                Propeller = new PropellerModel
                {
                    Diameter = 2.59f,                             // m, 8 ft 6 in [EST]
                    PeakEfficiency = 0.82f,                       // [EST] tuned vs cruise
                    StaticFigureOfMerit = 0.50f,                  // [EST] tuned vs climb
                },
                PropellerPosition = new Vector3(2.3f, 0f, 0f),    // [EST]
                PropellerAxis = Vector3.UnitX,
                ReferenceArea = WingArea,
                ReferenceChord = WingChord,
            };
        }

        static Vector3 OrientForward(float incidence)
            => new Vector3(MathF.Cos(incidence), 0f, MathF.Sin(incidence));

        static Vector3 OrientUp(float incidence, float dihedral)
            => Vector3.Normalize(new Vector3(-MathF.Sin(incidence), -MathF.Sin(dihedral), MathF.Cos(incidence)));
    }
}
