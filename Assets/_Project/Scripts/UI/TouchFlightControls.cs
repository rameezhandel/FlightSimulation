using System;
using System.Collections.Generic;
using Cirrus.Aircraft;
using Cirrus.Controls;
using Cirrus.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using TouchPhaseUnity = UnityEngine.InputSystem.TouchPhase;

namespace Cirrus.UI
{
    /// <summary>
    /// On-screen flight controls: reads the touchscreen (and the gravity sensor in
    /// tilt mode), runs the pure TouchControlState, and writes the result into
    /// FlightBody.
    ///
    /// The visuals are built procedurally into a Canvas — no prefabs to keep in
    /// sync while the project has no committed scenes, and unlike IMGUI a Canvas
    /// costs nothing per frame beyond a couple of RectTransform writes, which
    /// matters given §6's zero-per-frame-allocation rule. Two small textures are
    /// generated once at startup.
    ///
    /// This is the first slice of M4's control work, pulled forward because a
    /// device test you cannot steer is not a test.
    /// </summary>
    [RequireComponent(typeof(FlightBody))]
    public sealed class TouchFlightControls : MonoBehaviour
    {
        [SerializeField] private TouchControlScheme _scheme = TouchControlScheme.VirtualStick;
        [SerializeField] private bool _showRudderPedals = true;
        [SerializeField] private bool _autoCoordinate = true;

        public TouchControlState State { get; } = new TouchControlState();

        FlightBody _flight = null!;
        FlightInputController? _keyboardInput;
        readonly List<TouchSample> _touches = new List<TouchSample>(8);
        TouchSample[] _touchBuffer = new TouchSample[8];

        RectTransform? _stickBase;
        RectTransform? _stickKnob;
        RectTransform? _throttleHandle;
        Canvas? _canvas;
        Rect _lastSafeArea;

        void Awake()
        {
            _flight = GetComponent<FlightBody>();
            _keyboardInput = GetComponent<FlightInputController>();
            State.Scheme = _scheme;
            State.Coordinator.Enabled = _autoCoordinate;
            State.SetInitial(_flight.Controls.Throttle, _flight.Controls.Flap);
            EnhancedTouchSupport.Enable();

            if (_scheme == TouchControlScheme.Tilt && GravitySensor.current != null)
                InputSystem.EnableDevice(GravitySensor.current);

            BuildUi();
            ApplySafeArea();
        }

        void OnDestroy() => EnhancedTouchSupport.Disable();

        void Update()
        {
            ApplySafeArea();
            CollectTouches();

            GravityVector gravity = ReadGravity();
            if (State.ConsumeCalibrationRequest())
                State.Tilt.Calibrate(gravity);

            float sideslip = MeasureSideslip();
            int count = _touches.Count;
            if (_touchBuffer.Length < count)
                _touchBuffer = new TouchSample[Mathf.NextPowerOfTwo(count)];
            _touches.CopyTo(_touchBuffer);

            ControlDemand demand = State.Update(
                new ReadOnlySpan<TouchSample>(_touchBuffer, 0, count),
                gravity, sideslip, Time.deltaTime);

            // Hand back to the keyboard only when nothing is touching the glass and
            // tilt is not flying the aircraft — otherwise the editor keeps working
            // and the device is never fought over.
            bool touchInUse = count > 0 || _scheme == TouchControlScheme.Tilt;
            if (_keyboardInput != null)
                _keyboardInput.Suppressed = touchInUse;
            if (!touchInUse && _keyboardInput != null)
            {
                State.SetInitial(_flight.Controls.Throttle, _flight.Controls.Flap);
                UpdateVisuals();
                return;
            }

            _flight.Controls = new ControlInputs
            {
                Pitch = demand.Pitch,
                Roll = demand.Roll,
                Yaw = demand.Rudder,
                Throttle = demand.Throttle,
                Flap = demand.Flap,
            };

            // Keep the keyboard path's persistent axes in step, so lifting a finger
            // off the throttle does not hand control back to a stale value.
            _keyboardInput?.SetInitial(demand.Throttle, demand.Flap);

            UpdateVisuals();
        }

        /// <summary>
        /// Sideslip angle from the aircraft's own body-frame airflow: positive when
        /// the relative wind comes from the right, which is what the coordinator
        /// expects.
        /// </summary>
        float MeasureSideslip()
        {
            if (!_autoCoordinate || _flight.TrueAirspeed < 5f)
                return 0f;
            System.Numerics.Vector3 airRelative = MathUtil.WorldToBody(
                Cirrus.Core.CoreFrame.ToCore(transform.rotation),
                Cirrus.Core.CoreFrame.ToCore(GetComponent<Rigidbody>().linearVelocity) - _flight.WindCore);
            return MathF.Atan2(airRelative.Y, MathF.Max(1f, airRelative.X));
        }

        void CollectTouches()
        {
            _touches.Clear();
            foreach (Touch touch in Touch.activeTouches)
            {
                Controls.TouchPhase phase = touch.phase switch
                {
                    TouchPhaseUnity.Began => Controls.TouchPhase.Began,
                    TouchPhaseUnity.Moved => Controls.TouchPhase.Moved,
                    TouchPhaseUnity.Stationary => Controls.TouchPhase.Stationary,
                    _ => Controls.TouchPhase.Ended,
                };
                _touches.Add(new TouchSample(
                    touch.touchId,
                    new ScreenPoint(touch.screenPosition.x, touch.screenPosition.y),
                    phase));
            }
        }

        GravityVector ReadGravity()
        {
            GravitySensor? sensor = GravitySensor.current;
            if (sensor == null)
                return new GravityVector(0f, -1f, 0f);
            UnityEngine.Vector3 g = sensor.gravity.ReadValue().normalized;
            // Sensor axes are device-fixed; the pure model wants screen axes.
            return new GravityVector(g.x, g.y, g.z);
        }

        void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe == _lastSafeArea)
                return;
            _lastSafeArea = safe;
            State.SetLayout(new LayoutRect(safe.x, safe.y, safe.width, safe.height));
            PositionStaticElements();
        }

        // ---------------------------------------------------------------- visuals

        void BuildUi()
        {
            var canvasObject = new GameObject("TouchControls");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            Sprite disc = CreateDiscSprite(128);
            Sprite panel = CreateRectSprite();

            if (_scheme != TouchControlScheme.Tilt)
            {
                _stickBase = CreateImage("StickBase", disc, new Color(1f, 1f, 1f, 0.14f));
                _stickKnob = CreateImage("StickKnob", disc, new Color(1f, 1f, 1f, 0.32f));
            }
            CreateImage("ThrottleTrack", panel, new Color(1f, 1f, 1f, 0.12f));
            _throttleHandle = CreateImage("ThrottleHandle", panel, new Color(1f, 1f, 1f, 0.38f));
            CreateImage("FlapDown", panel, new Color(0.4f, 0.8f, 1f, 0.24f));
            CreateImage("FlapUp", panel, new Color(0.4f, 0.8f, 1f, 0.24f));
            if (_showRudderPedals)
            {
                CreateImage("RudderLeft", panel, new Color(1f, 0.85f, 0.4f, 0.20f));
                CreateImage("RudderRight", panel, new Color(1f, 0.85f, 0.4f, 0.20f));
            }
            if (_scheme == TouchControlScheme.Tilt)
                CreateImage("Calibrate", panel, new Color(0.6f, 1f, 0.6f, 0.28f));
        }

        RectTransform CreateImage(string name, Sprite sprite, Color color)
        {
            var imageObject = new GameObject(name);
            imageObject.transform.SetParent(_canvas!.transform, false);
            var image = imageObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false; // input comes from the touch layer, not the UI system
            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = UnityEngine.Vector2.zero;
            rect.anchorMax = UnityEngine.Vector2.zero;
            rect.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            return rect;
        }

        void PositionStaticElements()
        {
            if (_canvas == null) return;
            TouchLayout layout = State.Layout;
            Place("ThrottleTrack", layout.ThrottleTrack);
            Place("FlapDown", layout.FlapDownButton);
            Place("FlapUp", layout.FlapUpButton);
            if (_showRudderPedals)
            {
                Place("RudderLeft", layout.RudderLeftButton);
                Place("RudderRight", layout.RudderRightButton);
            }
            if (_scheme == TouchControlScheme.Tilt)
                Place("Calibrate", layout.CalibrateButton);

            if (_throttleHandle != null)
            {
                _throttleHandle.sizeDelta = new UnityEngine.Vector2(
                    layout.ThrottleTrack.Width * 1.8f, layout.Scale * 0.5f);
            }
            if (_stickBase != null)
                _stickBase.sizeDelta = UnityEngine.Vector2.one * (State.Stick.Radius * 2f);
            if (_stickKnob != null)
                _stickKnob.sizeDelta = UnityEngine.Vector2.one * (State.Stick.Radius * 0.7f);
        }

        void Place(string name, in LayoutRect rect)
        {
            Transform? child = _canvas!.transform.Find(name);
            if (child == null) return;
            var transformRect = (RectTransform)child;
            transformRect.sizeDelta = new UnityEngine.Vector2(rect.Width, rect.Height);
            ScreenPoint centre = rect.Center;
            transformRect.anchoredPosition = new UnityEngine.Vector2(centre.X, centre.Y);
        }

        void UpdateVisuals()
        {
            TouchLayout layout = State.Layout;

            if (_stickBase != null && _stickKnob != null)
            {
                bool active = State.StickActive;
                _stickBase.gameObject.SetActive(active);
                _stickKnob.gameObject.SetActive(active);
                if (active)
                {
                    _stickBase.anchoredPosition = new UnityEngine.Vector2(State.StickOrigin.X, State.StickOrigin.Y);
                    _stickKnob.anchoredPosition = new UnityEngine.Vector2(State.StickKnob.X, State.StickKnob.Y);
                }
            }

            if (_throttleHandle != null)
            {
                ScreenPoint handle = new ThrottleSliderModel(layout.ThrottleTrack).HandlePosition(State.Throttle);
                _throttleHandle.anchoredPosition = new UnityEngine.Vector2(handle.X, handle.Y);
            }
        }

        static Sprite CreateDiscSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius + 0.5f, dy = y - radius + 0.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    // Soft edge, and a ring so the base reads as an outline.
                    float alpha = Mathf.Clamp01(radius - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new UnityEngine.Vector2(0.5f, 0.5f));
        }

        static Sprite CreateRectSprite()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 4, 4), new UnityEngine.Vector2(0.5f, 0.5f));
        }
    }
}
