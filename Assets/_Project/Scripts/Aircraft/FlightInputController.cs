using Cirrus.Flight;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cirrus.Aircraft
{
    /// <summary>
    /// M1 developer input: direct Input System device reads, no .inputactions asset yet.
    /// The real touch control schemes (tilt / virtual stick / slider) are an M4 work
    /// item and will bring a proper action asset; this exists so the aircraft can be
    /// flown in-editor and with a gamepad.
    ///
    /// Keyboard: arrows or WASD = pitch/roll, Q/E = rudder, LeftShift/LeftCtrl =
    /// throttle up/down, F/V = flaps down/up one step.
    /// Gamepad: left stick = pitch/roll, right stick X = rudder, right stick Y =
    /// throttle rate, shoulder buttons = flaps.
    /// </summary>
    [RequireComponent(typeof(FlightBody))]
    public sealed class FlightInputController : MonoBehaviour
    {
        [SerializeField] private float _stickRatePerSecond = 4f;   // keyboard attack/decay rate
        [SerializeField] private float _throttleRatePerSecond = 0.5f;
        [SerializeField] private float _flapStep = 0.25f;

        FlightBody _flight = null!;
        float _pitch, _roll, _yaw, _throttle = 0.5f, _flap;

        void Awake() => _flight = GetComponent<FlightBody>();

        /// <summary>Seed persistent axes (from the spawn trim) so Update doesn't stomp them.</summary>
        public void SetInitial(float throttle, float flap)
        {
            _throttle = Mathf.Clamp01(throttle);
            _flap = Mathf.Clamp01(flap);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;

            float pitchTarget = 0f, rollTarget = 0f, yawTarget = 0f;

            if (keyboard != null)
            {
                // Up arrow / W = push (nose down) — flight-sim convention.
                pitchTarget += Axis(keyboard.sKey, keyboard.downArrowKey) - Axis(keyboard.wKey, keyboard.upArrowKey);
                rollTarget += Axis(keyboard.dKey, keyboard.rightArrowKey) - Axis(keyboard.aKey, keyboard.leftArrowKey);
                yawTarget += (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);

                if (keyboard.leftShiftKey.isPressed) _throttle += _throttleRatePerSecond * dt;
                if (keyboard.leftCtrlKey.isPressed) _throttle -= _throttleRatePerSecond * dt;
                if (keyboard.fKey.wasPressedThisFrame) _flap += _flapStep;
                if (keyboard.vKey.wasPressedThisFrame) _flap -= _flapStep;
            }

            if (gamepad != null)
            {
                Vector2 left = gamepad.leftStick.ReadValue();
                pitchTarget += -left.y; // stick pulled back (-y) = pull = +pitch input
                rollTarget += left.x;
                yawTarget += gamepad.rightStick.ReadValue().x;
                _throttle += gamepad.rightStick.ReadValue().y * _throttleRatePerSecond * dt;
                if (gamepad.rightShoulder.wasPressedThisFrame) _flap += _flapStep;
                if (gamepad.leftShoulder.wasPressedThisFrame) _flap -= _flapStep;
            }

            // Keyboard axes ramp toward the target so digital keys feel analog.
            _pitch = MoveToward(_pitch, Mathf.Clamp(pitchTarget, -1f, 1f), _stickRatePerSecond * dt);
            _roll = MoveToward(_roll, Mathf.Clamp(rollTarget, -1f, 1f), _stickRatePerSecond * dt);
            _yaw = MoveToward(_yaw, Mathf.Clamp(yawTarget, -1f, 1f), _stickRatePerSecond * dt);
            _throttle = Mathf.Clamp01(_throttle);
            _flap = Mathf.Clamp01(_flap);

            _flight.Controls = new ControlInputs
            {
                Pitch = _pitch,
                Roll = _roll,
                Yaw = _yaw,
                Throttle = _throttle,
                Flap = _flap,
            };
        }

        static float Axis(UnityEngine.InputSystem.Controls.KeyControl a, UnityEngine.InputSystem.Controls.KeyControl b)
            => (a.isPressed || b.isPressed) ? 1f : 0f;

        static float MoveToward(float current, float target, float maxDelta)
            => Mathf.MoveTowards(current, target, maxDelta);
    }
}
