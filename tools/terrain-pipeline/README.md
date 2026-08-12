# Terrain data-prep pipeline

Copernicus GLO-30 DEM → the `.ctil` tile pyramid the game streams
(CLAUDE.md §5: preprocessed offline, shipped in StreamingAssets, never fetched
at runtime).

## Requirements

- Python 3.11+, `numpy`; `rasterio` only for the real build (tests run without it).

## Getting the DEM

Copernicus DEM GLO-30 (30 m, free, ESA licence — attribution required in-app)
is available without registration from the AWS Open Data mirror
(`s3://copernicus-dem-30m/`, no-sign-request) or via OpenTopography. The region
needs the 1×1° tiles covering **lat 56–60 N, lon 132–137 W** — e.g. with the
AWS CLI:

```
aws s3 cp --no-sign-request --recursive \
  s3://copernicus-dem-30m/ ./dem/ \
  --exclude "*" --include "Copernicus_DSM_COG_10_N5[6789]_00_W13[234567]_00_DEM/*"
```

(~40 tiles, ~2 GB. Water is 0 m in the source, which is exactly what the game
wants.)

## Build

```
python3 build_tiles.py --dem ./dem --out ../../Assets/_Project/Data/Terrain
```

Output: `tile_<level>_<x>_<y>.ctil`, levels 0–6. Leaf level 6 = 4096 tiles of
4.375 km at ~34 m sample spacing (matched to the 30 m source), int16
decimetres, ~180 MB total. Parent levels are exact 2:1 decimations of their
children, so LOD transitions never disagree about a shared sample.

## Tests

```
python3 -m unittest test_pipeline -v
```

`Assets/_Project/Tests/Fixtures/golden.ctil` is written by this pipeline and
read by the C# test `TilePyramidFormatTests.GoldenFileFromPythonPipelineReads` —
the two format implementations cannot silently diverge. Regenerate the fixture
if (and only if) the format version is bumped in **both** languages.

## Not yet done

- Landcover (ESA WorldCover) classification tiles for procedural texturing —
  same pyramid pattern, second channel; scheduled with the real terrain
  material work.
- The Unity-side file-backed `IHeightSource` that streams these tiles from
  StreamingAssets (the synthetic source is the stand-in until then).
