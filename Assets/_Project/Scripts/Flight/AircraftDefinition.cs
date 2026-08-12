using System;
using System.Numerics;

namespace Cirrus.Flight
{
    /// <summary>
    /// Complete physical description of one aircraft, in the body frame with the
    /// origin at the (fixed, for M1) centre of gravity. Built once at load time —
    /// later from a ScriptableObject — and treated as immutable at runtime.
    /// </summary>
    public sealed class AircraftDefinition
    {
        public string Name = "";
        public float Mass;                  // kg
        public Vector3 InertiaDiagonal;     // Ixx, Iyy, Izz, kg m^2 (body axes; Ixz neglected for M1)
        public AeroSurfaceDefinition[] Surfaces = Array.Empty<AeroSurfaceDefinition>();

        /// <summary>Fuselage + interference parasite drag as an equivalent flat-plate area, m^2, acting at the CG.</summary>
        public float EquivalentFlatPlateArea;

        public PistonEngineModel Engine;
        public PropellerModel Propeller;
        public Vector3 PropellerPosition;   // thrust application point, body frame
        public Vector3 PropellerAxis = Vector3.UnitX;

        /// <summary>2D lift-curve slope shared by all surfaces before AR correction. Thin-airfoil 2*pi.</summary>
        public float SectionLiftSlope = 2f * MathF.PI;

        public float ReferenceArea;  // m^2, for coefficient readouts only
        public float ReferenceChord; // m
        public float ReferenceSpan;  // m, wing span; drives the ground-effect height ratio
    }
}
