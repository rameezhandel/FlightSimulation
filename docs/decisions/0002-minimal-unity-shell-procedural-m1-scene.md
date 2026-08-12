# 0002 — Minimal hand-authored Unity shell; M1 scene is procedural

**Status:** accepted
**Date:** 2026-08-11

## Decision

The Unity project shell is hand-authored down to the minimum Unity cannot
regenerate on its own:

- `Packages/manifest.json` — URP 17.x, Input System, Test Framework.
- `ProjectSettings/ProjectVersion.txt` — pinned to a Unity 6 LTS version; opening
  with a newer 6000.0.x patch and letting Unity upgrade is expected and fine.
- `ProjectSettings/TimeManager.asset` — `Fixed Timestep: 0.005` (the CLAUDE.md §4
  200 Hz aero rate). This file exists precisely so that setting cannot silently
  drift; do not let a Unity settings migration rewrite it to 0.02.
- `Assets/csc.rsp` — `-nullable:enable` for every Unity-compiled assembly.

Everything else (`ProjectSettings/*.asset` defaults, `Library/`, metas) is left
for Unity to generate on first open, then committed from that machine.

The M1 test scene is **procedural**: no committed `.unity` scene or prefabs.
`M1Bootstrap` (one component in an empty scene) builds ground plane, sun, chase
camera, dev overlay, and the placeholder Beaver at runtime, and spawns it
mid-air already trimmed via the core `TrimSolver`.

## Why

- Hand-authoring full `ProjectSettings/*.asset` or scene YAML means guessing
  serialized formats and GUIDs; a wrong guess corrupts the project silently.
  The three files above are tiny, stable formats worth owning; nothing else is.
- Scene/prefab assets need `.meta` GUIDs that only Unity assigns. Until the
  editor has touched the repo once, code-built scenes are the only version of
  the truth that can't desync.
- Spawning trimmed through `TrimSolver` means the in-engine aircraft starts in
  the exact state the headless hands-off test validates — any divergence
  between engine and core shows up immediately as a visible pitch/roll drift.

## Assembly layout (enforces CLAUDE.md §4 purity)

| asmdef | Folder | Engine refs |
|---|---|---|
| `Cirrus.Flight` | `Scripts/Flight/` | **none** (`noEngineReferences`) |
| `Cirrus.Aircraft.Definitions` | `Scripts/Aircraft/Definitions/` | **none** |
| `Cirrus.Runtime` | rest of `Scripts/` | Unity + InputSystem |
| `Cirrus.Tests.EditMode` | `Tests/EditMode/` | test runner |

The headless harness (`tools/flightcore-tests/`) links only the two pure folders
plus `Tests/EditMode/Flight/`; Unity-only tests go in `Tests/EditMode/Unity/`.

## First-open checklist (once, on the dev machine)

1. Open with Unity 6 LTS; accept the version upgrade if prompted.
2. Say **Yes** when the Input System package asks to enable the new input
   backends (editor restarts).
3. Create the URP pipeline asset (Assets → Create → Rendering → URP Asset) and
   assign it in Project Settings → Graphics. Until then the built-in pipeline
   renders the M1 scene — acceptable for M1, not beyond.
4. New empty scene → add `M1Bootstrap` to any GameObject → Play. Save as
   `Assets/_Project/Scenes/M1TestFlight.unity`.
5. Run the edit-mode tests in the Test Runner (includes the frame-conversion
   suite that cannot run headlessly).
6. Commit the Unity-generated `.meta` files and settings.
