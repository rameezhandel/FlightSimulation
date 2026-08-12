#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using Cirrus.Aircraft;
using Cirrus.Core;
using Cirrus.Flight;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cirrus.DebugTools
{
    /// <summary>
    /// In-app side of the flight recorder (CLAUDE.md §8): samples the full state at
    /// 10 Hz into a ring buffer holding the last five minutes; press R to dump the
    /// buffer to persistentDataPath. Copy interesting captures into
    /// Assets/_Project/Tests/Recordings/ to turn a reported bug into a headless
    /// repro (FlightReplay in the test harness reads the same binary format).
    /// </summary>
    [RequireComponent(typeof(FlightBody))]
    public sealed class FlightRecorder : MonoBehaviour
    {
        const int Capacity = 3000; // 5 min at 10 Hz

        FlightBody _flight = null!;
        Rigidbody _body = null!;
        readonly FlightRecordingFrame[] _ring = new FlightRecordingFrame[Capacity];
        int _next;
        int _count;
        float _time;
        float _nextSample;

        void Awake()
        {
            _flight = GetComponent<FlightBody>();
            _body = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            _time += Time.fixedDeltaTime;
            if (_time < _nextSample) return;
            _nextSample += FlightRecording.SampleInterval;

            System.Numerics.Quaternion orientation = CoreFrame.ToCore(transform.rotation);
            _ring[_next] = new FlightRecordingFrame
            {
                Time = _time,
                State = new RigidBodyState
                {
                    Position = CoreFrame.ToCore(transform.position),
                    Velocity = CoreFrame.ToCore(_body.linearVelocity),
                    Orientation = orientation,
                    AngularVelocity = CoreFrame.AngularVelocityToCoreBody(_body.angularVelocity, orientation),
                },
                Controls = _flight.Controls,
                Wind = _flight.WindCore,
            };
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                Dump();
        }

        void Dump()
        {
            var recording = new FlightRecording { AircraftName = _flight.Aircraft.Name };
            int start = (_next - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++)
                recording.Frames.Add(_ring[(start + i) % Capacity]);

            string path = Path.Combine(
                Application.persistentDataPath,
                $"flight-{System.DateTime.Now:yyyyMMdd-HHmmss}.cirrec");
            using (FileStream file = File.Create(path))
                recording.WriteTo(file);
            UnityEngine.Debug.Log($"Flight recording ({_count} frames) written to {path}");
        }
    }
}
#endif
