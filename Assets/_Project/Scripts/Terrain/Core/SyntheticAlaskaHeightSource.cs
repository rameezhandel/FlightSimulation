using System;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Deterministic synthetic Southeast-Alaska-flavoured terrain: ridged-fractal
    /// mountain ranges carved by wide, domain-warped fjord channels flooded by the
    /// sea. Exists so M2 is flyable before the real Copernicus DEM tiles come out
    /// of tools/terrain-pipeline; the streamer swaps in the file-backed source
    /// without touching anything else (both are IHeightSource).
    ///
    /// Thread-safe, allocation-free, same seed = same world.
    /// </summary>
    public sealed class SyntheticAlaskaHeightSource : IHeightSource
    {
        readonly int _seed;

        public SyntheticAlaskaHeightSource(int seed = 58) => _seed = seed;

        public float SampleElevation(float north, float east)
        {
            // Mountains: ridged fBM, ~10 km primary wavelength, up to ~1700 m.
            float mountains = RidgedFbm(north * 0.0001f, east * 0.0001f, octaves: 5, seed: _seed);
            float elevation = mountains * 1700f;

            // Fjords: two families of wide channels cut through everything.
            // Domain-warped stripes read as glacially-carved waterways.
            float warp = 4000f * (ValueNoise(north * 0.00005f, east * 0.00005f, _seed + 17) - 0.5f);
            float channelA = ChannelMask((north + warp) * 0.00003f, (east - warp) * 0.00008f, _seed + 31);
            float channelB = ChannelMask((east + warp * 0.7f) * 0.00003f, (north + warp * 0.4f) * 0.00007f, _seed + 47);
            float channel = MathF.Max(channelA, channelB);
            elevation -= channel * (elevation + 220f); // channels cut below sea level

            // Sea floods everything below zero; keep bathymetry shallow and bounded.
            return MathF.Max(elevation, -240f);
        }

        /// <summary>0 away from a channel centreline, 1 in the middle of one.</summary>
        static float ChannelMask(float u, float v, int seed)
        {
            float ridge = MathF.Abs(2f * ValueNoise(u, v, seed) - 1f);
            return Smoothstep(0.55f, 0.15f, ridge);
        }

        static float RidgedFbm(float x, float y, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, weight = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - MathF.Abs(2f * ValueNoise(x * frequency, y * frequency, seed + i) - 1f);
                n *= n * weight;
                weight = Math.Clamp(n * 2f, 0f, 1f);
                sum += n * amplitude;
                frequency *= 2.1f;
                amplitude *= 0.5f;
            }
            return Math.Clamp(sum, 0f, 1f);
        }

        static float ValueNoise(float x, float y, int seed)
        {
            int x0 = FloorToInt(x), y0 = FloorToInt(y);
            float tx = SmoothT(x - x0), ty = SmoothT(y - y0);
            float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
            float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374_761_393 + y * 668_265_263 + seed * 1_442_695_040);
                h = (h ^ (h >> 13)) * 1_274_126_177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static int FloorToInt(float v) => v >= 0f ? (int)v : (int)v - 1;
        static float SmoothT(float t) => t * t * (3f - 2f * t);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>
    /// Decorator that flattens a rectangular runway pad into any height source,
    /// with a smooth blend apron so the terrain meets the pad without cliffs.
    /// </summary>
    public sealed class RunwayPadHeightSource : IHeightSource
    {
        readonly IHeightSource _inner;
        readonly LocalNE _center;
        readonly float _sinHeading, _cosHeading;
        readonly float _halfLength, _halfWidth, _elevation, _blendMargin;

        /// <param name="headingDegrees">True heading of the runway centreline, degrees clockwise from north.</param>
        public RunwayPadHeightSource(
            IHeightSource inner, LocalNE center, float headingDegrees,
            float length, float width, float elevation, float blendMargin = 250f)
        {
            _inner = inner;
            _center = center;
            float rad = headingDegrees * MathF.PI / 180f;
            _sinHeading = MathF.Sin(rad);
            _cosHeading = MathF.Cos(rad);
            _halfLength = length / 2f;
            _halfWidth = width / 2f;
            _elevation = elevation;
            _blendMargin = blendMargin;
        }

        public float SampleElevation(float north, float east)
        {
            float dn = north - _center.North;
            float de = east - _center.East;
            // Along/across the runway axis.
            float along = MathF.Abs(dn * _cosHeading + de * _sinHeading);
            float across = MathF.Abs(-dn * _sinHeading + de * _cosHeading);

            float outsideAlong = along - _halfLength;
            float outsideAcross = across - _halfWidth;
            float outside = MathF.Max(MathF.Max(outsideAlong, outsideAcross), 0f);
            if (outside >= _blendMargin)
                return _inner.SampleElevation(north, east);

            float t = outside / _blendMargin;
            t = t * t * (3f - 2f * t);
            return _elevation + (_inner.SampleElevation(north, east) - _elevation) * t;
        }
    }
}
