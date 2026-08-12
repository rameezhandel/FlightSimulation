using Cirrus.Terrain;

namespace Cirrus.Navigation
{
    /// <summary>
    /// Bridges navigation data to terrain generation: flattens runway pads into a
    /// height source so aircraft have something level to land on. Lives in
    /// Navigation because the airport data does; Terrain supplies the generic,
    /// data-free pad decorator.
    /// </summary>
    public static class RunwayTerrain
    {
        /// <summary>Wraps a height source with a flattened pad for every runway of the given airport.</summary>
        public static IHeightSource WithPads(IHeightSource source, Airport airport)
        {
            foreach (Runway runway in airport.Runways)
            {
                source = new RunwayPadHeightSource(
                    source,
                    airport.Local,
                    runway.TrueHeading,
                    runway.LengthMeters,
                    runway.WidthMeters,
                    runway.ElevationMeters);
            }
            return source;
        }

        /// <summary>Wraps a height source with pads for every airport in the region.</summary>
        public static IHeightSource WithAllRegionPads(IHeightSource source)
        {
            foreach (Airport airport in SoutheastAlaskaAirports.All)
                source = WithPads(source, airport);
            return source;
        }
    }
}
