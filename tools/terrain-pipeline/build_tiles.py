#!/usr/bin/env python3
"""Copernicus DEM -> Cirrus .ctil tile pyramid.

Usage:
    python3 build_tiles.py --dem <folder-of-GLO30-GeoTIFFs> --out <output-folder>

Input: Copernicus GLO-30 1x1-degree GeoTIFF tiles covering roughly
lat 56..60 N, lon 137..132 W (see README.md for the download recipe).
Output: tile_<level>_<x>_<y>.ctil for levels 0..LEAF_LEVEL, destined for
Assets/_Project/Data/Terrain/ (StreamingAssets-bound; terrain is never fetched
at runtime — CLAUDE.md §5).

Leaf level 6: 4096 tiles of 4.375 km, sample spacing ~34 m — matched to the
30 m source. Parent levels are exact 2:1 decimations of their children, so
every level's samples lie on the leaf grid (no resampling drift between LODs).

rasterio is imported lazily: only this script needs it, the unit tests
(test_pipeline.py) run on numpy alone.
"""
from __future__ import annotations

import argparse
import math
import pathlib
import sys

import numpy as np

import region
import tile_format

LEAF_LEVEL = 6
SAMPLES = tile_format.SAMPLES_PER_SIDE


def build_leaf_tile(dem, level: int, x: int, y: int) -> np.ndarray:
    """Sample the DEM mosaic on the tile's 129x129 grid. Row 0 = south edge."""
    north_min, east_min, north_max, east_max = region.tile_bounds(level, x, y)
    norths = np.linspace(north_min, north_max, SAMPLES)
    easts = np.linspace(east_min, east_max, SAMPLES)

    grid = np.empty((SAMPLES, SAMPLES), dtype=np.float64)
    for r, north in enumerate(norths):
        lats, lons = [], []
        for east in easts:
            lat, lon = region.to_geo(north, east)
            lats.append(lat)
            lons.append(lon)
        grid[r, :] = dem.sample_bilinear(np.array(lats), np.array(lons))
    return grid


def build_parent(children: dict[tuple[int, int], np.ndarray]) -> np.ndarray:
    """Exact decimation: the parent's 129x129 grid takes every second sample of
    its four children. Children keyed by (child_x_offset, child_y_offset) in
    {0,1}^2, x = north. Shared edges between children are identical by
    construction, so the stitch is seamless."""
    half = SAMPLES // 2  # 64
    parent = np.empty((SAMPLES, SAMPLES), dtype=np.float64)
    for (cx, cy), child in children.items():
        decimated = child[::2, ::2]  # (65, 65)
        parent[cx * half:cx * half + half + 1, cy * half:cy * half + half + 1] = decimated
    return parent


class DemMosaic:
    """Thin wrapper over rasterio for bilinear sampling across a folder of
    1-degree GeoTIFFs. Ocean (no tile / nodata) samples as 0 m."""

    def __init__(self, folder: pathlib.Path):
        import rasterio  # lazy: tests do not need it

        self._rasterio = rasterio
        self._datasets = []
        for path in sorted(folder.glob("**/*.tif")):
            self._datasets.append(rasterio.open(path))
        if not self._datasets:
            raise SystemExit(f"no .tif files under {folder}")

    def sample_bilinear(self, lats: np.ndarray, lons: np.ndarray) -> np.ndarray:
        out = np.zeros(lats.shape, dtype=np.float64)
        for i, (lat, lon) in enumerate(zip(lats, lons)):
            for ds in self._datasets:
                b = ds.bounds
                if b.left <= lon <= b.right and b.bottom <= lat <= b.top:
                    row, col = ds.index(lon, lat, op=float)
                    out[i] = self._bilinear(ds, row, col)
                    break
        return out

    def _bilinear(self, ds, row: float, col: float) -> float:
        r0, c0 = int(math.floor(row)), int(math.floor(col))
        tr, tc = row - r0, col - c0
        window = self._rasterio.windows.Window(c0, r0, 2, 2)
        block = ds.read(1, window=window, boundless=True, fill_value=0)
        if block.shape != (2, 2):
            return 0.0
        top = block[0, 0] * (1 - tc) + block[0, 1] * tc
        bottom = block[1, 0] * (1 - tc) + block[1, 1] * tc
        value = top * (1 - tr) + bottom * tr
        return float(0.0 if np.isnan(value) else value)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dem", type=pathlib.Path, required=True)
    parser.add_argument("--out", type=pathlib.Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    dem = DemMosaic(args.dem)

    # Leaf level, then decimate upward.
    tiles_at = {}
    count = 1 << LEAF_LEVEL
    print(f"building {count * count} leaf tiles at level {LEAF_LEVEL}...")
    level_tiles: dict[tuple[int, int], np.ndarray] = {}
    for x in range(count):
        for y in range(count):
            grid = build_leaf_tile(dem, LEAF_LEVEL, x, y)
            level_tiles[(x, y)] = grid
            (args.out / tile_format.tile_file_name(LEAF_LEVEL, x, y)).write_bytes(
                tile_format.encode_tile(LEAF_LEVEL, x, y, grid))
        print(f"  row {x + 1}/{count}", file=sys.stderr)
    tiles_at[LEAF_LEVEL] = level_tiles

    for level in range(LEAF_LEVEL - 1, -1, -1):
        child_tiles = tiles_at[level + 1]
        level_tiles = {}
        count = 1 << level
        for x in range(count):
            for y in range(count):
                children = {(cx, cy): child_tiles[(x * 2 + cx, y * 2 + cy)]
                            for cx in (0, 1) for cy in (0, 1)}
                grid = build_parent(children)
                level_tiles[(x, y)] = grid
                (args.out / tile_format.tile_file_name(level, x, y)).write_bytes(
                    tile_format.encode_tile(level, x, y, grid))
        tiles_at[level] = level_tiles
        del tiles_at[level + 1]
        print(f"level {level} done ({count * count} tiles)")

    print(f"pyramid written to {args.out}")


if __name__ == "__main__":
    main()
