using System;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Quadtree tile address. Level 0 is the single root tile covering the whole
    /// region; each level halves the tile size. X indexes north (0 = south edge),
    /// Y indexes east (0 = west edge) — matching the core world frame's X-north /
    /// Y-east convention.
    /// </summary>
    public readonly struct TileAddress : IEquatable<TileAddress>
    {
        public readonly int Level;
        public readonly int X; // along north
        public readonly int Y; // along east

        public TileAddress(int level, int x, int y)
        {
            Level = level;
            X = x;
            Y = y;
        }

        public float Size => Region.Size / (1 << Level);

        /// <summary>South-west corner in region-local metres.</summary>
        public LocalNE MinCorner
            => new LocalNE(-Region.HalfSize + X * Size, -Region.HalfSize + Y * Size);

        public LocalNE Center
            => new LocalNE(-Region.HalfSize + (X + 0.5f) * Size, -Region.HalfSize + (Y + 0.5f) * Size);

        public TileAddress Child(int childX, int childY)
            => new TileAddress(Level + 1, X * 2 + childX, Y * 2 + childY);

        /// <summary>Shortest horizontal distance from a point to this tile's footprint (0 inside).</summary>
        public float DistanceTo(in LocalNE p)
        {
            LocalNE min = MinCorner;
            float dn = MathF.Max(MathF.Max(min.North - p.North, 0f), p.North - (min.North + Size));
            float de = MathF.Max(MathF.Max(min.East - p.East, 0f), p.East - (min.East + Size));
            return MathF.Sqrt(dn * dn + de * de);
        }

        public bool Equals(TileAddress other) => Level == other.Level && X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is TileAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Level, X, Y);
        public override string ToString() => $"L{Level}({X},{Y})";
    }
}
