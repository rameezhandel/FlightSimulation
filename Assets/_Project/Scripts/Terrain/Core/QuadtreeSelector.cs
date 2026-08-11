using System;
using System.Collections.Generic;

namespace Cirrus.Terrain
{
    /// <summary>
    /// Distance-based quadtree LOD selection: a tile splits while the viewer is
    /// closer than SplitFactor times its size (and the max level allows), giving a
    /// ring structure of finer tiles around the viewer. Cracks between neighbouring
    /// levels are closed by mesh skirts (see TileMesher), so no explicit
    /// neighbour-level constraint is enforced — the standard mobile-friendly choice.
    ///
    /// Pure and deterministic: same inputs, same tile list, same order.
    /// </summary>
    public static class QuadtreeSelector
    {
        /// <summary>Split radius as a multiple of tile size. 2.5 keeps adjacent levels within one step in practice.</summary>
        public const float DefaultSplitFactor = 2.5f;

        /// <param name="viewer">Viewer position, region-local metres.</param>
        /// <param name="viewerHeight">Viewer height above the terrain surface, m (clamped ≥ 0).</param>
        /// <param name="maxLevel">Finest allowed level (leaf tiles).</param>
        /// <param name="results">Cleared and filled, depth-first south-west first.</param>
        public static void Select(
            in LocalNE viewer,
            float viewerHeight,
            int maxLevel,
            float splitFactor,
            List<TileAddress> results)
        {
            results.Clear();
            Visit(new TileAddress(0, 0, 0), viewer, MathF.Max(0f, viewerHeight), maxLevel, splitFactor, results);
        }

        static void Visit(
            in TileAddress tile,
            in LocalNE viewer,
            float viewerHeight,
            int maxLevel,
            float splitFactor,
            List<TileAddress> results)
        {
            if (tile.Level < maxLevel && ShouldSplit(tile, viewer, viewerHeight, splitFactor))
            {
                for (int cx = 0; cx < 2; cx++)
                    for (int cy = 0; cy < 2; cy++)
                        Visit(tile.Child(cx, cy), viewer, viewerHeight, maxLevel, splitFactor, results);
            }
            else
            {
                results.Add(tile);
            }
        }

        static bool ShouldSplit(in TileAddress tile, in LocalNE viewer, float viewerHeight, float splitFactor)
        {
            float horizontal = tile.DistanceTo(viewer);
            float distance = MathF.Sqrt(horizontal * horizontal + viewerHeight * viewerHeight);
            return distance < splitFactor * tile.Size;
        }
    }
}
