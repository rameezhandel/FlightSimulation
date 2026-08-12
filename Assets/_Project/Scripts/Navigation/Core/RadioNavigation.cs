using System;
using Cirrus.Terrain;

namespace Cirrus.Navigation
{
    public enum NavaidKind
    {
        Vor,
        VorDme,
        Ndb,
    }

    /// <summary>
    /// A ground navigation station. Radials are published MAGNETIC (as on charts
    /// and instruments), while all geometry underneath is true — the conversion
    /// happens here, once.
    /// </summary>
    public sealed class Navaid
    {
        public string Identifier { get; }
        public string Name { get; }
        public NavaidKind Kind { get; }
        public GeoPoint Position { get; }
        public LocalNE Local { get; }
        public float FrequencyMhz { get; }   // kHz for NDBs
        public float ServiceRangeMeters { get; }

        public Navaid(string identifier, string name, NavaidKind kind, GeoPoint position,
                      float frequencyMhz, float serviceRangeMeters = 150_000f)
        {
            Identifier = identifier;
            Name = name;
            Kind = kind;
            Position = position;
            Local = RegionProjection.ToLocal(position);
            FrequencyMhz = frequencyMhz;
            ServiceRangeMeters = serviceRangeMeters;
        }

        public bool HasDme => Kind == NavaidKind.VorDme;

        /// <summary>Slant-range DME distance, metres. Aircraft altitude matters close in — that is the DME error pilots know.</summary>
        public float SlantRange(in LocalNE position, float altitudeMeters)
        {
            float ground = Bearings.DistanceMeters(position, Local);
            float up = MathF.Max(0f, altitudeMeters);
            return MathF.Sqrt(ground * ground + up * up);
        }

        /// <summary>The magnetic radial the aircraft sits on (bearing FROM the station).</summary>
        public float RadialFrom(in LocalNE position)
            => Bearings.TrueToMagnetic(Bearings.TrueBearing(Local, position));

        /// <summary>Magnetic bearing TO the station — what an RMI/ADF needle points at.</summary>
        public float BearingTo(in LocalNE position)
            => Bearings.TrueToMagnetic(Bearings.TrueBearing(position, Local));

        /// <summary>Relative bearing to the station from the nose, degrees clockwise (ADF needle).</summary>
        public float RelativeBearing(in LocalNE position, float aircraftTrueHeading)
            => Bearings.Normalize(Bearings.TrueBearing(position, Local) - aircraftTrueHeading);

        public bool InRange(in LocalNE position, float altitudeMeters)
            => SlantRange(position, altitudeMeters) <= ServiceRangeMeters;
    }

    /// <summary>
    /// What a VOR indicator (CDI) shows. Deviation is in dots, the unit pilots
    /// actually fly: full scale is 10° for VOR tracking, 2.5 dots per side.
    /// </summary>
    public readonly struct CourseDeviation
    {
        public readonly float DegreesOffCourse; // signed; positive = course is to the right
        public readonly float Dots;             // clamped to full-scale
        public readonly bool To;                // TO/FROM flag: true = flying TO the station
        public readonly bool Valid;             // false = out of range (OFF flag)

        public CourseDeviation(float degreesOffCourse, float dots, bool to, bool valid)
        {
            DegreesOffCourse = degreesOffCourse;
            Dots = dots;
            To = to;
            Valid = valid;
        }
    }

    public static class RadioNavigation
    {
        public const float VorFullScaleDegrees = 10f;
        public const float VorFullScaleDots = 2.5f;

        /// <summary>
        /// CDI indication for a selected magnetic course (OBS setting).
        /// TO/FROM comes from whether the station lies ahead of the course line,
        /// exactly as the real instrument resolves it.
        /// </summary>
        public static CourseDeviation Deviation(
            Navaid station, in LocalNE position, float altitudeMeters, float selectedMagneticCourse)
        {
            if (!station.InRange(position, altitudeMeters))
                return new CourseDeviation(0f, 0f, false, false);

            float radial = station.RadialFrom(position);
            float error = Bearings.Difference(selectedMagneticCourse, radial);

            // |error| > 90 means the selected course points back at the station.
            bool to = MathF.Abs(error) > 90f;
            if (to)
                error = Bearings.Difference(Bearings.Normalize(selectedMagneticCourse + 180f), radial);

            float clamped = Math.Clamp(error, -VorFullScaleDegrees, VorFullScaleDegrees);
            float dots = clamped / VorFullScaleDegrees * VorFullScaleDots;
            // Needle deflects toward the course: fly toward the needle.
            return new CourseDeviation(-error, -dots, to, true);
        }
    }

    /// <summary>
    /// Region navaids. **[EST] — placeholders, not real published facilities.**
    /// Real SE Alaska navaid identifiers, frequencies and positions must come from
    /// an official source before anything ships; these exist so the radio-nav math
    /// has something to be tested against and the HUD has something to display.
    /// </summary>
    public static class RegionNavaids
    {
        public static Navaid JuneauVor { get; } = new Navaid(
            "JNU", "Juneau", NavaidKind.VorDme, new GeoPoint(58.3548, -134.5830), 112.0f);

        public static Navaid SitkaVor { get; } = new Navaid(
            "SIT", "Sitka", NavaidKind.VorDme, new GeoPoint(57.0500, -135.3650), 114.4f);

        public static Navaid[] All { get; } = { JuneauVor, SitkaVor };
    }
}
