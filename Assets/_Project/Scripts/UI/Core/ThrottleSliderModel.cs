using System;

namespace Cirrus.Controls
{
    /// <summary>
    /// Vertical slider for throttle (and reusable for flap). Supports both grabbing
    /// styles because they suit different moments:
    ///
    ///   relative — grab the handle anywhere and drag; the value moves with the
    ///              thumb. No jump, so it is safe to touch mid-flight.
    ///   absolute — tap anywhere on the track to jump there. Fast for "full power
    ///              now", dangerous for accidental brushes.
    ///
    /// The controls use relative dragging; absolute exists for a future tap-to-set
    /// affordance and is tested so it cannot rot.
    /// </summary>
    public struct ThrottleSliderModel
    {
        public LayoutRect Track;

        public ThrottleSliderModel(in LayoutRect track) => Track = track;

        /// <summary>Value 0..1 for a touch at an absolute position on the track.</summary>
        public float Absolute(in ScreenPoint touch)
        {
            if (Track.Height <= 0f)
                return 0f;
            return Math.Clamp((touch.Y - Track.Y) / Track.Height, 0f, 1f);
        }

        /// <summary>Value after dragging from where the touch began.</summary>
        public float Relative(float valueAtGrab, in ScreenPoint grab, in ScreenPoint current)
        {
            if (Track.Height <= 0f)
                return valueAtGrab;
            float delta = (current.Y - grab.Y) / Track.Height;
            return Math.Clamp(valueAtGrab + delta, 0f, 1f);
        }

        /// <summary>Screen position of the handle centre for a value.</summary>
        public ScreenPoint HandlePosition(float value)
            => new ScreenPoint(
                Track.X + Track.Width * 0.5f,
                Track.Y + Track.Height * Math.Clamp(value, 0f, 1f));
    }
}
