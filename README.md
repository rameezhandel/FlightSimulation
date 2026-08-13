# Cirrus

Bush flying through fjords and glaciers, with weather that actually matters, on a phone.

A study-level-ish flight simulator for iOS, set in Southeast Alaska — Juneau, Sitka,
Skagway, Haines, Gustavus, and the water and mountains between them. One region, one
aircraft, done properly.

> **Status: pre-alpha.** The simulation core is built and tested; the Unity project has
> never been opened in a real Unity Editor. Everything under `Assets/` compiles against a
> stub API only. Treat the Unity layer as unverified until that first editor pass happens
> (checklist: [`docs/decisions/0002`](docs/decisions/0002-minimal-unity-shell-procedural-m1-scene.md)).

---

## What's built

| Area | State |
|---|---|
| Flight model | Blade-element aero, post-stall airfoil, ground effect, slipstream, P-factor, windmilling drag. All four validation gates green. |
| Aircraft | DHC-2 Beaver (landplane). Float variant pending float performance data. |
| Terrain | Region projection, quadtree LOD with skirts, threaded meshing, floating origin, `.ctil` tile pyramid + reader, synthetic stand-in terrain. |
| Weather | Deterministic wind field: boundary-layer profile, gusts, turbulence. Calm/Breeze/Storm. |
| Navigation | Bearings, airports/runways, flight plans with cross-track, VOR/DME/ADF. |
| Tooling | Python DEM pipeline, flight recorder with deterministic replay, dev overlay, CI. |
| Rendering | Placeholder. No water system, no sky, no clouds — see *Known gaps*. |
| Touch controls | Virtual stick, throttle slider, flap/rudder buttons, tilt scheme, auto-coordination. Safe-area aware. |
| Product (M4) | Otherwise untouched. No cockpit, menus, flight planning UI, or save. |

The flight model matches published DHC-2 figures within the project's own tolerances:

| | Model | Published | Gate |
|---|---|---|---|
| Stall, full flap | 52.8 kt | 52.1 kt | ±3 kt |
| Cruise, 75% power | 122.3 kt | 124.3 kt | ±5 kt |
| Climb, sea level | 1038 fpm | 1020 fpm | ±100 fpm |
| Trimmed hands-off | stable | — | no drift |

Details and sources: [`docs/flight-model.md`](docs/flight-model.md).

---

## Running the tests (no Unity required)

The simulation core is plain C# with no Unity types, so it runs anywhere .NET does.

```bash
# 126 tests: aero, trim, terrain, navigation, weather, recorder
dotnet test tools/flightcore-tests

# 8 tests: DEM tile pipeline
cd tools/terrain-pipeline && python3 -m unittest test_pipeline
```

Both run in CI on every push, alongside a job that fails the build if `UnityEngine`
appears anywhere in the pure layer.

## Opening in Unity

Requires **Unity 6 LTS** (URP). First open needs a short setup pass — accept the version
upgrade, enable the new Input System backends, create and assign a URP asset. The full
checklist is in [`docs/decisions/0002`](docs/decisions/0002-minimal-unity-shell-procedural-m1-scene.md).

There are no committed scenes. Both test scenes build themselves: drop a single component
into an empty scene and press Play.

| Component | What you get |
|---|---|
| `M1Bootstrap` | Flat checkerboard ground, Beaver spawned trimmed at 300 m, chase camera, dev overlay. |
| `M2TerrainBootstrap` | Streaming Southeast Alaska terrain, PAJN runway, floating origin, wind, on an 8 km final for Juneau. |

**On a phone or tablet**, controls are touch: a floating thumb stick on the left (roll and
pitch), a throttle slider on the right, flap and rudder buttons, and optional auto-coordinated
rudder. Tilt steering is available as an alternative scheme. Nothing needs to be plugged in.

**In the editor**, the same build also accepts keyboard and gamepad: `W`/`S` or arrows pitch,
`A`/`D` roll, `Q`/`E` rudder, `LeftShift`/`LeftCtrl` throttle, `F`/`V` flaps, `R` dumps a
flight recording. The two input paths hand off automatically.

## Building the terrain data

The shipped terrain is synthetic until you run the pipeline over real elevation data:

```bash
cd tools/terrain-pipeline
python3 build_tiles.py --dem ./dem --out ../../Assets/_Project/Data/Terrain
```

See [`tools/terrain-pipeline/README.md`](tools/terrain-pipeline/README.md) for the DEM
download recipe. The game falls back to synthetic terrain wherever tiles are missing, so a
partial pyramid still flies.

---

## How it's put together

The simulation is a **pure C# core** with **no Unity types anywhere** — aerodynamics,
terrain math, navigation, and weather are all plain structs over `System.Numerics`. Unity
only supplies the Rigidbody integration, rendering, and input, and there is exactly one
file that converts between the two coordinate frames.

That split is the reason a flight simulator's hardest logic can be unit-tested in under a
second without ever entering play mode, and why the aircraft can be flown headlessly in
tests to verify it trims, stalls, and holds altitude hands-off. Assembly definitions
enforce the boundary; CI enforces it again.

```
Assets/_Project/Scripts/
  Flight/       aero, engine, propeller, trim solver, 6-DOF test integrator   [pure]
  Aircraft/     Beaver definition [pure] + Rigidbody glue
  Terrain/      projection, quadtree, mesher, tile pyramid [pure] + streaming
  Navigation/   bearings, airports, flight plans, radio nav                   [pure]
  Atmosphere/   wind field, weather presets [pure] + controller
  Camera/ Core/ Debug/   chase rig, bootstraps, overlay, flight recorder
tools/
  flightcore-tests/   headless NUnit harness over the pure code
  terrain-pipeline/   Copernicus DEM -> .ctil tile pyramid
docs/
  flight-model.md     the aero math, with sources
  decisions/          one file per architectural decision
```

Full conventions, performance budget, and milestones: [`CLAUDE.md`](CLAUDE.md).

---

## Known gaps

Called out because they're real, not because they're forgotten:

- **Never compiled by Unity.** The single biggest risk in the project.
- **Water is a flat blue plane.** Fjords are ~40% of every frame here; they deserve a real
  system, and this deliberately isn't one yet.
- **Airport and navaid data is hand-entered and unverified** — placeholder values pending
  a proper import from OurAirports. Don't navigate by it.
- **No performance validation on device.** The frame budget is a plan, not a measurement.
- Touch controls are functional but visually plain — flat translucent shapes generated in
  code, not designed art. Feel needs tuning with a real thumb.
- Terrain meshing runs on ThreadPool tasks rather than Burst jobs, and terrain colouring is
  provisional elevation/slope bands. Both are tracked in
  [`docs/decisions/0003`](docs/decisions/0003-terrain-architecture-and-deviations.md).
- Beaver performance figures come from a public spec sheet, not a POH; several geometry
  constants are estimates tuned to hit published numbers.

## Data sources

All open or freely licensed. Attribution requirements must be honoured in-app before
release — the exact notices still need to be confirmed against each licence.

- **Copernicus DEM GLO-30** — elevation (© ESA / Airbus / DLR, free with attribution)
- **OurAirports** — airport and runway data (public domain)
- **ESA WorldCover** — landcover classification (CC BY 4.0)
