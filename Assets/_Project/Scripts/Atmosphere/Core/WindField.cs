using System;
using System.Numerics;

namespace Cirrus.Atmosphere
{
    /// <summary>
    /// A weather situation, data-only. Becomes a ScriptableObject-backed preset
    /// catalogue in M3 proper; the fields the wind model needs are here now so the
    /// flight model feels weather early (CLAUDE.md: "weather that actually matters").
    /// </summary>
    public struct WeatherPreset
    {
        public string Name;
        public float SurfaceWindSpeed;      // m/s at 10 m reference height
        public float SurfaceWindDirection;  // deg true, met convention: direction the wind blows FROM
        public float GustIntensity;         // m/s, slow (tens of seconds) speed swings
        public float TurbulenceIntensity;   // m/s, fast bumpiness
        public float VisibilityMeters;      // fog hookup (M3 rendering)
        public float CloudBaseMeters;       // cloud hookup (M3 rendering)

        public static WeatherPreset Calm => new WeatherPreset
        {
            Name = "Calm",
            SurfaceWindSpeed = 0f,
            SurfaceWindDirection = 0f,
            GustIntensity = 0f,
            TurbulenceIntensity = 0f,
            VisibilityMeters = 60_000f,
            CloudBaseMeters = 3000f,
        };

        public static WeatherPreset Breeze => new WeatherPreset
        {
            Name = "Breeze",
            SurfaceWindSpeed = 5f,          // ~10 kt
            SurfaceWindDirection = 250f,    // prevailing SW'ly in the channels [EST]
            GustIntensity = 1.5f,
            TurbulenceIntensity = 0.6f,
            VisibilityMeters = 40_000f,
            CloudBaseMeters = 1500f,
        };

        public static WeatherPreset Storm => new WeatherPreset
        {
            Name = "Storm",
            SurfaceWindSpeed = 13f,         // ~25 kt
            SurfaceWindDirection = 140f,    // SE'ly — the direction bad weather arrives from [EST]
            GustIntensity = 6f,
            TurbulenceIntensity = 2.5f,
            VisibilityMeters = 5000f,
            CloudBaseMeters = 400f,
        };
    }

    /// <summary>
    /// Deterministic wind sampler, core world frame (X north, Y east, Z up):
    ///   steady : met-convention surface wind with a power-law boundary-layer
    ///            profile, V(h) = V10 * (h/10)^0.14, capped at 1.8x (Hellmann
    ///            exponent for open terrain).
    ///   gusts  : slow value-noise modulation of speed and direction.
    ///   bumps  : three octaves of spatial+temporal value noise, incl. vertical.
    /// Pure and allocation-free: safe from FixedUpdate at 200 Hz. Same seed, same
    /// weather, always — required for the flight recorder's deterministic replay
    /// (wind is recorded per frame, so replays are exact regardless).
    /// </summary>
    public sealed class WindField
    {
        readonly WeatherPreset _preset;
        readonly int _seed;
        readonly float _directionRad;

        public WindField(in WeatherPreset preset, int seed = 1)
        {
            _preset = preset;
            _seed = seed;
            _directionRad = preset.SurfaceWindDirection * (MathF.PI / 180f);
        }

        public WeatherPreset Preset => _preset;

        /// <param name="position">Core world frame; Z is height above sea level.</param>
        /// <param name="time">Simulation time, s.</param>
        public Vector3 Sample(in Vector3 position, float time)
        {
            if (_preset.SurfaceWindSpeed <= 0f && _preset.GustIntensity <= 0f && _preset.TurbulenceIntensity <= 0f)
                return Vector3.Zero;

            // Boundary-layer profile.
            float h = MathF.Max(position.Z, 2f);
            float profile = MathF.Min(MathF.Pow(h / 10f, 0.14f), 1.8f);

            // Gusts: speed surges and a few degrees of direction wander, tens of
            // seconds — both scale with gust intensity, so a steady preset is steady.
            float gust = _preset.GustIntensity * (Noise1(time * 0.07f, _seed) * 2f - 1f);
            float wander = 0.03f * _preset.GustIntensity * (Noise1(time * 0.045f, _seed + 3) * 2f - 1f); // rad
            float speed = MathF.Max(0f, _preset.SurfaceWindSpeed * profile + gust);
            float direction = _directionRad + wander;

            // Met convention: wind FROM `direction` blows TOWARD direction + 180.
            float sin = MathF.Sin(direction), cos = MathF.Cos(direction);
            var wind = new Vector3(-cos * speed, -sin * speed, 0f);

            // Turbulence: spatial-temporal, three octaves, vertical included.
            float t = _preset.TurbulenceIntensity;
            if (t > 0f)
            {
                float px = position.X * 0.01f, py = position.Y * 0.01f;
                wind += t * new Vector3(
                    Octaves(px, py + time * 0.9f, _seed + 11),
                    Octaves(px + time * 0.9f, py, _seed + 23),
                    0.7f * Octaves(px - time * 0.9f, py + time * 0.9f, _seed + 37));
            }
            return wind;
        }

        static float Octaves(float x, float y, int seed)
        {
            float sum = 0f, amplitude = 0.55f, frequency = 1f;
            for (int i = 0; i < 3; i++)
            {
                sum += amplitude * (Noise2(x * frequency, y * frequency, seed + i) * 2f - 1f);
                frequency *= 2.3f;
                amplitude *= 0.5f;
            }
            return sum;
        }

        static float Noise1(float t, int seed) => Noise2(t, 0.137f, seed);

        static float Noise2(float x, float y, int seed)
        {
            int x0 = x >= 0f ? (int)x : (int)x - 1;
            int y0 = y >= 0f ? (int)y : (int)y - 1;
            float tx = Smooth(x - x0), ty = Smooth(y - y0);
            float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
            float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
            return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
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

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
