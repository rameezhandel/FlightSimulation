# 0001 — Pure-C# aero core on System.Numerics, tested headlessly

**Status:** accepted
**Date:** 2026-08-11

## Decision

The flight model lives in `Assets/_Project/Scripts/Flight/` as plain C# with
**no Unity types**, using `System.Numerics` vectors/quaternions, and is tested
headlessly by a hand-authored .NET 8 project (`tools/flightcore-tests/`) that
compiles the same source files by link and runs the same NUnit tests Unity Test
Framework will run in-editor.

## Frame convention (the part everyone must know)

- **Body frame: X out the nose, Y out the right wing, Z up. Right-handed.**
- World frame: Z up, X/Y horizontal.
- Derived signs: nose-up torque −Y, right-roll torque −X, nose-right torque +Z.
- Angular velocity is expressed in the body frame. Orientation maps body → world.
- Quaternion integration uses an explicitly written Hamilton product
  (`MathUtil.Derivative`) so nothing depends on a library's operator convention.

This is *not* Unity's left-handed X-right/Y-up/Z-forward frame. The MonoBehaviour
glue (M1, once the Unity project shell exists) owns the conversion in both
directions, in exactly one file, with tests. Never convert ad hoc elsewhere.

## Why

- CLAUDE.md §4 requires the aero math to be unit-testable without play mode; SI
  units; no Unity types. A right-handed frame lets us use textbook flight-dynamics
  math without sign gymnastics, and `System.Numerics` is available inside Unity 6
  (netstandard 2.1), so the same files compile in both worlds.
- A dotnet-SDK test loop runs in ~2 s and in CI without a Unity license.
- NUnit is the shared denominator: Unity Test Framework is NUnit-based, so test
  files are written once. The harness pins `LangVersion` 9 to stay within Unity 6's
  compiler.

## Consequences / rules

- No `UnityEngine` usings under `Scripts/Flight/` or `Scripts/Aircraft/` — the
  headless csproj enforces this by failing to compile.
- `tools/flightcore-tests/*.csproj` is hand-authored and committed (the .gitignore
  carves it out of the Unity-generated `*.csproj` ignore rule).
- The 6-DOF integrator in the core (`SixDofSimulator`) exists for tests and
  flight-recorder replay only; in-app integration stays on the Unity Rigidbody.
- No new dependencies were added: the harness uses NUnit + the test SDK only,
  in the test project only. The shipped code depends on nothing.
