# 0003 — Terrain architecture, and two explicit deviations from §5

**Status:** accepted
**Date:** 2026-08-11

## Architecture

- **Region**: one 280 km square centred on (58.25 N, 135 W). CLAUDE.md says
  "roughly 250 km"; Sitka→Skagway alone spans 268 km, so 280 it is. All
  geodetic data enters through `RegionProjection` (equirectangular at centre
  latitude, ±4% east-west scale at the extremes, self-consistent throughout).
- **Quadtree**: distance-based split (`QuadtreeSelector`), root = whole region,
  leaf level 6 offline data (~34 m samples, matched to the 30 m DEM), streamer
  meshes to level 7. Cracks between LOD levels are closed by **mesh skirts**,
  not neighbour-level constraints — cheaper and simpler on mobile.
- **Height sources are pluggable** (`IHeightSource`): today a deterministic
  synthetic fjord generator (+ PAJN runway-pad decorator) so M2 is flyable
  immediately; the `.ctil` pyramid from `tools/terrain-pipeline` slots in
  without touching the streamer. The Python writer and C# reader share a
  committed golden fixture so the binary format cannot silently diverge.
- **Floating origin**: `OriginTracker` (pure, double-precision, tested) owns
  the bookkeeping; `FloatingOrigin` (Unity) shifts registered roots past 5 km
  and raises `Shifted`. The tested invariant: no rebase ever changes a point's
  *region* position.
- **Meshing** runs on worker threads via the pure `TileMesher` (positions
  corner-relative for float health, provisional landcover vertex colors);
  the main thread only copies finished arrays into `Mesh` objects, budgeted
  per frame. Leaf tiles get MeshColliders.

## Deviation 1: ThreadPool tasks instead of the Jobs system (§5)

The mesher is deliberately pure C# so it is headlessly testable — that rules
out `NativeArray`/Burst types in the core. Streaming currently dispatches that
pure code on ThreadPool tasks. Geometry generation still never blocks the
render thread, which is the intent behind the rule; what's lost is Burst's
throughput. **Follow-up (profiling-driven, in-editor):** port the inner
sampling loops to a Burst job that writes the same array layout, keeping the
pure version as the reference implementation for tests.

## Deviation 2: water is a placeholder plane (§5 explicitly forbids this)

CLAUDE.md: fjords "deserve real work, not a blue plane" — and the current
build ships exactly a blue plane, knowingly. A real water system (shader,
shore blending, reflection strategy fit for mobile) is iterative visual work
that needs the editor and a device; scheduling it as the first big M2
in-editor task rather than writing speculative shader code headlessly.
This deviation is temporary and tracked in CLAUDE.md §11.

## Also provisional

- Terrain colouring is per-vertex elevation/slope bands
  (`TileMesher.WriteLandcoverColor`) rendered by a URP vertex-colour shader
  with fallbacks; real procedural texturing from ESA WorldCover classes
  (per §5) comes with the landcover pipeline channel.
- `TerrainStreamer` file-backed source (StreamingAssets IO + tile cache +
  level resolution) is not yet written — synthetic source only.
