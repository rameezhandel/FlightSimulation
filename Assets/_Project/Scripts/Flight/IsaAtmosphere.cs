using System;

namespace Cirrus.Flight
{
    /// <summary>Ambient air properties at a point. SI units throughout.</summary>
    public readonly struct AirState
    {
        public readonly float Density;      // kg/m^3
        public readonly float Pressure;     // Pa
        public readonly float Temperature;  // K
        public readonly float SpeedOfSound; // m/s

        public AirState(float density, float pressure, float temperature, float speedOfSound)
        {
            Density = density;
            Pressure = pressure;
            Temperature = temperature;
            SpeedOfSound = speedOfSound;
        }
    }

    /// <summary>
    /// International Standard Atmosphere, troposphere segment only.
    /// Constants per U.S. Standard Atmosphere 1976 / ICAO Doc 7488.
    /// The sim region (Southeast Alaska, GA aircraft) never leaves the troposphere,
    /// so altitude is clamped to [-500 m, 11 000 m].
    /// </summary>
    public static class IsaAtmosphere
    {
        public const float SeaLevelTemperature = 288.15f;   // K
        public const float SeaLevelPressure = 101_325f;     // Pa
        public const float SeaLevelDensity = 1.225f;        // kg/m^3
        public const float Gravity = 9.80665f;              // m/s^2

        const float LapseRate = 0.0065f;                    // K/m
        const float GasConstant = 287.05287f;               // J/(kg K), dry air
        const float HeatCapacityRatio = 1.4f;
        // g / (L * R): pressure exponent of the temperature ratio in the troposphere.
        const float PressureExponent = Gravity / (LapseRate * GasConstant);

        public static AirState AtAltitude(float altitudeMeters)
        {
            float h = Math.Clamp(altitudeMeters, -500f, 11_000f);
            float temperature = SeaLevelTemperature - LapseRate * h;
            float pressure = SeaLevelPressure * MathF.Pow(temperature / SeaLevelTemperature, PressureExponent);
            float density = pressure / (GasConstant * temperature);
            float speedOfSound = MathF.Sqrt(HeatCapacityRatio * GasConstant * temperature);
            return new AirState(density, pressure, temperature, speedOfSound);
        }
    }
}
