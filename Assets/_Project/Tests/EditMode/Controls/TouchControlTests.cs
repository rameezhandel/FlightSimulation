using System;
using Cirrus.Controls;
using NUnit.Framework;

namespace Cirrus.Tests.Controls
{
    [TestFixture]
    public class ControlResponseTests
    {
        [Test]
        public void DeadZoneSuppressesSmallInputs()
        {
            Assert.AreEqual(0f, ControlResponse.Apply(0.05f, 0.1f, 0f), 1e-6f);
            Assert.AreEqual(0f, ControlResponse.Apply(-0.05f, 0.1f, 0f), 1e-6f);
            Assert.AreNotEqual(0f, ControlResponse.Apply(0.2f, 0.1f, 0f));
        }

        [Test]
        public void OutputStartsFromZeroJustOutsideTheDeadZone()
        {
            // A jump here would make the aircraft twitch the moment the thumb moves.
            float justOutside = ControlResponse.Apply(0.1001f, 0.1f, 0.5f);
            Assert.Less(MathF.Abs(justOutside), 0.01f);
        }

        [Test]
        public void FullDeflectionAlwaysReachesOne()
        {
            foreach (float expo in new[] { 0f, 0.5f, 1f })
                foreach (float deadZone in new[] { 0f, 0.1f, 0.3f })
                    Assert.AreEqual(1f, ControlResponse.Apply(1f, deadZone, expo), 1e-5f,
                        $"expo {expo}, dead zone {deadZone}");
        }

        [Test]
        public void ExpoSoftensTheMiddleWithoutChangingSign()
        {
            float linear = ControlResponse.Apply(0.5f, 0f, 0f);
            float shaped = ControlResponse.Apply(0.5f, 0f, 1f);
            Assert.Less(shaped, linear, "expo must give finer control near centre");
            Assert.Greater(shaped, 0f);
            Assert.AreEqual(-shaped, ControlResponse.Apply(-0.5f, 0f, 1f), 1e-6f, "must stay symmetric");
        }

        [Test]
        public void InputIsClampedToUnitRange()
        {
            Assert.AreEqual(1f, ControlResponse.Apply(5f, 0.1f, 0.5f), 1e-5f);
            Assert.AreEqual(-1f, ControlResponse.Apply(-5f, 0.1f, 0.5f), 1e-5f);
        }
    }

    [TestFixture]
    public class VirtualStickTests
    {
        static readonly ScreenPoint Origin = new ScreenPoint(200f, 300f);

        static VirtualStickModel Stick() => new VirtualStickModel
        {
            Radius = 100f,
            DeadZone = 0.1f,
            Expo = 0f,
        };

        [Test]
        public void RestingThumbCommandsNothing()
        {
            (float roll, float pitch) = Stick().Evaluate(Origin, Origin);
            Assert.AreEqual(0f, roll, 1e-6f);
            Assert.AreEqual(0f, pitch, 1e-6f);
        }

        [Test]
        public void ThumbRightRollsRight()
        {
            (float roll, float pitch) = Stick().Evaluate(Origin, new ScreenPoint(300f, 300f));
            Assert.AreEqual(1f, roll, 1e-4f);
            Assert.AreEqual(0f, pitch, 1e-4f);
        }

        [Test]
        public void ThumbDownIsAPullWhichIsNoseUp()
        {
            // Screen y decreases downward; pulling the stick back must be +pitch.
            (float _, float pitch) = Stick().Evaluate(Origin, new ScreenPoint(200f, 200f));
            Assert.AreEqual(1f, pitch, 1e-4f, "dragging down = pull = nose up");
        }

        [Test]
        public void ThumbUpPushesTheNoseDown()
        {
            (float _, float pitch) = Stick().Evaluate(Origin, new ScreenPoint(200f, 400f));
            Assert.AreEqual(-1f, pitch, 1e-4f);
        }

        [Test]
        public void DiagonalsCannotExceedFullDeflection()
        {
            (float roll, float pitch) = Stick().Evaluate(Origin, new ScreenPoint(500f, 600f));
            float magnitude = MathF.Sqrt(roll * roll + pitch * pitch);
            Assert.LessOrEqual(magnitude, 1f + 1e-4f, "the stick must live on a disc, not a square");
            Assert.AreEqual(1f, magnitude, 1e-3f, "and still reach full travel diagonally");
        }

        [Test]
        public void DirectionIsPreservedThroughExpo()
        {
            var stick = Stick();
            stick.Expo = 0.8f;
            // 30 degrees up-right from the origin.
            var current = new ScreenPoint(Origin.X + 86.6f, Origin.Y + 50f);
            (float roll, float pitch) = stick.Evaluate(Origin, current);
            // Expo changes the magnitude, never the heading.
            float angle = MathF.Atan2(-pitch, roll) * (180f / MathF.PI);
            Assert.AreEqual(30f, angle, 0.5f);
        }

        [Test]
        public void KnobStaysInsideTheRing()
        {
            ScreenPoint offset = Stick().KnobOffset(Origin, new ScreenPoint(1000f, 1000f));
            Assert.AreEqual(100f, offset.Length, 1e-3f);
        }
    }

    [TestFixture]
    public class ThrottleSliderTests
    {
        static ThrottleSliderModel Slider()
            => new ThrottleSliderModel(new LayoutRect(900f, 100f, 40f, 400f));

        [Test]
        public void AbsoluteMapsTrackEndsToZeroAndOne()
        {
            Assert.AreEqual(0f, Slider().Absolute(new ScreenPoint(920f, 100f)), 1e-5f);
            Assert.AreEqual(1f, Slider().Absolute(new ScreenPoint(920f, 500f)), 1e-5f);
            Assert.AreEqual(0.5f, Slider().Absolute(new ScreenPoint(920f, 300f)), 1e-5f);
        }

        [Test]
        public void AbsoluteClampsOutsideTheTrack()
        {
            Assert.AreEqual(0f, Slider().Absolute(new ScreenPoint(920f, -500f)), 1e-5f);
            Assert.AreEqual(1f, Slider().Absolute(new ScreenPoint(920f, 5000f)), 1e-5f);
        }

        [Test]
        public void RelativeDragDoesNotJump()
        {
            // Grabbing at the bottom of the track while throttle is at 0.5 must not
            // snap the throttle to zero — that would be an engine failure on touch.
            var grab = new ScreenPoint(920f, 120f);
            float value = Slider().Relative(0.5f, grab, grab);
            Assert.AreEqual(0.5f, value, 1e-5f);
        }

        [Test]
        public void RelativeDragMovesWithTheThumb()
        {
            var grab = new ScreenPoint(920f, 200f);
            float value = Slider().Relative(0.25f, grab, new ScreenPoint(920f, 400f));
            Assert.AreEqual(0.75f, value, 1e-4f, "200 px of a 400 px track = +0.5");
        }

        [Test]
        public void HandleTracksValue()
        {
            ScreenPoint handle = Slider().HandlePosition(0.25f);
            Assert.AreEqual(200f, handle.Y, 1e-4f);
            Assert.AreEqual(920f, handle.X, 1e-4f);
        }
    }

    [TestFixture]
    public class TiltModelTests
    {
        static TiltModel Level()
        {
            var tilt = TiltModel.Default();
            tilt.Expo = 0f;
            tilt.DeadZone = 0f;
            tilt.MaxTiltDegrees = 30f;
            tilt.Calibrate(new GravityVector(0f, -0.7071f, -0.7071f));
            return tilt;
        }

        [Test]
        public void NeutralAttitudeCommandsNothing()
        {
            TiltModel tilt = Level();
            (float roll, float pitch) = tilt.Evaluate(tilt.Neutral);
            Assert.AreEqual(0f, roll, 1e-5f);
            Assert.AreEqual(0f, pitch, 1e-5f);
        }

        [Test]
        public void CalibrationMakesAnyHoldingAngleNeutral()
        {
            var tilt = TiltModel.Default();
            tilt.DeadZone = 0f;
            var awkward = new GravityVector(0.35f, -0.2f, -0.91f);
            tilt.Calibrate(awkward);
            (float roll, float pitch) = tilt.Evaluate(awkward);
            Assert.AreEqual(0f, roll, 1e-5f);
            Assert.AreEqual(0f, pitch, 1e-5f);
            Assert.IsTrue(tilt.Calibrated);
        }

        [Test]
        public void RollingTheDeviceRightRollsRight()
        {
            TiltModel tilt = Level();
            // Tip the right edge down: gravity gains a +x (screen right) component.
            var tilted = new GravityVector(MathF.Sin(15f * MathF.PI / 180f), -0.7071f, -0.7071f);
            (float roll, float _) = tilt.Evaluate(tilted);
            Assert.Greater(roll, 0f);
            Assert.AreEqual(0.5f, roll, 0.02f, "15 of 30 degrees is half deflection");
        }

        [Test]
        public void FullTiltSaturatesAndDoesNotWrap()
        {
            TiltModel tilt = Level();
            var extreme = new GravityVector(1f, -0.7071f, 0f);
            (float roll, float _) = tilt.Evaluate(extreme);
            Assert.AreEqual(1f, roll, 1e-4f, "past the configured tilt must clamp, not reverse");
        }

        [Test]
        public void DeadZoneAbsorbsHandTremor()
        {
            var tilt = TiltModel.Default();
            tilt.DeadZone = 0.1f;
            tilt.Calibrate(new GravityVector(0f, -0.7071f, -0.7071f));
            var tremor = new GravityVector(0.01f, -0.7071f, -0.7071f);
            (float roll, float pitch) = tilt.Evaluate(tremor);
            Assert.AreEqual(0f, roll, 1e-6f);
            Assert.AreEqual(0f, pitch, 1e-6f);
        }
    }

    [TestFixture]
    public class TurnCoordinatorTests
    {
        [Test]
        public void DisabledPassesManualRudderThrough()
        {
            var coordinator = TurnCoordinator.Default();
            coordinator.Enabled = false;
            Assert.AreEqual(0.4f, coordinator.Rudder(1f, 0.2f, 0.4f), 1e-5f);
        }

        [Test]
        public void RollingRightLeadsWithRightRudder()
        {
            var coordinator = TurnCoordinator.Default();
            Assert.Greater(coordinator.Rudder(1f, 0f), 0f, "counter adverse yaw");
            Assert.Less(coordinator.Rudder(-1f, 0f), 0f);
        }

        [Test]
        public void SlipIsCorrectedTowardTheWind()
        {
            var coordinator = TurnCoordinator.Default();
            // Positive sideslip = relative wind from the right = needs right rudder.
            Assert.Greater(coordinator.Rudder(0f, 0.1f), 0f);
            Assert.Less(coordinator.Rudder(0f, -0.1f), 0f);
        }

        [Test]
        public void AssistIsBoundedByItsAuthority()
        {
            var coordinator = TurnCoordinator.Default();
            float huge = coordinator.Rudder(1f, 1.5f);
            Assert.LessOrEqual(huge, coordinator.Authority + 1e-5f,
                "the assist must never be able to boot the rudder to the stop by itself");
        }

        [Test]
        public void ManualInputCanOverrideTheAssist()
        {
            var coordinator = TurnCoordinator.Default();
            // Deliberate slip: full opposite rudder must beat the assist.
            float withManual = coordinator.Rudder(0f, 0.1f, -1f);
            Assert.Less(withManual, 0f, "a player who wants to slip must be able to");
        }

        [Test]
        public void OutputAlwaysStaysInRange()
        {
            var coordinator = TurnCoordinator.Default();
            for (float slip = -1f; slip <= 1f; slip += 0.1f)
                foreach (float manual in new[] { -1f, 0f, 1f })
                    Assert.That(coordinator.Rudder(1f, slip, manual), Is.InRange(-1f, 1f));
        }
    }

    [TestFixture]
    public class TouchLayoutTests
    {
        // iPhone 14 landscape, with the notch inset on the left and the home
        // indicator along the bottom.
        static LayoutRect PhoneSafeArea() => new LayoutRect(47f, 21f, 770f, 369f);
        static LayoutRect PadSafeArea() => new LayoutRect(0f, 0f, 2360f, 1640f);

        [Test]
        public void EverythingStaysInsideTheSafeArea()
        {
            LayoutRect safe = PhoneSafeArea();
            var layout = new TouchLayout(safe);
            foreach ((string name, LayoutRect rect) in Regions(layout))
            {
                Assert.GreaterOrEqual(rect.X, safe.X - 0.01f, $"{name} left");
                Assert.GreaterOrEqual(rect.Y, safe.Y - 0.01f, $"{name} bottom");
                Assert.LessOrEqual(rect.MaxX, safe.MaxX + 0.01f, $"{name} right");
                Assert.LessOrEqual(rect.MaxY, safe.MaxY + 0.01f, $"{name} top");
            }
        }

        [Test]
        public void ControlsDoNotOverlapEachOther()
        {
            var layout = new TouchLayout(PhoneSafeArea());
            (string, LayoutRect)[] regions = Regions(layout);
            for (int i = 0; i < regions.Length; i++)
            {
                for (int j = i + 1; j < regions.Length; j++)
                {
                    // The stick zone is a landing area, not a widget; it is allowed
                    // to be large, but must not swallow another control.
                    Assert.IsFalse(regions[i].Item2.Overlaps(regions[j].Item2),
                        $"{regions[i].Item1} overlaps {regions[j].Item1}");
                }
            }
        }

        [Test]
        public void ButtonsMeetTheMinimumTouchTarget()
        {
            var layout = new TouchLayout(PhoneSafeArea());
            foreach ((string name, LayoutRect rect) in new[]
            {
                ("flap down", layout.FlapDownButton),
                ("flap up", layout.FlapUpButton),
                ("rudder left", layout.RudderLeftButton),
                ("rudder right", layout.RudderRightButton),
            })
            {
                Assert.GreaterOrEqual(rect.Width, TouchLayout.MinimumTouchTarget * 0.75f, $"{name} width");
                Assert.GreaterOrEqual(rect.Height, TouchLayout.MinimumTouchTarget * 0.75f, $"{name} height");
            }
        }

        [Test]
        public void LayoutScalesWithTheDeviceNotThePixelCount()
        {
            var phone = new TouchLayout(PhoneSafeArea());
            var pad = new TouchLayout(PadSafeArea());
            Assert.Greater(pad.Scale, phone.Scale, "an iPad must not get iPhone-sized targets");
            // Proportionally the throttle occupies a similar share of the screen.
            float phoneShare = phone.ThrottleTrack.Height / PhoneSafeArea().Height;
            float padShare = pad.ThrottleTrack.Height / PadSafeArea().Height;
            Assert.AreEqual(phoneShare, padShare, 0.02f);
        }

        [Test]
        public void StickZoneIsOnTheLeftAndThrottleOnTheRight()
        {
            var layout = new TouchLayout(PhoneSafeArea());
            Assert.Less(layout.StickZone.Center.X, PhoneSafeArea().Center.X);
            Assert.Greater(layout.ThrottleTrack.Center.X, PhoneSafeArea().Center.X);
        }

        static (string, LayoutRect)[] Regions(in TouchLayout layout) => new[]
        {
            ("stick", layout.StickZone),
            ("throttle", layout.ThrottleTrack),
            ("flap down", layout.FlapDownButton),
            ("flap up", layout.FlapUpButton),
            ("rudder left", layout.RudderLeftButton),
            ("rudder right", layout.RudderRightButton),
        };
    }
}
