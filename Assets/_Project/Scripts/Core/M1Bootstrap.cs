using Cirrus.Aircraft;
using Cirrus.CameraRigs;
using Cirrus.Flight;
using UnityEngine;

namespace Cirrus.Core
{
    /// <summary>
    /// M1 test-flight scene, built procedurally: drop this single component into an
    /// empty scene and press Play. Creates the flat ground plane, sun, chase camera,
    /// dev overlay, and a primitive-placeholder Beaver spawned at 300 m already
    /// trimmed for level flight at 105 kt (via the core TrimSolver, so the hands-off
    /// validation behaviour is reproduced in-engine).
    ///
    /// Procedural on purpose: no scene/prefab assets to keep in sync while the
    /// project shell is young. Real scenes arrive with M2 terrain.
    /// </summary>
    public sealed class M1Bootstrap : MonoBehaviour
    {
        const float SpawnAltitude = 300f;
        const float SpawnSpeedKnots = 105f;

        Transform _ground = null!;
        Transform _aircraft = null!;

        void Start()
        {
            CreateSun();
            FlightBody flight = CreateAircraft();
            CreateGround();
            CreateCameraAndOverlay(flight);
        }

        void LateUpdate()
        {
            // Fake the infinite plane: keep the ground centred under the aircraft.
            Vector3 p = _aircraft.position;
            _ground.position = new Vector3(Mathf.Round(p.x / 10f) * 10f, 0f, Mathf.Round(p.z / 10f) * 10f);
        }

        static void CreateSun()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            sun.intensity = 1.2f;
        }

        FlightBody CreateAircraft()
        {
            var root = new GameObject("Beaver");
            _aircraft = root.transform;

            var body = root.AddComponent<Rigidbody>();
            FlightBody flight = root.AddComponent<FlightBody>(); // configures the Rigidbody in Awake
            var input = root.AddComponent<FlightInputController>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            root.AddComponent<Cirrus.DebugTools.FlightRecorder>(); // press R to dump the last 5 min
#endif

            BuildPlaceholderVisuals(root.transform);
            BuildContactColliders(root);

            // Spawn trimmed, straight and level, mid-air.
            float speed = SpawnSpeedKnots * MathUtil.KnotsToMetersPerSecond;
            TrimResult trim = TrimSolver.SolveLevelFlight(flight.Aircraft, speed, SpawnAltitude);
            root.transform.SetPositionAndRotation(
                new Vector3(0f, SpawnAltitude, 0f),
                trim.Converged
                    ? CoreFrame.ToUnity(trim.State.Orientation)
                    : Quaternion.identity);
            body.linearVelocity = CoreFrame.ToUnity(new System.Numerics.Vector3(speed, 0f, 0f));
            if (trim.Converged)
            {
                flight.Controls = trim.Controls;
                input.SetInitial(trim.Controls.Throttle, trim.Controls.Flap);
            }
            return flight;
        }

        public static void BuildPlaceholderVisuals(Transform parent)
        {
            // Proportions eyeballed from the DHC-2 3-view; purely a visual stand-in.
            AddPart(parent, PrimitiveType.Capsule, new Vector3(0f, 0f, 0.3f), new Vector3(1.4f, 1.4f, 8.5f), rotateCapsule: true);
            AddPart(parent, PrimitiveType.Cube, new Vector3(0f, 1.1f, 0.24f), new Vector3(14.6f, 0.18f, 1.6f));
            AddPart(parent, PrimitiveType.Cube, new Vector3(0f, 0.5f, -5.2f), new Vector3(4.7f, 0.15f, 1.0f));
            AddPart(parent, PrimitiveType.Cube, new Vector3(0f, 1.0f, -5.0f), new Vector3(0.12f, 1.8f, 1.25f));
        }

        static void AddPart(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, bool rotateCapsule = false)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>()); // visuals only; contact colliders are explicit
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (rotateCapsule) part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        public static void BuildContactColliders(GameObject root)
        {
            var material = new PhysicsMaterial("Skid") { dynamicFriction = 0.5f, staticFriction = 0.6f };
            // Crude tricycle of skids so the ground stops us; real gear/floats later in M1.
            AddSkid(root, new Vector3(0f, -1.0f, 2.5f), material);
            AddSkid(root, new Vector3(-1.2f, -1.0f, -0.8f), material);
            AddSkid(root, new Vector3(1.2f, -1.0f, -0.8f), material);
            var fuselage = root.AddComponent<BoxCollider>();
            fuselage.center = new Vector3(0f, 0.1f, 0.3f);
            fuselage.size = new Vector3(1.3f, 1.3f, 8.0f);
            fuselage.material = material;
        }

        static void AddSkid(GameObject root, Vector3 center, PhysicsMaterial material)
        {
            var skid = root.AddComponent<SphereCollider>();
            skid.center = center;
            skid.radius = 0.25f;
            skid.material = material;
        }

        void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(600f, 1f, 600f); // 6 km x 6 km, recentred every frame
            _ground = ground.transform;

            // Checkerboard so speed and height read visually on a featureless plane.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            texture.SetPixels(new[]
            {
                new Color(0.24f, 0.34f, 0.22f), new Color(0.30f, 0.42f, 0.27f),
                new Color(0.30f, 0.42f, 0.27f), new Color(0.24f, 0.34f, 0.22f),
            });
            texture.Apply();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { mainTexture = texture };
            material.mainTextureScale = new Vector2(300f, 300f); // 20 m squares at this plane scale
            ground.GetComponent<MeshRenderer>().material = material;
        }

        void CreateCameraAndOverlay(FlightBody flight)
        {
            var cameraObject = new GameObject("ChaseCamera");
            cameraObject.AddComponent<UnityEngine.Camera>().farClipPlane = 20_000f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ChaseCamera>()
                .SetTarget(flight.transform, flight.GetComponent<Rigidbody>());

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            cameraObject.AddComponent<Cirrus.DebugTools.FlightDebugOverlay>()
                .SetTarget(flight, flight.GetComponent<Rigidbody>());
#endif
        }
    }
}
