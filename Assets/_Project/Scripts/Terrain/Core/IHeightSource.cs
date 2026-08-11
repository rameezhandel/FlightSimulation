namespace Cirrus.Terrain
{
    /// <summary>
    /// Terrain elevation provider, region-local coordinates, metres above sea level
    /// (negative = below the sea surface; water rendering covers everything below
    /// Region.SeaLevel). Implementations must be pure, thread-safe, and
    /// deterministic — tile meshing runs on worker threads.
    /// </summary>
    public interface IHeightSource
    {
        float SampleElevation(float north, float east);
    }
}
