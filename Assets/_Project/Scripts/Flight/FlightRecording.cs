using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Cirrus.Flight
{
    public struct FlightRecordingFrame
    {
        public float Time;             // s since recording start
        public RigidBodyState State;
        public ControlInputs Controls;
        public Vector3 Wind;           // world frame
    }

    /// <summary>
    /// Full-state flight recording at 10 Hz (CLAUDE.md §8). Pure data + binary
    /// serialization, no dependencies, so recordings written by the app replay
    /// byte-for-byte in the headless test harness. Any reported flight-model bug
    /// gets one of these in Assets/_Project/Tests/Recordings/ before it gets a fix.
    /// </summary>
    public sealed class FlightRecording
    {
        public const uint Magic = 0x43495252;   // "CIRR"
        public const int FormatVersion = 1;
        public const float SampleInterval = 0.1f; // 10 Hz

        public string AircraftName = "";
        public List<FlightRecordingFrame> Frames = new List<FlightRecordingFrame>();

        public void WriteTo(Stream stream)
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(AircraftName);
            writer.Write(Frames.Count);
            foreach (FlightRecordingFrame frame in Frames)
            {
                writer.Write(frame.Time);
                WriteVector(writer, frame.State.Position);
                WriteVector(writer, frame.State.Velocity);
                writer.Write(frame.State.Orientation.X);
                writer.Write(frame.State.Orientation.Y);
                writer.Write(frame.State.Orientation.Z);
                writer.Write(frame.State.Orientation.W);
                WriteVector(writer, frame.State.AngularVelocity);
                writer.Write(frame.Controls.Pitch);
                writer.Write(frame.Controls.Roll);
                writer.Write(frame.Controls.Yaw);
                writer.Write(frame.Controls.Flap);
                writer.Write(frame.Controls.Throttle);
                WriteVector(writer, frame.Wind);
            }
        }

        public static FlightRecording ReadFrom(Stream stream)
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Not a Cirrus flight recording.");
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Unsupported recording version {version} (expected {FormatVersion}).");

            var recording = new FlightRecording { AircraftName = reader.ReadString() };
            int count = reader.ReadInt32();
            if (count < 0 || count > 10_000_000)
                throw new InvalidDataException($"Implausible frame count {count}.");
            recording.Frames.Capacity = count;

            for (int i = 0; i < count; i++)
            {
                var frame = new FlightRecordingFrame { Time = reader.ReadSingle() };
                frame.State.Position = ReadVector(reader);
                frame.State.Velocity = ReadVector(reader);
                frame.State.Orientation = new Quaternion(
                    reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                frame.State.AngularVelocity = ReadVector(reader);
                frame.Controls.Pitch = reader.ReadSingle();
                frame.Controls.Roll = reader.ReadSingle();
                frame.Controls.Yaw = reader.ReadSingle();
                frame.Controls.Flap = reader.ReadSingle();
                frame.Controls.Throttle = reader.ReadSingle();
                frame.Wind = ReadVector(reader);
                recording.Frames.Add(frame);
            }
            return recording;
        }

        static void WriteVector(BinaryWriter writer, in Vector3 v)
        {
            writer.Write(v.X);
            writer.Write(v.Y);
            writer.Write(v.Z);
        }

        static Vector3 ReadVector(BinaryReader reader)
            => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    /// <summary>
    /// Deterministic replay: re-integrates from the recording's first frame with
    /// SixDofSimulator at the project timestep, holding each frame's controls until
    /// the next sample (zero-order hold). For recordings captured headlessly this
    /// reproduces the trajectory exactly; for recordings captured in-app it measures
    /// how far the Unity/PhysX integration has drifted from the core — itself a
    /// useful diagnostic.
    /// </summary>
    public static class FlightReplay
    {
        public delegate void FrameVisitor(int frameIndex, in RigidBodyState replayed, in FlightRecordingFrame recorded);

        /// <summary>Steps through the whole recording; returns the max position deviation (m) vs the recorded frames.</summary>
        public static float Run(FlightRecording recording, AircraftDefinition aircraft, FrameVisitor? visitor = null)
        {
            if (recording.Frames.Count < 2)
                return 0f;

            int stepsPerSample = (int)MathF.Round(FlightRecording.SampleInterval / SixDofSimulator.FixedTimestep);
            RigidBodyState state = recording.Frames[0].State;
            float maxDeviation = 0f;

            for (int i = 1; i < recording.Frames.Count; i++)
            {
                FlightRecordingFrame previous = recording.Frames[i - 1];
                for (int step = 0; step < stepsPerSample; step++)
                    SixDofSimulator.Step(aircraft, ref state, previous.Controls, previous.Wind, SixDofSimulator.FixedTimestep);

                FlightRecordingFrame recorded = recording.Frames[i];
                float deviation = (state.Position - recorded.State.Position).Length();
                if (deviation > maxDeviation) maxDeviation = deviation;
                visitor?.Invoke(i, in state, in recorded);
            }
            return maxDeviation;
        }
    }
}
