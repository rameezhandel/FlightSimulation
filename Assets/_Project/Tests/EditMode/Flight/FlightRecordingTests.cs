using System;
using System.IO;
using System.Numerics;
using Cirrus.Aircraft;
using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.Flight
{
    [TestFixture]
    public class FlightRecordingTests
    {
        /// <summary>
        /// Flies a scripted 12 s maneuver (trimmed cruise, then an elevator pulse and
        /// an aileron pulse) headlessly, sampling at 10 Hz — the same shape of data
        /// the in-app recorder captures.
        /// </summary>
        static FlightRecording RecordManeuver(AircraftDefinition aircraft)
        {
            float speed = 100f * MathUtil.KnotsToMetersPerSecond;
            TrimResult trim = TrimSolver.SolveLevelFlight(aircraft, speed, 400f);
            Assert.IsTrue(trim.Converged, "test prerequisite: trim must converge");

            var recording = new FlightRecording { AircraftName = aircraft.Name };
            RigidBodyState state = trim.State;
            int stepsPerSample = (int)MathF.Round(FlightRecording.SampleInterval / SixDofSimulator.FixedTimestep);

            for (int frame = 0; frame < 120; frame++)
            {
                float time = frame * FlightRecording.SampleInterval;
                ControlInputs controls = trim.Controls;
                if (time is > 2f and < 3f) controls.Pitch += 0.3f;   // elevator pulse
                if (time is > 6f and < 7f) controls.Roll += 0.4f;    // aileron pulse

                recording.Frames.Add(new FlightRecordingFrame
                {
                    Time = time,
                    State = state,
                    Controls = controls,
                    Wind = Vector3.Zero,
                });

                for (int step = 0; step < stepsPerSample; step++)
                    SixDofSimulator.Step(aircraft, ref state, in controls, Vector3.Zero, SixDofSimulator.FixedTimestep);
            }
            return recording;
        }

        [Test]
        public void SerializationRoundTripIsExact()
        {
            AircraftDefinition beaver = BeaverDefinition.Create();
            FlightRecording original = RecordManeuver(beaver);

            using var buffer = new MemoryStream();
            original.WriteTo(buffer);
            buffer.Position = 0;
            FlightRecording restored = FlightRecording.ReadFrom(buffer);

            Assert.AreEqual(original.AircraftName, restored.AircraftName);
            Assert.AreEqual(original.Frames.Count, restored.Frames.Count);
            for (int i = 0; i < original.Frames.Count; i++)
            {
                // Bitwise float equality: serialization must not lose precision.
                Assert.AreEqual(original.Frames[i].Time, restored.Frames[i].Time);
                Assert.AreEqual(original.Frames[i].State.Position, restored.Frames[i].State.Position);
                Assert.AreEqual(original.Frames[i].State.Orientation, restored.Frames[i].State.Orientation);
                Assert.AreEqual(original.Frames[i].Controls.Throttle, restored.Frames[i].Controls.Throttle);
            }
        }

        /// <summary>CLAUDE.md §8: replay must be deterministic.</summary>
        [Test]
        public void ReplayReproducesHeadlessRecordingExactly()
        {
            AircraftDefinition beaver = BeaverDefinition.Create();
            FlightRecording recording = RecordManeuver(beaver);

            float deviation = FlightReplay.Run(recording, beaver);
            // Same integrator, same steps, same floats: this should be identically zero.
            Assert.AreEqual(0f, deviation, 1e-4f, $"replay deviated {deviation} m from the recording");
        }

        [Test]
        public void ReplayIsRunToRunDeterministic()
        {
            AircraftDefinition beaver = BeaverDefinition.Create();
            FlightRecording recording = RecordManeuver(beaver);

            var firstRun = new Vector3[recording.Frames.Count];
            FlightReplay.Run(recording, beaver, (int i, in RigidBodyState replayed, in FlightRecordingFrame _) =>
                firstRun[i] = replayed.Position);
            FlightReplay.Run(recording, beaver, (int i, in RigidBodyState replayed, in FlightRecordingFrame _) =>
                Assert.AreEqual(firstRun[i], replayed.Position, $"nondeterministic at frame {i}"));
        }

        [Test]
        public void GarbageInputIsRejected()
        {
            using var garbage = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
            Assert.Throws<InvalidDataException>(() => FlightRecording.ReadFrom(garbage));
        }

        [Test]
        public void WrongVersionIsRejected()
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(FlightRecording.Magic);
                writer.Write(999);
            }
            buffer.Position = 0;
            Assert.Throws<InvalidDataException>(() => FlightRecording.ReadFrom(buffer));
        }
    }
}
