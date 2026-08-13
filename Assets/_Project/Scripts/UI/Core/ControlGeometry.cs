using System;

namespace Cirrus.Controls
{
    /// <summary>
    /// A point in screen pixels. Origin is BOTTOM-LEFT, x right, y up — Unity's
    /// screen convention, kept here so the pure layer and the touch input agree
    /// without a conversion step.
    /// </summary>
    public readonly struct ScreenPoint
    {
        public readonly float X;
        public readonly float Y;

        public ScreenPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static ScreenPoint operator -(ScreenPoint a, ScreenPoint b) => new ScreenPoint(a.X - b.X, a.Y - b.Y);
        public static ScreenPoint operator +(ScreenPoint a, ScreenPoint b) => new ScreenPoint(a.X + b.X, a.Y + b.Y);
        public static ScreenPoint operator *(ScreenPoint a, float s) => new ScreenPoint(a.X * s, a.Y * s);

        public float Length => MathF.Sqrt(X * X + Y * Y);
    }

    /// <summary>Axis-aligned screen rectangle, bottom-left origin.</summary>
    public readonly struct LayoutRect
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public LayoutRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float MaxX => X + Width;
        public float MaxY => Y + Height;
        public ScreenPoint Center => new ScreenPoint(X + Width * 0.5f, Y + Height * 0.5f);

        public bool Contains(in ScreenPoint point)
            => point.X >= X && point.X <= MaxX && point.Y >= Y && point.Y <= MaxY;

        public bool Overlaps(in LayoutRect other)
            => X < other.MaxX && MaxX > other.X && Y < other.MaxY && MaxY > other.Y;
    }
}
