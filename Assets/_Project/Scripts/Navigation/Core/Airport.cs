using System;
using Cirrus.Terrain;

namespace Cirrus.Navigation
{
    /// <summary>
    /// One runway, described by its centre point and TRUE heading of the primary
    /// (lower-numbered) end. The designator is derived, not stored, so heading and
    /// designator can never drift apart.
    /// </summary>
    public readonly struct Runway
    {
        public readonly float TrueHeading;    // deg, primary end
        public readonly float LengthMeters;
        public readonly float WidthMeters;
        public readonly float ElevationMeters;

        public Runway(float trueHeading, float lengthMeters, float widthMeters, float elevationMeters)
        {
            TrueHeading = Bearings.Normalize(trueHeading);
            LengthMeters = lengthMeters;
            WidthMeters = widthMeters;
            ElevationMeters = elevationMeters;
        }

        public float ReciprocalTrueHeading => Bearings.Normalize(TrueHeading + 180f);

        /// <summary>Runway number of the primary end (1–36), from the magnetic heading.</summary>
        public int PrimaryDesignator => DesignatorFor(TrueHeading);

        public int ReciprocalDesignator => DesignatorFor(ReciprocalTrueHeading);

        /// <summary>e.g. "08/26".</summary>
        public string Name => $"{PrimaryDesignator:00}/{ReciprocalDesignator:00}";

        static int DesignatorFor(float trueHeading)
        {
            float magnetic = Bearings.TrueToMagnetic(trueHeading);
            int number = (int)MathF.Round(magnetic / 10f);
            if (number <= 0) number += 36;
            if (number > 36) number -= 36;
            return number;
        }

        /// <summary>Threshold of the primary end (the end you depart FROM on that heading).</summary>
        public LocalNE PrimaryThreshold(in LocalNE center)
            => Bearings.Offset(center, ReciprocalTrueHeading, LengthMeters * 0.5f);

        public LocalNE ReciprocalThreshold(in LocalNE center)
            => Bearings.Offset(center, TrueHeading, LengthMeters * 0.5f);
    }

    /// <summary>
    /// An airport in the region. Positions are geodetic on input and projected once
    /// into region-local metres, so everything downstream (nav, terrain pads,
    /// flight plans) shares one coordinate story.
    /// </summary>
    public sealed class Airport
    {
        public string Icao { get; }
        public string Name { get; }
        public GeoPoint Position { get; }
        public LocalNE Local { get; }
        public float ElevationMeters { get; }
        public Runway[] Runways { get; }

        public Airport(string icao, string name, GeoPoint position, float elevationMeters, Runway[] runways)
        {
            Icao = icao;
            Name = name;
            Position = position;
            Local = RegionProjection.ToLocal(position);
            ElevationMeters = elevationMeters;
            Runways = runways;
        }

        /// <summary>
        /// The runway end best aligned with the wind (landing into wind).
        /// Returns the TRUE heading to land on.
        /// </summary>
        /// <param name="windFromTrueBearing">Direction the wind blows FROM, deg true.</param>
        public float PreferredLandingHeading(float windFromTrueBearing)
        {
            float best = Runways.Length > 0 ? Runways[0].TrueHeading : 0f;
            float bestScore = float.MinValue;
            foreach (Runway runway in Runways)
            {
                foreach (float heading in new[] { runway.TrueHeading, runway.ReciprocalTrueHeading })
                {
                    // Headwind component: 1 when landing straight into the wind.
                    float score = MathF.Cos(Bearings.Difference(heading, windFromTrueBearing) * (MathF.PI / 180f));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = heading;
                    }
                }
            }
            return best;
        }
    }
}
