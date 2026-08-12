"""Unit tests for the data-prep pipeline. numpy only — no rasterio, no DEM data.
Run: python3 -m unittest test_pipeline -v
"""
from __future__ import annotations

import struct
import unittest

import numpy as np

import region
import tile_format
from build_tiles import build_parent


class TileFormatTests(unittest.TestCase):
    def test_round_trip(self):
        rng = np.random.default_rng(42)
        grid = rng.uniform(-240, 1700, (129, 129))
        blob = tile_format.encode_tile(6, 12, 60, grid)
        level, x, y, decoded = tile_format.decode_tile(blob)
        self.assertEqual((level, x, y), (6, 12, 60))
        # Quantized to 0.1 m.
        np.testing.assert_allclose(decoded, np.round(grid, 1), atol=0.051)

    def test_golden_byte_layout(self):
        """Pins the exact byte layout the C# reader expects."""
        grid = np.zeros((129, 129))
        grid[0, 0] = 1.0    # 10 decimetres, first sample
        grid[0, 1] = -2.5   # -25 decimetres, second sample
        blob = tile_format.encode_tile(3, 1, 2, grid)

        self.assertEqual(blob[:4], b"CTIL")
        self.assertEqual(struct.unpack_from("<i", blob, 4)[0], 1)   # version
        self.assertEqual(struct.unpack_from("<i", blob, 8)[0], 3)   # level
        self.assertEqual(struct.unpack_from("<i", blob, 12)[0], 1)  # x
        self.assertEqual(struct.unpack_from("<i", blob, 16)[0], 2)  # y
        self.assertEqual(struct.unpack_from("<h", blob, 20)[0], 10)
        self.assertEqual(struct.unpack_from("<h", blob, 22)[0], -25)
        self.assertEqual(len(blob), 20 + 129 * 129 * 2)

    def test_clipping(self):
        grid = np.full((129, 129), 99_999.0)
        blob = tile_format.encode_tile(0, 0, 0, grid)
        _, _, _, decoded = tile_format.decode_tile(blob)
        self.assertAlmostEqual(decoded[0, 0], 3276.7, places=3)


class ParentDecimationTests(unittest.TestCase):
    """Real pyramid children share their boundary rows/columns (they are sampled
    at identical coordinates by build_leaf_tile's linspace), so fixtures are cut
    from one 257x257 super-grid, which guarantees that invariant."""

    @staticmethod
    def _children_from_super(super_grid: np.ndarray) -> dict:
        assert super_grid.shape == (257, 257)
        return {
            (cx, cy): super_grid[cx * 128:cx * 128 + 129, cy * 128:cy * 128 + 129].copy()
            for cx in (0, 1) for cy in (0, 1)
        }

    def test_parent_is_exact_decimation_of_the_super_grid(self):
        rng = np.random.default_rng(7)
        super_grid = rng.uniform(-200, 1700, (257, 257))
        parent = build_parent(self._children_from_super(super_grid))
        self.assertEqual(parent.shape, (129, 129))
        np.testing.assert_array_equal(parent, super_grid[::2, ::2])

    def test_overlap_rows_agree_regardless_of_write_order(self):
        # Row/column 64 of the parent is written by two children; with the
        # shared-edge invariant both writes carry the same values.
        super_grid = np.arange(257 * 257, dtype=np.float64).reshape(257, 257)
        parent = build_parent(self._children_from_super(super_grid))
        np.testing.assert_array_equal(parent[64, :], super_grid[128, ::2])
        np.testing.assert_array_equal(parent[:, 64], super_grid[::2, 128])


class RegionTests(unittest.TestCase):
    def test_matches_csharp_landmarks(self):
        # Same checks as the C# RegionProjectionTests.
        north, east = region.to_local(region.CENTER_LATITUDE + 1.0, region.CENTER_LONGITUDE)
        self.assertAlmostEqual(north, 111_195, delta=500)
        self.assertAlmostEqual(east, 0.0, places=6)

        for lat, lon in [(58.3547, -134.5763), (57.0531, -135.33),
                         (59.46, -135.3144), (59.2358, -135.4453), (58.4133, -135.7378)]:
            n, e = region.to_local(lat, lon)
            self.assertLessEqual(abs(n), region.HALF_SIZE, f"({lat},{lon}) north")
            self.assertLessEqual(abs(e), region.HALF_SIZE, f"({lat},{lon}) east")

    def test_round_trip(self):
        lat, lon = region.to_geo(*region.to_local(58.3547, -134.5763))
        self.assertAlmostEqual(lat, 58.3547, places=6)
        self.assertAlmostEqual(lon, -134.5763, places=6)

    def test_tile_bounds_tile_the_region(self):
        n0, e0, n1, e1 = region.tile_bounds(0, 0, 0)
        self.assertEqual((n0, e0, n1, e1),
                         (-region.HALF_SIZE, -region.HALF_SIZE, region.HALF_SIZE, region.HALF_SIZE))
        n0, e0, n1, e1 = region.tile_bounds(6, 63, 63)
        self.assertAlmostEqual(n1, region.HALF_SIZE)
        self.assertAlmostEqual(e1, region.HALF_SIZE)


if __name__ == "__main__":
    unittest.main()
