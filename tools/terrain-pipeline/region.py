"""Region definition and projection — the Python mirror of
Assets/_Project/Scripts/Terrain/Core/RegionProjection.cs. Keep the constants in
lockstep; RegionProjectionTests (C#) and test_pipeline.py (Python) both check
the same landmark values.
"""
from __future__ import annotations

import math

CENTER_LATITUDE = 58.25
CENTER_LONGITUDE = -135.0
SIZE = 280_000.0  # m
HALF_SIZE = SIZE / 2.0

_EARTH_RADIUS = 6_371_000.0
_M_PER_DEG_LAT = _EARTH_RADIUS * math.pi / 180.0
_M_PER_DEG_LON = _M_PER_DEG_LAT * math.cos(math.radians(CENTER_LATITUDE))


def to_local(latitude: float, longitude: float) -> tuple[float, float]:
    """(north, east) metres from the region centre."""
    return (
        (latitude - CENTER_LATITUDE) * _M_PER_DEG_LAT,
        (longitude - CENTER_LONGITUDE) * _M_PER_DEG_LON,
    )


def to_geo(north: float, east: float) -> tuple[float, float]:
    return (
        CENTER_LATITUDE + north / _M_PER_DEG_LAT,
        CENTER_LONGITUDE + east / _M_PER_DEG_LON,
    )


def tile_bounds(level: int, x: int, y: int) -> tuple[float, float, float, float]:
    """Region-local bounds (north_min, east_min, north_max, east_max) of a tile."""
    size = SIZE / (1 << level)
    north_min = -HALF_SIZE + x * size
    east_min = -HALF_SIZE + y * size
    return north_min, east_min, north_min + size, east_min + size
