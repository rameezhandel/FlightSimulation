using System;
using Cirrus.Terrain;

namespace Cirrus.Navigation
{
    /// <summary>
    /// Bearing/distance arithmetic in the region-local frame (north/east metres).
    /// Over a 280 km region a flat-earth treatment is well inside anything a pilot
    /// could perceive, and it keeps navigation consistent with the terrain, which
    /// uses the same projection (see RegionProjection).
    ///
    /// Bearings are degrees clockwise from TRUE north unless a name says magnetic.
    /// </summary>
    public static class Bearings
    {
        /// <summary>
        /// Magnetic variation for the region, degrees EAST (true = magnetic + variation).
        /// Southeast Alaska runs roughly 18–21° E and drifts ~0.1°/year.
        /// [EST] one constant for the whole region; refine from a WMM sample if
        /// instrument work ever needs better than a degree.
        /// </summary>
        public const float MagneticVariationEast = 19.5f;

        public static float TrueToMagnetic(float trueBearing) => Normalize(trueBearing - MagneticVariationEast);

        public static float MagneticToTrue(float magneticBearing) => Normalize(magneticBearing + MagneticVariationEast);

        /// <summary>Wrap into [0, 360).</summary>
        public static float Normalize(float degrees)
        {
            float wrapped = degrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        /// <summary>Signed smallest angle from a to b, in (-180, 180].</summary>
        public static float Difference(float a, float b)
        {
            float delta = Normalize(b - a);
            return delta > 180f ? delta - 360f : delta;
        }

        public static float DistanceMeters(in LocalNE from, in LocalNE to)
        {
            float dn = to.North - from.North;
            float de = to.East - from.East;
            return MathF.Sqrt(dn * dn + de * de);
        }

        /// <summary>True bearing from one point to another, degrees.</summary>
        public static float TrueBearing(in LocalNE from, in LocalNE to)
        {
            float dn = to.North - from.North;
            float de = to.East - from.East;
            return Normalize(MathF.Atan2(de, dn) * (180f / MathF.PI));
        }

        /// <summary>Unit direction (north, east) of a true bearing.</summary>
        public static LocalNE Direction(float trueBearing)
        {
            float rad = trueBearing * (MathF.PI / 180f);
            return new LocalNE(MathF.Cos(rad), MathF.Sin(rad));
        }

        /// <summary>Offset a point by a distance along a true bearing.</summary>
        public static LocalNE Offset(in LocalNE origin, float trueBearing, float distanceMeters)
        {
            LocalNE unit = Direction(trueBearing);
            return new LocalNE(origin.North + unit.North * distanceMeters, origin.East + unit.East * distanceMeters);
        }
    }
}
