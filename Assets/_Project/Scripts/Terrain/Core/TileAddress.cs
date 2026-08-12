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

        public TileAddress Parent
            => Level <= 0 ? new TileAddress(0, 0, 0) : new TileAddress(Level - 1, X / 2, Y / 2);

        /// <summary>The tile at <paramref name="level"/> containing a point; clamped to the region.</summary>
        public static TileAddress ForPoint(int level, in LocalNE point)
        {
            int count = 1 << level;
            float size = Region.Size / count;
            int x = Math.Clamp((int)MathF.Floor((point.North + Region.HalfSize) / size), 0, count - 1);
            int y = Math.Clamp((int)MathF.Floor((point.East + Region.HalfSize) / size), 0, count - 1);
            return new TileAddress(level, x, y);
        }

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
