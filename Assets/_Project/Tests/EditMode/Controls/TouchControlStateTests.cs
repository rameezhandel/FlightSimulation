using System;
using Cirrus.Controls;
using NUnit.Framework;

namespace Cirrus.Tests.Controls
{
    /// <summary>
    /// The finger-tracking state machine. These are the cases that are miserable to
    /// debug on a device: a second finger arriving mid-drag, a finger lifting while
    /// the aircraft is banked, a thumb sliding out of the zone it started in.
    /// </summary>
    [TestFixture]
    public class TouchControlStateTests
    {
        static readonly LayoutRect SafeArea = new LayoutRect(47f, 21f, 770f, 369f);
        static readonly GravityVector Level = new GravityVector(0f, -0.7071f, -0.7071f);

        static TouchControlState NewState()
        {
            var state = new TouchControlState();
            state.SetLayout(SafeArea);
            state.SetInitial(0.5f, 0f);
            state.Coordinator.Enabled = false; // isolate raw inputs
            return state;
        }

        static ControlDemand Step(TouchControlState state, params TouchSample[] touches)
            => state.Update(touches, Level, 0f, 1f / 60f);

        static ScreenPoint InStickZone(TouchControlState state)
            => state.Layout.StickZone.Center;

        static ScreenPoint InThrottle(TouchControlState state)
            => state.Layout.ThrottleTrack.Center;

        [Test]
        public void NoTouchesMeansNeutralStickButKeepsThrottle()
        {
            TouchControlState state = NewState();
            ControlDemand demand = Step(state);
            Assert.AreEqual(0f, demand.Roll, 1e-6f);
            Assert.AreEqual(0f, demand.Pitch, 1e-6f);
            Assert.AreEqual(0.5f, demand.Throttle, 1e-6f, "throttle is a setting, not a held control");
        }

        [Test]
        public void StickPlantsWhereTheThumbLandsAndFollowsIt()
        {
            TouchControlState state = NewState();
            ScreenPoint origin = InStickZone(state);
            Step(state, new TouchSample(1, origin, TouchPhase.Began));
            Assert.IsTrue(state.StickActive);

            var moved = new ScreenPoint(origin.X + state.Stick.Radius, origin.Y);
            ControlDemand demand = Step(state, new TouchSample(1, moved, TouchPhase.Moved));
            Assert.Greater(demand.Roll, 0.9f, "full right roll");
        }

        [Test]
        public void LiftingTheStickFingerRecentresTheControls()
        {
            TouchControlState state = NewState();
            ScreenPoint origin = InStickZone(state);
            Step(state, new TouchSample(1, origin, TouchPhase.Began));
            Step(state, new TouchSample(1, new ScreenPoint(origin.X + 200f, origin.Y), TouchPhase.Moved));

            ControlDemand released = Step(state, new TouchSample(1, origin, TouchPhase.Ended));
            Assert.IsFalse(state.StickActive);
            Assert.AreEqual(0f, released.Roll, 1e-6f, "letting go must not leave the aircraft in a bank command");
        }

        [Test]
        public void ThrottleDragDoesNotStealTheStick()
        {
            TouchControlState state = NewState();
            ScreenPoint stickOrigin = InStickZone(state);
            Step(state, new TouchSample(1, stickOrigin, TouchPhase.Began));

            // Second finger grabs the throttle while the first still flies.
            ScreenPoint throttle = InThrottle(state);
            ControlDemand demand = Step(state,
                new TouchSample(1, new ScreenPoint(stickOrigin.X + state.Stick.Radius, stickOrigin.Y), TouchPhase.Moved),
                new TouchSample(2, throttle, TouchPhase.Began));

            Assert.IsTrue(state.StickActive, "the stick finger keeps its claim");
            Assert.Greater(demand.Roll, 0.9f, "and keeps commanding roll");
        }

        [Test]
        public void ThrottleDragsRelativeToWhereItWasGrabbed()
        {
            TouchControlState state = NewState();
            ScreenPoint grab = InThrottle(state);
            Step(state, new TouchSample(1, grab, TouchPhase.Began));
            Assert.AreEqual(0.5f, state.Throttle, 1e-5f, "grabbing must not jump the throttle");

            float quarterTrack = state.Layout.ThrottleTrack.Height * 0.25f;
            ControlDemand demand = Step(state,
                new TouchSample(1, new ScreenPoint(grab.X, grab.Y + quarterTrack), TouchPhase.Moved));
            Assert.AreEqual(0.75f, demand.Throttle, 1e-3f);
        }

        [Test]
        public void ThrottleSurvivesTheFingerLifting()
        {
            TouchControlState state = NewState();
            ScreenPoint grab = InThrottle(state);
            Step(state, new TouchSample(1, grab, TouchPhase.Began));
            float up = state.Layout.ThrottleTrack.Height * 0.4f;
            Step(state, new TouchSample(1, new ScreenPoint(grab.X, grab.Y + up), TouchPhase.Moved));
            ControlDemand after = Step(state, new TouchSample(1, grab, TouchPhase.Ended));
            Assert.AreEqual(0.9f, after.Throttle, 1e-3f, "power stays where you set it");
        }

        [Test]
        public void FlapButtonsStepAndClamp()
        {
            TouchControlState state = NewState();
            ScreenPoint down = state.Layout.FlapDownButton.Center;
            ScreenPoint up = state.Layout.FlapUpButton.Center;

            Step(state, new TouchSample(1, down, TouchPhase.Began));
            Assert.AreEqual(0.25f, state.Flap, 1e-5f);
            Step(state, new TouchSample(2, down, TouchPhase.Began));
            Assert.AreEqual(0.5f, state.Flap, 1e-5f);

            for (int i = 0; i < 10; i++)
                Step(state, new TouchSample(10 + i, down, TouchPhase.Began));
            Assert.AreEqual(1f, state.Flap, 1e-5f, "flaps clamp fully extended");

            for (int i = 0; i < 10; i++)
                Step(state, new TouchSample(30 + i, up, TouchPhase.Began));
            Assert.AreEqual(0f, state.Flap, 1e-5f, "and clamp fully retracted");
        }

        [Test]
        public void FlapIsOneStepPerPressNotPerFrame()
        {
            TouchControlState state = NewState();
            ScreenPoint down = state.Layout.FlapDownButton.Center;
            Step(state, new TouchSample(1, down, TouchPhase.Began));
            // Same finger held for many frames.
            for (int i = 0; i < 30; i++)
                Step(state, new TouchSample(1, down, TouchPhase.Stationary));
            Assert.AreEqual(0.25f, state.Flap, 1e-5f, "holding the button must not run the flaps out");
        }

        [Test]
        public void RudderRampsInAndBackOut()
        {
            TouchControlState state = NewState();
            ScreenPoint right = state.Layout.RudderRightButton.Center;

            ControlDemand first = Step(state, new TouchSample(1, right, TouchPhase.Began));
            Assert.Greater(first.Rudder, 0f);
            Assert.Less(first.Rudder, 1f, "a tap is a nudge, not a boot");

            ControlDemand held = first;
            for (int i = 0; i < 60; i++)
                held = Step(state, new TouchSample(1, right, TouchPhase.Stationary));
            Assert.AreEqual(1f, held.Rudder, 1e-3f, "holding reaches full deflection");

            ControlDemand released = Step(state, new TouchSample(1, right, TouchPhase.Ended));
            Assert.Less(released.Rudder, held.Rudder, "and it returns when you let go");
        }

        [Test]
        public void BothRudderButtonsCancel()
        {
            TouchControlState state = NewState();
            ControlDemand demand = Step(state,
                new TouchSample(1, state.Layout.RudderLeftButton.Center, TouchPhase.Began),
                new TouchSample(2, state.Layout.RudderRightButton.Center, TouchPhase.Began));
            Assert.AreEqual(0f, demand.Rudder, 1e-6f);
        }

        [Test]
        public void ThumbMayDragOutsideTheZoneItStartedIn()
        {
            TouchControlState state = NewState();
            ScreenPoint origin = InStickZone(state);
            Step(state, new TouchSample(1, origin, TouchPhase.Began));

            // Well outside the stick zone — a real thumb does this constantly.
            var faraway = new ScreenPoint(origin.X + 400f, origin.Y);
            ControlDemand demand = Step(state, new TouchSample(1, faraway, TouchPhase.Moved));
            Assert.IsTrue(state.StickActive, "the claim survives leaving the zone");
            Assert.AreEqual(1f, demand.Roll, 1e-3f);
        }

        [Test]
        public void TouchOutsideEveryControlIsIgnored()
        {
            TouchControlState state = NewState();
            // Top-right corner: no control lives there in stick mode.
            var empty = new ScreenPoint(SafeArea.MaxX - 5f, SafeArea.MaxY - 5f);
            ControlDemand demand = Step(state, new TouchSample(1, empty, TouchPhase.Began));
            Assert.IsFalse(state.StickActive);
            Assert.AreEqual(0f, demand.Roll, 1e-6f);
            Assert.AreEqual(0.5f, demand.Throttle, 1e-6f);
        }

        [Test]
        public void TiltSchemeIgnoresTheStickZoneButKeepsTheThrottle()
        {
            TouchControlState state = NewState();
            state.Scheme = TouchControlScheme.Tilt;
            state.Tilt.DeadZone = 0f;
            state.Tilt.Expo = 0f;
            state.Tilt.Calibrate(Level);

            ControlDemand demand = Step(state, new TouchSample(1, InStickZone(state), TouchPhase.Began));
            Assert.IsFalse(state.StickActive, "no on-screen stick in tilt mode");
            Assert.AreEqual(0f, demand.Roll, 1e-5f, "level device, level wings");

            // Throttle still works.
            ScreenPoint grab = InThrottle(state);
            Step(state, new TouchSample(2, grab, TouchPhase.Began));
            float up = state.Layout.ThrottleTrack.Height * 0.25f;
            ControlDemand withPower = Step(state,
                new TouchSample(2, new ScreenPoint(grab.X, grab.Y + up), TouchPhase.Moved));
            Assert.AreEqual(0.75f, withPower.Throttle, 1e-3f);
        }

        [Test]
        public void TiltSchemeCalibrationIsRequestedByTheButton()
        {
            TouchControlState state = NewState();
            state.Scheme = TouchControlScheme.Tilt;
            Assert.IsFalse(state.ConsumeCalibrationRequest());

            Step(state, new TouchSample(1, state.Layout.CalibrateButton.Center, TouchPhase.Began));
            Assert.IsTrue(state.ConsumeCalibrationRequest());
            Assert.IsFalse(state.ConsumeCalibrationRequest(), "the request is consumed once");
        }

        [Test]
        public void CoordinatorAddsRudderWhenEnabled()
        {
            TouchControlState state = NewState();
            state.Coordinator = TurnCoordinator.Default();
            ScreenPoint origin = InStickZone(state);
            Step(state, new TouchSample(1, origin, TouchPhase.Began));

            ControlDemand demand = state.Update(
                new[] { new TouchSample(1, new ScreenPoint(origin.X + state.Stick.Radius, origin.Y), TouchPhase.Moved) },
                Level, sideslipRadians: 0f, deltaTime: 1f / 60f);

            Assert.Greater(demand.Roll, 0.9f);
            Assert.Greater(demand.Rudder, 0f, "auto-coordination leads the turn with rudder");
        }

        [Test]
        public void AllDemandsStayInRangeUnderChaos()
        {
            TouchControlState state = NewState();
            state.Coordinator = TurnCoordinator.Default();
            var random = new Random(11);

            for (int frame = 0; frame < 400; frame++)
            {
                int count = random.Next(0, 4);
                var touches = new TouchSample[count];
                for (int i = 0; i < count; i++)
                {
                    var position = new ScreenPoint(
                        (float)(SafeArea.X + random.NextDouble() * SafeArea.Width),
                        (float)(SafeArea.Y + random.NextDouble() * SafeArea.Height));
                    TouchPhase phase = (TouchPhase)random.Next(0, 4);
                    touches[i] = new TouchSample(random.Next(0, 5), position, phase);
                }

                ControlDemand demand = state.Update(touches, Level,
                    (float)(random.NextDouble() - 0.5), 1f / 60f);

                Assert.That(demand.Roll, Is.InRange(-1f, 1f), $"roll, frame {frame}");
                Assert.That(demand.Pitch, Is.InRange(-1f, 1f), $"pitch, frame {frame}");
                Assert.That(demand.Rudder, Is.InRange(-1f, 1f), $"rudder, frame {frame}");
                Assert.That(demand.Throttle, Is.InRange(0f, 1f), $"throttle, frame {frame}");
                Assert.That(demand.Flap, Is.InRange(0f, 1f), $"flap, frame {frame}");
            }
        }
    }
}
