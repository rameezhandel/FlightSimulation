using System;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Floating-origin bookkeeping (CLAUDE.md §5): Unity world coordinates are only
    /// ever a local window into the 280 km region, and this tracks — in doubles —
    /// where that window's origin sits in region coordinates. When the focus object
    /// strays more than RebaseDistance from the Unity origin, Rebase() recentres the
    /// window on it and reports the shift every Unity-side object must apply.
    ///
    /// The invariant that matters (and is unit-tested): a point's REGION position
    /// never changes across a rebase — only its Unity-local coordinates do.
    /// </summary>
    public sealed class OriginTracker
    {
        /// <summary>Rebase every 5 km of travel (CLAUDE.md §5).</summary>
        public const float RebaseDistance = 5000f;

        double _originNorth;
        double _originEast;

        public OriginTracker(in LocalNE initialOrigin)
        {
            _originNorth = initialOrigin.North;
            _originEast = initialOrigin.East;
        }

        /// <summary>Region coordinates of the Unity world origin.</summary>
        public LocalNE Origin => new LocalNE((float)_originNorth, (float)_originEast);

        /// <summary>Convert a Unity-local horizontal position (north/east metres from the Unity origin) to region coordinates.</summary>
        public LocalNE ToRegion(float localNorth, float localEast)
            => new LocalNE((float)(_originNorth + localNorth), (float)(_originEast + localEast));

        /// <summary>Convert region coordinates to Unity-local north/east.</summary>
        public (float north, float east) ToLocal(in LocalNE region)
            => ((float)(region.North - _originNorth), (float)(region.East - _originEast));

        public bool NeedsRebase(float localNorth, float localEast)
            => localNorth * localNorth + localEast * localEast > RebaseDistance * RebaseDistance;

        /// <summary>
        /// Recentre the origin on the given local position. Returns the shift that
        /// must be ADDED to every Unity object's local position (i.e. minus the
        /// focus's old local coordinates).
        /// </summary>
        public (float shiftNorth, float shiftEast) Rebase(float localNorth, float localEast)
        {
            _originNorth += localNorth;
            _originEast += localEast;
            return (-localNorth, -localEast);
        }
    }
}
