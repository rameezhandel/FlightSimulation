using System;
using Cirrus.Aircraft;
using Cirrus.CameraRigs;
using Cirrus.Flight;
using Cirrus.Navigation;
using Cirrus.Terrain;
using Cirrus.TerrainStreaming;
using UnityEngine;

namespace Cirrus.Core
{
    /// <summary>
    /// M2 test scene, procedural like M1Bootstrap: drop this one component into an
    /// empty scene and press Play. Streams synthetic Southeast-Alaska terrain (the
    /// real DEM tiles slot in once tools/terrain-pipeline has run), flattens the
    /// PAJN runway pad, runs the floating origin, and spawns the Beaver trimmed on
    /// an 8 km final for Juneau runway 08.
    ///
    /// Water is a placeholder plane for now — CLAUDE.md §5 demands a real water
    /// system for the fjords; that is scheduled in-editor work (docs/decisions/0003).
    /// </summary>
    public sealed class M2TerrainBootstrap : MonoBehaviour
    {
        const float SpawnAltitude = 600f;
        const float SpawnSpeedKnots = 105f;
        const float FinalDistance = 8000f;

        Transform? _water;
        Transform? _aircraft;

        void Start()
        {
            CreateSun();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.74f);
            RenderSettings.fogDensity = 0.00006f;

            // Spawn on final approach: back up from the PAJN threshold along the
            // runway heading, in region coordinates.
            Airport juneau = SoutheastAlaskaAirports.Juneau;
            LocalNE pajn = juneau.Local;
            float runwayHeading = juneau.Runways[0].TrueHeading;
            float heading = runwayHeading * Mathf.Deg2Rad;
            var spawnRegion = new LocalNE(
                pajn.North - FinalDistance * Mathf.Cos(heading),
                pajn.East - FinalDistance * Mathf.Sin(heading));

            var originObject = new GameObject("FloatingOrigin");
            var origin = originObject.AddComponent<FloatingOrigin>();

            FlightBody flight = CreateAircraft(origin, spawnRegion);
            _aircraft = flight.transform;
            origin.Initialize(spawnRegion, flight.transform);
            origin.Register(flight.transform);

            // Real DEM tiles when the pipeline has produced them, synthetic fjords
            // otherwise; runway pads flattened on top either way.
            IHeightSource source = RunwayTerrain.WithAllRegionPads(
                TerrainStreamer.CreateShippedSource(new SyntheticAlaskaHeightSource()));
            var streamer = originObject.AddComponent<TerrainStreamer>();
            streamer.Initialize(source, origin, flight.transform);
            flight.GroundElevationProvider = streamer.SampleElevation;

            CreateWater();
            CreateCameraAndOverlay(flight, origin);

            var weather = originObject.AddComponent<Cirrus.AtmosphereRuntime.WeatherController>();
            weather.Initialize(Cirrus.Atmosphere.WeatherPreset.Breeze, flight);
        }

        void LateUpdate()
        {
            if (_water == null || _aircraft == null) return;
            Vector3 p = _aircraft.position;
            _water.position = new Vector3(Mathf.Round(p.x / 100f) * 100f, 0f, Mathf.Round(p.z / 100f) * 100f);
        }

        static void CreateSun()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(38f, 150f, 0f); // low southern sun
            sun.intensity = 1.15f;
        }

        FlightBody CreateAircraft(FloatingOrigin origin, in LocalNE spawnRegion)
        {
            var root = new GameObject("Beaver");
            var body = root.AddComponent<Rigidbody>();
            FlightBody flight = root.AddComponent<FlightBody>();
            var input = root.AddComponent<FlightInputController>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            root.AddComponent<Cirrus.DebugTools.FlightRecorder>();
#endif
            M1Bootstrap.BuildPlaceholderVisuals(root.transform);
            M1Bootstrap.BuildContactColliders(root);

            float speed = SpawnSpeedKnots * MathUtil.KnotsToMetersPerSecond;
            TrimResult trim = TrimSolver.SolveLevelFlight(flight.Aircraft, speed, SpawnAltitude);

            // Face the runway: yaw to the runway heading, pitched at the trim attitude.
            var yaw = Quaternion.Euler(0f, SoutheastAlaskaAirports.Juneau.Runways[0].TrueHeading, 0f);
            Quaternion attitude = trim.Converged
                ? yaw * CoreFrame.ToUnity(trim.State.Orientation)
                : yaw;
            root.transform.SetPositionAndRotation(new Vector3(0f, SpawnAltitude, 0f), attitude);
            body.linearVelocity = attitude * new Vector3(0f, 0f, speed);
            if (trim.Converged)
            {
                flight.Controls = trim.Controls;
                input.SetInitial(trim.Controls.Throttle, trim.Controls.Flap);
            }
            return flight;
        }

        void CreateWater()
        {
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "WaterPlaceholder";
            Destroy(water.GetComponent<Collider>());
            water.transform.localScale = new Vector3(4000f, 1f, 4000f); // 40 km, follows the aircraft
            _water = water.transform;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = new Color(0.09f, 0.18f, 0.24f) };
            water.GetComponent<MeshRenderer>().material = material;
        }

        void CreateCameraAndOverlay(FlightBody flight, FloatingOrigin origin)
        {
            var cameraObject = new GameObject("ChaseCamera");
            var camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.farClipPlane = 80_000f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ChaseCamera>()
                .SetTarget(flight.transform, flight.GetComponent<Rigidbody>());
            origin.Register(cameraObject.transform);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            cameraObject.AddComponent<Cirrus.DebugTools.FlightDebugOverlay>()
                .SetTarget(flight, flight.GetComponent<Rigidbody>());
#endif
        }
    }
}
