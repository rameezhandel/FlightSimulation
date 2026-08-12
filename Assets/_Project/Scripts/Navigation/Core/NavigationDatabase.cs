using System;
using System.Collections.Generic;
using Cirrus.Terrain;

namespace Cirrus.Navigation
{
    /// <summary>
    /// The region's airports (CLAUDE.md §1: Juneau, Sitka, Skagway, Haines,
    /// Gustavus — and nothing else, ever).
    ///
    /// **Every value below is [OA]: hand-entered from general knowledge and NOT yet
    /// verified against the OurAirports CSV named in CLAUDE.md §2.** Coordinates
    /// are good to roughly a few hundred metres, elevations and runway dimensions
    /// to the nearest sensible round figure, and true headings are derived from the
    /// published magnetic designator plus the regional variation. Good enough to
    /// fly and to build against; NOT good enough to ship. Replacing this file with
    /// generated data from OurAirports is a tracked task — until then treat any
    /// disagreement with a chart as the chart being right.
    /// </summary>
    public static class SoutheastAlaskaAirports
    {
        public static Airport Juneau { get; } = new Airport(
            "PAJN", "Juneau International",
            new GeoPoint(58.3547, -134.5763), elevationMeters: 6.4f,
            new[] { RunwayFromDesignator(8, lengthMeters: 2713f, widthMeters: 46f, elevationMeters: 6.4f) });

        public static Airport Sitka { get; } = new Airport(
            "PASI", "Sitka Rocky Gutierrez",
            new GeoPoint(57.0471, -135.3616), elevationMeters: 6.4f,
            new[] { RunwayFromDesignator(11, 1981f, 46f, 6.4f) });

        public static Airport Skagway { get; } = new Airport(
            "PAGY", "Skagway",
            new GeoPoint(59.4601, -135.3157), elevationMeters: 13.4f,
            new[] { RunwayFromDesignator(2, 1075f, 23f, 13.4f) });

        public static Airport Haines { get; } = new Airport(
            "PAHN", "Haines",
            new GeoPoint(59.2438, -135.5241), elevationMeters: 4.9f,
            new[] { RunwayFromDesignator(8, 1219f, 23f, 4.9f) });

        public static Airport Gustavus { get; } = new Airport(
            "PAGS", "Gustavus",
            new GeoPoint(58.4253, -135.7073), elevationMeters: 10.7f,
            new[] { RunwayFromDesignator(2, 2004f, 46f, 10.7f) });

        public static IReadOnlyList<Airport> All { get; } = new[]
        {
            Juneau, Sitka, Skagway, Haines, Gustavus,
        };

        /// <summary>
        /// Builds a runway whose true heading is the published magnetic designator
        /// converted with the regional variation, so `PrimaryDesignator` always
        /// round-trips back to the number painted on the asphalt.
        /// </summary>
        static Runway RunwayFromDesignator(int designator, float lengthMeters, float widthMeters, float elevationMeters)
            => new Runway(Bearings.MagneticToTrue(designator * 10f), lengthMeters, widthMeters, elevationMeters);
    }

    /// <summary>Lookup over a set of airports. Small enough that linear scans are the right answer.</summary>
    public sealed class NavigationDatabase
    {
        readonly IReadOnlyList<Airport> _airports;

        public NavigationDatabase(IReadOnlyList<Airport> airports) => _airports = airports;

        public static NavigationDatabase Region { get; } = new NavigationDatabase(SoutheastAlaskaAirports.All);

        public IReadOnlyList<Airport> Airports => _airports;

        public Airport? ByIcao(string icao)
        {
            foreach (Airport airport in _airports)
                if (string.Equals(airport.Icao, icao, StringComparison.OrdinalIgnoreCase))
                    return airport;
            return null;
        }

        /// <summary>Nearest airport to a region-local position, with its distance in metres.</summary>
        public (Airport airport, float distanceMeters)? Nearest(in LocalNE position)
        {
            Airport? best = null;
            float bestDistance = float.MaxValue;
            foreach (Airport airport in _airports)
            {
                float distance = Bearings.DistanceMeters(position, airport.Local);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = airport;
                }
            }
            return best == null ? null : (best, bestDistance);
        }

        /// <summary>Airports within a radius, nearest first.</summary>
        public List<Airport> Within(in LocalNE position, float radiusMeters)
        {
            var found = new List<(Airport airport, float distance)>();
            foreach (Airport airport in _airports)
            {
                float distance = Bearings.DistanceMeters(position, airport.Local);
                if (distance <= radiusMeters) found.Add((airport, distance));
            }
            found.Sort((a, b) => a.distance.CompareTo(b.distance));
            var result = new List<Airport>(found.Count);
            foreach ((Airport airport, float _) in found) result.Add(airport);
            return result;
        }
    }
}
