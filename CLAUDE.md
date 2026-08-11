# CLAUDE.md

Project context for Claude Code. Read this fully before making changes.

---

## 1. What we're building

**Working title:** Cirrus
**Genre:** Study-level-ish flight simulator for iOS. Realistic flight model, arcade-friendly onboarding.
**Platform:** iOS only. iPhone 13 / A15 and newer, plus iPad. No Android, no desktop, no Vision Pro.
**Team:** One experienced developer. Assume no artist, no QA, no second programmer.
**Business model:** Premium base app (~$9.99), aircraft sold as IAP packs later. No ads, no energy timers.

**Region:** Southeast Alaska only (Juneau, Sitka, Skagway, Haines, Gustavus). Roughly 250 km x 250 km.
We are explicitly NOT building a global sim. Do not add code paths that assume worldwide coverage.

**The pitch in one line:** bush flying through fjords and glaciers, with weather that actually
matters, on a phone.

---

## 2. Tech stack

| Layer | Choice | Notes |
|---|---|---|
| Engine | Unity 6 LTS | URP, Forward+ |
| Language | C# | Nullable enabled, `LangVersion` latest supported |
| Rendering | URP 17.x | No HDRP. No custom SRP. |
| Physics | Custom aero, Unity Rigidbody for integration | See §4 |
| Terrain | Custom quadtree streaming | NOT Unity Terrain. See §5 |
| Input | Unity Input System (new) | Not legacy `Input.GetAxis` |
| Data | Copernicus DEM 30m, OurAirports CSV, ESA WorldCover | All open/free licensed |
| Build | Xcode 16+, Metal only | No OpenGL ES fallback |

**Dependency policy:** every new package needs a written justification in `docs/decisions/`.
Prefer writing 200 lines over adding a dependency. Asset Store packages are allowed for
*art* but not for *systems*.

---

## 3. Repository layout

```
Assets/
  _Project/
    Scripts/
      Flight/          aerodynamics, engine, control surfaces
      Aircraft/        per-aircraft configs + MonoBehaviour glue
      Terrain/         quadtree, tile streaming, mesh generation
      Atmosphere/      sky, clouds, scattering, weather state
      Navigation/      airports, runways, radio nav, waypoints
      Camera/          chase, cockpit, cinematic rigs
      UI/              menus, HUD, instrument rendering
      Core/            service locator, save system, settings
      Debug/           dev overlays, flight recorder, cheats
    Shaders/
    Data/              StreamingAssets-bound terrain + airport data
    Prefabs/
    Scenes/
  Plugins/
docs/
  decisions/           one markdown file per architectural decision
  flight-model.md      the aero math, with sources
tools/                 Python data-prep pipeline (DEM -> tiles)
```

`_Project` prefix keeps our code sorted above imported assets. Keep it.

---

## 4. Flight model — the thing that must not be compromised

Blade-element theory, not lookup tables. The wing is divided into discrete sections; each
computes its own local airflow, angle of attack, lift, and drag. This gets us stalls,
spins, ground effect, and asymmetric behavior emergently instead of scripted.

**Non-negotiables:**
- Fixed timestep for aerodynamics: **200 Hz** (`Time.fixedDeltaTime = 0.005f`). Aero is
  stiff; 50 Hz produces oscillation on a light aircraft. Rendering stays decoupled.
- Forces applied via `Rigidbody.AddForceAtPosition` per surface, never a single lumped force.
- Lift coefficient curve must go **past** the stall angle and come back down. A curve that
  clamps at CLmax means the aircraft can never stall, which defeats the entire project.
- All aero math in SI units, in a plain C# struct-based layer with **no Unity types**, so it
  is unit-testable without entering play mode. Convert at the MonoBehaviour boundary.
- Every aero constant carries a source comment (aircraft POH, NACA report, or JSBSim config).

**Validation before any flight-model change is considered done:**
- Stall speed within 3 knots of the real aircraft's published Vs0/Vs1.
- Cruise speed at 75% power within 5 knots of published.
- Climb rate at sea level within 100 fpm of published.
- Aircraft flies hands-off straight and level when trimmed. Any persistent roll or pitch
  drift is a bug, not a feature.

Reference aircraft #1: **de Havilland Beaver DHC-2** on floats. Bush aircraft, forgiving,
regionally correct, and its performance data is public.

---

## 5. Terrain

Custom quadtree LOD over a fixed geographic bounding box. Not Unity Terrain — it does not
stream at the scale or speed we need, and it assumes a flat world.

- Source DEM is preprocessed offline by `tools/` into a tile pyramid shipped in
  StreamingAssets. **Never** fetch terrain at runtime from a network service.
- Tile meshes generated on worker threads, uploaded on main thread. Use the Jobs system.
  Geometry generation must never block the render thread.
- Origin rebasing every 5 km of travel to avoid float precision breakdown. Every system
  that caches world positions must subscribe to the rebase event. This is the single
  easiest way to introduce subtle, awful bugs — treat it seriously.
- Ground texturing is **procedural from landcover classification**, not satellite imagery.
  Rock, snow, glacier, conifer, tidal flat, water — blended by slope, altitude, and class.
- Water is a separate system. Fjords and sea are ~40% of every frame in this region;
  they deserve real work, not a blue plane.

---

## 6. Performance budget

Target: **60 fps on iPhone 14**, sustained for a 20-minute flight without thermal collapse.
Test on a real device that has already been warm for ten minutes. Simulator numbers are lies.

Per-frame budget at 60 fps (16.6 ms):

| System | Budget |
|---|---|
| Aerodynamics + physics | 2.0 ms |
| Terrain streaming/meshing | 1.5 ms (main thread only) |
| Rendering (CPU) | 5.0 ms |
| Atmosphere/clouds | 3.0 ms |
| UI + instruments | 1.5 ms |
| Headroom | 3.6 ms |

Hard limits: draw calls under 400. Triangles under 900k. Zero per-frame heap allocation in
flight — the GC alarm is a stutter at 200 knots.

If a change costs more than 0.3 ms, say so in the summary. Do not silently spend the budget.

---

## 7. Code conventions

- Async over coroutines. `Awaitable` or UniTask, never `IEnumerator` for new work.
- No singletons except one service locator in `Core/`. Inject dependencies.
- No `Find`, `FindObjectOfType`, `SendMessage`, or string-based lookups at runtime.
- `[SerializeField] private` over public fields.
- ScriptableObjects for all tuning data (aircraft configs, weather presets, terrain materials).
  A designer — future you at 2am — must be able to tune without recompiling.
- Naming: `_camelCase` private fields, `PascalCase` everything else.
- Comments explain *why*. The code already says what.

---

## 8. Testing

- Unit tests are **required** for `Flight/`, `Navigation/`, and the terrain quadtree math.
  These are pure, deterministic, and where the real bugs hide.
- Do not write play-mode tests for rendering or UI. They are slow, brittle, and low value here.
- The flight recorder in `Debug/` captures full state at 10 Hz and replays deterministically.
  Any reported flight-model bug gets a recorded case in `Assets/_Project/Tests/Recordings/`
  before it gets a fix.

---

## 9. Working agreement with Claude Code

**Do:**
- Read the relevant existing code before writing new code. This codebase has opinions.
- Propose a short plan before large changes; wait for approval on anything touching
  the flight model, terrain streaming, or origin rebasing.
- Keep changes tight and reviewable. One concern per commit.
- Write the test first for anything in `Flight/` or `Navigation/`.
- Tell me when a request conflicts with the performance budget or a decision in
  `docs/decisions/` — do not quietly work around it.
- Say plainly when you are uncertain about aero math or a Unity API rather than
  producing plausible-looking code. A wrong lift equation is worse than a question.

**Don't:**
- Don't add packages, plugins, or Asset Store dependencies without asking.
- Don't scope-creep toward global terrain, multiplayer, or additional aircraft. The
  answer for now is always "one region, one aircraft, done properly."
- Don't refactor unrelated code while fixing something.
- Don't use `Debug.Log` in per-frame paths.
- Don't add a settings toggle to avoid making a decision.

**Commits:** conventional commits (`feat:`, `fix:`, `perf:`, `refactor:`, `test:`).
Include the measured frame-time delta in the body for anything in `perf:`.

---

## 10. Milestones

**M1 — It flies (weeks 1–4).**
Flat infinite ground plane, no art. Beaver flight model, blade-element aero, control
surfaces, engine, propeller. Chase camera. Dev overlay showing airspeed, altitude, AoA,
per-surface forces. Gate: it stalls correctly, and flying it for ten minutes is enjoyable.

**M2 — It has a world (weeks 5–10).**
Terrain pipeline, quadtree streaming, origin rebasing, procedural landcover texturing,
water. One airport (Juneau, PAJN) with a real runway. Gate: fly Juneau to Skagway with no
hitches and no precision artifacts.

**M3 — It has a sky (weeks 11–15).**
Atmospheric scattering, volumetric-hybrid clouds, time of day, weather state with wind,
turbulence, and visibility. Gate: flying into a cloud is the best moment in the app.

**M4 — It's a product (weeks 16–24).**
Cockpit view with functioning instruments, menus, flight planning, settings, save/resume,
touch control schemes (tilt, virtual stick, slider), tutorial, App Store build.

Everything else — additional aircraft, more regions, multiplayer, failures, ATC — is
post-launch. Do not start it early.

---

## 11. Current status

M1 in progress. The flight core was built first, test-driven and headless
(`tools/flightcore-tests/`, run with `dotnet test` — 36 tests green).

Done:
- Blade-element aero core in `Scripts/Flight/` (ISA atmosphere, post-stall
  airfoil, strip forces, engine/prop, 6-DOF test integrator, trim solver).
  Pure C#, no Unity types; frame conventions in `docs/decisions/0001`.
- Beaver (landplane) definition, all four §4 validation gates green: stall
  52.8 kt (pub. 52.1), cruise 122.8 kt (pub. 124.3), climb 1045 fpm (pub.
  1020), hands-off stable when trimmed.
- Minimal Unity shell (manifest, 200 Hz TimeManager, asmdefs enforcing core
  purity) + MonoBehaviour glue: `FlightBody` (per-surface AddForceAtPosition),
  `CoreFrame` (the one Unity<->core conversion), dev input, chase camera, dev
  overlay, procedural M1 test scene (`M1Bootstrap`). See `docs/decisions/0002`.
- Flight recorder (§8): 10 Hz full-state ring buffer in-app (press R to dump),
  versioned binary format, deterministic headless replay via `FlightReplay`;
  repro workflow documented in `Tests/Recordings/README.md`. The Unity glue
  additionally passes a stub-API compile check, but has still never been
  compiled by a real Unity Editor.

Next (needs the Unity Editor — follow the first-open checklist in decision
0002, then commit the generated metas):
- First in-editor flight; run `Tests/EditMode/Unity` conversion tests in the
  Test Runner; assign the URP asset.
- Then: handling-quality tuning against the M1 gate ("stalls correctly, fun
  for ten minutes"), and the flight-model gaps in docs/flight-model.md
  §limitations (slipstream, P-factor, ground effect, float variant).

*(Keep this section updated. It is the first thing read in every new session.)*
