"""Binary .ctil tile writer/reader.

MUST stay byte-compatible with the C# reader
(Assets/_Project/Scripts/Terrain/Core/TilePyramidFormat.cs). The committed golden
fixture (Assets/_Project/Tests/Fixtures/golden.ctil) plus the C# test
TilePyramidFormatTests.GoldenFileFromPythonPipelineReads pin the layout — if you
change anything here, regenerate the fixture and bump FORMAT_VERSION in BOTH
languages.

Layout (little-endian):
    uint32  magic "CTIL" (0x4C495443)
    int32   format version (1)
    int32   level
    int32   x   (tile index along north, 0 = south edge)
    int32   y   (tile index along east, 0 = west edge)
    int16 x 129*129  elevation in decimetres, row-major with row 0 = SOUTH edge
                     (row r, column c) = sample r*129 + c, matching the C# reader.
"""
from __future__ import annotations

import struct

import numpy as np

MAGIC = 0x4C495443
FORMAT_VERSION = 1
SAMPLES_PER_SIDE = 129
METERS_PER_UNIT = 0.1

_HEADER = struct.Struct("<Iiiii")


def encode_tile(level: int, x: int, y: int, elevation_meters: np.ndarray) -> bytes:
    """elevation_meters: float array (129, 129), row 0 = SOUTH edge."""
    if elevation_meters.shape != (SAMPLES_PER_SIDE, SAMPLES_PER_SIDE):
        raise ValueError(f"expected {SAMPLES_PER_SIDE}x{SAMPLES_PER_SIDE}, got {elevation_meters.shape}")
    decimeters = np.clip(np.round(elevation_meters / METERS_PER_UNIT), -32768, 32767).astype("<i2")
    return _HEADER.pack(MAGIC, FORMAT_VERSION, level, x, y) + decimeters.tobytes()


def decode_tile(blob: bytes) -> tuple[int, int, int, np.ndarray]:
    magic, version, level, x, y = _HEADER.unpack_from(blob, 0)
    if magic != MAGIC:
        raise ValueError("not a Cirrus terrain tile")
    if version != FORMAT_VERSION:
        raise ValueError(f"unsupported tile version {version}")
    samples = np.frombuffer(blob, dtype="<i2", offset=_HEADER.size)
    grid = samples.reshape(SAMPLES_PER_SIDE, SAMPLES_PER_SIDE).astype(np.float64) * METERS_PER_UNIT
    return level, x, y, grid


def tile_file_name(level: int, x: int, y: int) -> str:
    return f"tile_{level}_{x}_{y}.ctil"
