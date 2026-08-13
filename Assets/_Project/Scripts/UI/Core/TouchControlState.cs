using System;

namespace Cirrus.Controls
{
    public enum TouchPhase
    {
        Began,
        Moved,
        Stationary,
        Ended,
    }

    /// <summary>One finger, as the pure layer sees it. Ids let us track a finger across frames.</summary>
    public readonly struct TouchSample
    {
        public readonly int Id;
        public readonly ScreenPoint Position;
        public readonly TouchPhase Phase;

        public TouchSample(int id, in ScreenPoint position, TouchPhase phase)
        {
            Id = id;
            Position = position;
            Phase = phase;
        }

        public bool IsActive => Phase != TouchPhase.Ended;
    }

    /// <summary>What the controls are asking the aircraft to do this frame.</summary>
    public readonly struct ControlDemand
    {
        public readonly float Roll;
        public readonly float Pitch;
        public readonly float Rudder;
        public readonly float Throttle;
        public readonly float Flap;

        public ControlDemand(float roll, float pitch, float rudder, float throttle, float flap)
        {
            Roll = roll;
            Pitch = pitch;
            Rudder = rudder;
            Throttle = throttle;
            Flap = flap;
        }
    }

    /// <summary>
    /// The touch controls as a pure state machine: feed it touches and a frame
    /// time, get back control demands. No Unity, so the awkward parts — finger
    /// tracking across frames, a second finger arriving mid-drag, a touch ending
    /// while the aircraft is in a turn — are unit-testable instead of being
    /// discovered on a phone at 2am.
    ///
    /// Fingers are claimed by whichever control they land on and stay claimed until
    /// they lift, so dragging the throttle never steals the stick and vice versa.
    /// </summary>
    public sealed class TouchControlState
    {
        public TouchControlScheme Scheme = TouchControlScheme.VirtualStick;
        public VirtualStickModel Stick = VirtualStickModel.Default();
        public TiltModel Tilt = TiltModel.Default();
        public TurnCoordinator Coordinator = TurnCoordinator.Default();

        /// <summary>Flap steps per press, and how fast the throttle key-repeats.</summary>
        public float FlapStep = 0.25f;
        public float RudderRatePerSecond = 3f;

        TouchLayout _layout;

        int _stickFinger = -1;
        ScreenPoint _stickOrigin;
        ScreenPoint _stickCurrent;

        int _throttleFinger = -1;
        ScreenPoint _throttleGrab;
        float _throttleAtGrab;

        int _rudderLeftFinger = -1;
        int _rudderRightFinger = -1;

        float _throttle = 0.5f;
        float _flap;
        float _rudder;
        bool _calibrateRequested;

        public TouchLayout Layout => _layout;
        public bool StickActive => _stickFinger >= 0;
        public ScreenPoint StickOrigin => _stickOrigin;
        public ScreenPoint StickKnob => _stickOrigin + Stick.KnobOffset(_stickOrigin, _stickCurrent);
        public float Throttle => _throttle;
        public float Flap => _flap;

        /// <summary>True once, after the player taps calibrate; the caller supplies gravity and clears it.</summary>
        public bool ConsumeCalibrationRequest()
        {
            bool requested = _calibrateRequested;
            _calibrateRequested = false;
            return requested;
        }

        public void SetLayout(in LayoutRect safeArea) => _layout = new TouchLayout(safeArea);

        public void SetInitial(float throttle, float flap)
        {
            _throttle = Math.Clamp(throttle, 0f, 1f);
            _flap = Math.Clamp(flap, 0f, 1f);
        }

        /// <param name="touches">All touches this frame.</param>
        /// <param name="gravity">Device gravity, only read in the Tilt scheme.</param>
        /// <param name="sideslipRadians">Measured sideslip, for the turn coordinator.</param>
        /// <param name="deltaTime">Seconds since the last update.</param>
        public ControlDemand Update(
            ReadOnlySpan<TouchSample> touches,
            in GravityVector gravity,
            float sideslipRadians,
            float deltaTime)
        {
            ReleaseLiftedFingers(touches);
            ClaimNewFingers(touches);
            TrackHeldFingers(touches);

            float roll = 0f, pitch = 0f;
            if (Scheme == TouchControlScheme.Tilt)
            {
                (roll, pitch) = Tilt.Evaluate(gravity);
            }
            else if (_stickFinger >= 0)
            {
                (roll, pitch) = Stick.Evaluate(_stickOrigin, _stickCurrent);
            }

            // Rudder buttons ramp rather than snap, so a tap is a nudge.
            float rudderTarget = 0f;
            if (_rudderLeftFinger >= 0) rudderTarget -= 1f;
            if (_rudderRightFinger >= 0) rudderTarget += 1f;
            _rudder = ControlResponse.MoveToward(_rudder, rudderTarget, RudderRatePerSecond * deltaTime);

            float rudder = Coordinator.Rudder(roll, sideslipRadians, _rudder);
            return new ControlDemand(roll, pitch, rudder, _throttle, _flap);
        }

        void ReleaseLiftedFingers(ReadOnlySpan<TouchSample> touches)
        {
            if (_stickFinger >= 0 && !IsStillDown(touches, _stickFinger)) _stickFinger = -1;
            if (_throttleFinger >= 0 && !IsStillDown(touches, _throttleFinger)) _throttleFinger = -1;
            if (_rudderLeftFinger >= 0 && !IsStillDown(touches, _rudderLeftFinger)) _rudderLeftFinger = -1;
            if (_rudderRightFinger >= 0 && !IsStillDown(touches, _rudderRightFinger)) _rudderRightFinger = -1;
        }

        void ClaimNewFingers(ReadOnlySpan<TouchSample> touches)
        {
            foreach (TouchSample touch in touches)
            {
                if (touch.Phase != TouchPhase.Began || IsClaimed(touch.Id))
                    continue;

                if (_layout.ThrottleTrack.Contains(touch.Position) && _throttleFinger < 0)
                {
                    _throttleFinger = touch.Id;
                    _throttleGrab = touch.Position;
                    _throttleAtGrab = _throttle;
                }
                else if (_layout.FlapDownButton.Contains(touch.Position))
                {
                    _flap = Math.Clamp(_flap + FlapStep, 0f, 1f);
                }
                else if (_layout.FlapUpButton.Contains(touch.Position))
                {
                    _flap = Math.Clamp(_flap - FlapStep, 0f, 1f);
                }
                else if (_layout.RudderLeftButton.Contains(touch.Position) && _rudderLeftFinger < 0)
                {
                    _rudderLeftFinger = touch.Id;
                }
                else if (_layout.RudderRightButton.Contains(touch.Position) && _rudderRightFinger < 0)
                {
                    _rudderRightFinger = touch.Id;
                }
                else if (Scheme == TouchControlScheme.Tilt && _layout.CalibrateButton.Contains(touch.Position))
                {
                    _calibrateRequested = true;
                }
                else if (Scheme != TouchControlScheme.Tilt
                         && _layout.StickZone.Contains(touch.Position)
                         && _stickFinger < 0)
                {
                    // Floating stick: it centres wherever the thumb landed.
                    _stickFinger = touch.Id;
                    _stickOrigin = touch.Position;
                    _stickCurrent = touch.Position;
                }
            }
        }

        void TrackHeldFingers(ReadOnlySpan<TouchSample> touches)
        {
            foreach (TouchSample touch in touches)
            {
                if (touch.Id == _stickFinger)
                    _stickCurrent = touch.Position;
                else if (touch.Id == _throttleFinger)
                    _throttle = new ThrottleSliderModel(_layout.ThrottleTrack)
                        .Relative(_throttleAtGrab, _throttleGrab, touch.Position);
            }
        }

        bool IsClaimed(int id)
            => id == _stickFinger || id == _throttleFinger
               || id == _rudderLeftFinger || id == _rudderRightFinger;

        static bool IsStillDown(ReadOnlySpan<TouchSample> touches, int id)
        {
            foreach (TouchSample touch in touches)
                if (touch.Id == id)
                    return touch.IsActive;
            return false;
        }
    }
}
