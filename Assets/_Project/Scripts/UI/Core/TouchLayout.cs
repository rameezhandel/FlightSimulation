using System;

namespace Cirrus.Controls
{
    public enum TouchControlScheme
    {
        /// <summary>Floating thumb stick, left side. The default: precise and unambiguous.</summary>
        VirtualStick,
        /// <summary>Device tilt for roll and pitch; the screen stays clear.</summary>
        Tilt,
        /// <summary>Separate horizontal roll slider and vertical pitch slider.</summary>
        Sliders,
    }

    /// <summary>
    /// Where the on-screen controls live, in screen pixels.
    ///
    /// Built from the SAFE AREA, not the full screen: on a notched iPhone the home
    /// indicator sits exactly where a thumb wants to rest, and a throttle under the
    /// notch is a throttle you cannot reach. Everything scales with the shorter
    /// screen dimension so an iPad does not get iPhone-sized targets.
    /// </summary>
    public readonly struct TouchLayout
    {
        public readonly LayoutRect StickZone;      // where a thumb may plant the stick
        public readonly LayoutRect ThrottleTrack;
        public readonly LayoutRect FlapDownButton;
        public readonly LayoutRect FlapUpButton;
        public readonly LayoutRect RudderLeftButton;
        public readonly LayoutRect RudderRightButton;
        public readonly LayoutRect CalibrateButton; // tilt scheme only
        public readonly float Scale;                // px per layout unit

        /// <summary>Apple's 44pt minimum touch target, in layout units.</summary>
        public const float MinimumTouchTarget = 44f;

        public TouchLayout(in LayoutRect safeArea)
        {
            // One "unit" is 1/10 of the short edge: thumbs scale with the device,
            // not with the pixel count.
            float shortEdge = MathF.Min(safeArea.Width, safeArea.Height);
            float unit = shortEdge * 0.1f;
            Scale = unit;

            float margin = unit * 0.35f;
            float buttonSize = MathF.Max(unit * 1.1f, MinimumTouchTarget);
            float pedalHeight = buttonSize * 0.8f;

            // The bottom strip belongs to the rudder pedals. Everything else keeps
            // clear of it: a thumb reaching for the stick must never be able to land
            // on a pedal instead, which is exactly the sort of ambiguity that makes
            // a touch control feel broken rather than merely imprecise.
            float pedalBand = margin + pedalHeight + margin;

            // Left side, above the pedals: the stick may be planted anywhere here.
            StickZone = new LayoutRect(
                safeArea.X,
                safeArea.Y + pedalBand,
                safeArea.Width * 0.38f,
                safeArea.Height * 0.55f);

            // Right edge: vertical throttle, thumb-reachable.
            float throttleWidth = unit * 0.9f;
            ThrottleTrack = new LayoutRect(
                safeArea.MaxX - margin - throttleWidth,
                safeArea.Y + margin + buttonSize * 2.2f,
                throttleWidth,
                safeArea.Height * 0.5f);

            // Flap buttons stack below the throttle, in the bottom-right corner.
            FlapUpButton = new LayoutRect(
                safeArea.MaxX - margin - buttonSize,
                safeArea.Y + margin + buttonSize * 1.1f,
                buttonSize,
                buttonSize);
            FlapDownButton = new LayoutRect(
                safeArea.MaxX - margin - buttonSize,
                safeArea.Y + margin,
                buttonSize,
                buttonSize);

            // Rudder pedals, bottom centre: reachable by either thumb, in the band
            // reserved for them above.
            float pedalWidth = buttonSize * 1.3f;
            float pedalCentre = safeArea.X + safeArea.Width * 0.5f;
            RudderLeftButton = new LayoutRect(
                pedalCentre - pedalWidth - margin * 0.5f,
                safeArea.Y + margin,
                pedalWidth,
                pedalHeight);
            RudderRightButton = new LayoutRect(
                pedalCentre + margin * 0.5f,
                safeArea.Y + margin,
                pedalWidth,
                pedalHeight);

            // Top-left, away from everything: recalibrate the tilt neutral.
            CalibrateButton = new LayoutRect(
                safeArea.X + margin,
                safeArea.MaxY - margin - buttonSize,
                buttonSize,
                buttonSize);
        }
    }
}
