using System;
using System.Collections.Generic;
using Cirrus.Terrain;

namespace Cirrus.Navigation
{
    public readonly struct Waypoint
    {
        public readonly string Name;
        public readonly LocalNE Position;
        public readonly float AltitudeMeters; // planned crossing altitude; NaN = unconstrained

        public Waypoint(string name, in LocalNE position, float altitudeMeters = float.NaN)
        {
            Name = name;
            Position = position;
            AltitudeMeters = altitudeMeters;
        }

        public static Waypoint Over(Airport airport, float altitudeMeters = float.NaN)
            => new Waypoint(airport.Icao, airport.Local, altitudeMeters);
    }

    /// <summary>Where the aircraft is relative to the active leg.</summary>
    public readonly struct LegProgress
    {
        /// <summary>Metres left/right of the course line; positive = right of course.</summary>
        public readonly float CrossTrackMeters;
        /// <summary>Metres travelled along the leg from its start (may exceed the leg length).</summary>
        public readonly float AlongTrackMeters;
        /// <summary>Metres still to run to the leg's end (0 once past it).</summary>
        public readonly float RemainingMeters;
        /// <summary>True bearing of the leg's course.</summary>
        public readonly float CourseTrue;
        /// <summary>True bearing from the aircraft straight to the leg end.</summary>
        public readonly float BearingToEndTrue;

        public LegProgress(float crossTrack, float alongTrack, float remaining, float courseTrue, float bearingToEndTrue)
        {
            CrossTrackMeters = crossTrack;
            AlongTrackMeters = alongTrack;
            RemainingMeters = remaining;
            CourseTrue = courseTrue;
            BearingToEndTrue = bearingToEndTrue;
        }
    }

    /// <summary>
    /// An ordered list of waypoints and the arithmetic a HUD or autopilot needs off
    /// it. Pure and immutable-ish: the plan is data, the aircraft's progress is a
    /// query. Sequencing (auto-advance to the next leg) is deliberately left to the
    /// caller so the UI can decide the policy.
    /// </summary>
    public sealed class FlightPlan
    {
        readonly List<Waypoint> _waypoints;

        public FlightPlan(IEnumerable<Waypoint> waypoints) => _waypoints = new List<Waypoint>(waypoints);

        public IReadOnlyList<Waypoint> Waypoints => _waypoints;

        public int LegCount => Math.Max(0, _waypoints.Count - 1);

        public float LegDistance(int leg)
        {
            RequireLeg(leg);
            return Bearings.DistanceMeters(_waypoints[leg].Position, _waypoints[leg + 1].Position);
        }

        public float LegCourseTrue(int leg)
        {
            RequireLeg(leg);
            return Bearings.TrueBearing(_waypoints[leg].Position, _waypoints[leg + 1].Position);
        }

        public float TotalDistanceMeters
        {
            get
            {
                float total = 0f;
                for (int leg = 0; leg < LegCount; leg++) total += LegDistance(leg);
                return total;
            }
        }

        public LegProgress Progress(int leg, in LocalNE position)
        {
            RequireLeg(leg);
            LocalNE start = _waypoints[leg].Position;
            LocalNE end = _waypoints[leg + 1].Position;

            float course = Bearings.TrueBearing(start, end);
            LocalNE unit = Bearings.Direction(course);

            float dn = position.North - start.North;
            float de = position.East - start.East;

            // Along-track is the projection onto the course; cross-track is the
            // perpendicular component, positive to the RIGHT of course (which for
            // north/east axes is the (east, -north) normal of the course vector).
            float along = dn * unit.North + de * unit.East;
            float cross = de * unit.North - dn * unit.East;

            float legLength = Bearings.DistanceMeters(start, end);
            float remaining = MathF.Max(0f, legLength - along);
            float bearingToEnd = Bearings.TrueBearing(position, end);
            return new LegProgress(cross, along, remaining, course, bearingToEnd);
        }

        /// <summary>Distance from a position to the end of the plan, following the remaining legs.</summary>
        public float RemainingDistance(int leg, in LocalNE position)
        {
            RequireLeg(leg);
            float remaining = Progress(leg, position).RemainingMeters;
            for (int next = leg + 1; next < LegCount; next++) remaining += LegDistance(next);
            return remaining;
        }

        /// <summary>The classic first flight: Juneau to Skagway (CLAUDE.md §10, M2 gate).</summary>
        public static FlightPlan JuneauToSkagway(float cruiseAltitudeMeters = 1200f)
            => new FlightPlan(new[]
            {
                Waypoint.Over(SoutheastAlaskaAirports.Juneau),
                Waypoint.Over(SoutheastAlaskaAirports.Skagway, cruiseAltitudeMeters),
            });

        void RequireLeg(int leg)
        {
            if (leg < 0 || leg >= LegCount)
                throw new ArgumentOutOfRangeException(nameof(leg), $"leg {leg} outside 0..{LegCount - 1}");
        }
    }
}
