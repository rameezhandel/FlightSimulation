using System;

namespace Cirrus.Terrain
{
    public readonly struct GeoPoint
    {
        public readonly double Latitude;   // deg, +N
        public readonly double Longitude;  // deg, +E

        public GeoPoint(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }
    }

    /// <summary>Region-local horizontal position: metres north / east of the region centre.</summary>
    public readonly struct LocalNE
    {
        public readonly float North;
        public readonly float East;

        public LocalNE(float north, float east)
        {
            North = north;
            East = east;
        }
    }

    /// <summary>
    /// The one and only region (CLAUDE.md §1): Southeast Alaska, a 280 km square
    /// centred between the five towns. Sitka (south) to Skagway (north) span
    /// ~268 km, so "roughly 250 km" rounds up to 280 to fit them with margin.
    /// Tile sizes are Size/2^level — floats, nothing requires a power of two.
    /// There is deliberately no code path for any other region.
    /// </summary>
    public static class Region
    {
        public const double CenterLatitude = 58.25;
        public const double CenterLongitude = -135.0;
        public const float Size = 280_000f;      // m
        public const float HalfSize = Size / 2f;
        public const float SeaLevel = 0f;
    }

    /// <summary>
    /// Geodetic &lt;-&gt; region-local conversion: equirectangular projection scaled at
    /// the region centre latitude. Over this region the east-west scale error grows
    /// to ~±4% at the extreme latitudes — accepted for gameplay (positions of
    /// airports/nav data all go through this same projection, so everything stays
    /// self-consistent). Revisit with UTM zone 8N if survey-grade placement is ever
    /// needed. Spherical earth radius per WGS-84 mean.
    /// </summary>
    public static class RegionProjection
    {
        const double EarthRadius = 6_371_000.0;
        const double DegToRad = Math.PI / 180.0;
        static readonly double MetersPerDegreeLat = EarthRadius * DegToRad;
        static readonly double MetersPerDegreeLon = EarthRadius * DegToRad * Math.Cos(Region.CenterLatitude * DegToRad);

        public static LocalNE ToLocal(in GeoPoint p)
            => new LocalNE(
                (float)((p.Latitude - Region.CenterLatitude) * MetersPerDegreeLat),
                (float)((p.Longitude - Region.CenterLongitude) * MetersPerDegreeLon));

        public static GeoPoint ToGeo(in LocalNE p)
            => new GeoPoint(
                Region.CenterLatitude + p.North / MetersPerDegreeLat,
                Region.CenterLongitude + p.East / MetersPerDegreeLon);

        public static bool IsInsideRegion(in LocalNE p)
            => MathF.Abs(p.North) <= Region.HalfSize && MathF.Abs(p.East) <= Region.HalfSize;
    }
}
