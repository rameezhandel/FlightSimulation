using Cirrus.Aircraft;
using Cirrus.Core;
using UnityEngine;

namespace Cirrus.AtmosphereRuntime
{
    /// <summary>
    /// Feeds the wind field into the flight model every physics step. Rendering-side
    /// weather (clouds, visibility, precipitation) hooks onto the same preset in M3
    /// proper; the aerodynamic side ships first so wind matters from M2 on.
    /// </summary>
    public sealed class WeatherController : MonoBehaviour
    {
        Cirrus.Atmosphere.WindField? _wind;
        FlightBody? _flight;

        public void Initialize(in Cirrus.Atmosphere.WeatherPreset preset, FlightBody flight, int seed = 1)
        {
            _wind = new Cirrus.Atmosphere.WindField(preset, seed);
            _flight = flight;

            RenderSettings.fogDensity = 3f / Mathf.Max(preset.VisibilityMeters, 1000f);
        }

        void FixedUpdate()
        {
            if (_wind == null || _flight == null) return;
            // Position converted only for altitude + spatial variation; the wind
            // vector itself is core-frame, which is what FlightBody expects.
            UnityEngine.Vector3 p = _flight.transform.position;
            _flight.WindCore = _wind.Sample(CoreFrame.ToCore(p), Time.fixedTime);
        }
    }
}
