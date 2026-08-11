# Flight model

The aero math behind `Assets/_Project/Scripts/Flight/`, with sources and known
limitations. Read together with CLAUDE.md §4, which states the non-negotiables.

## Layer boundaries

The flight core is plain C# over `System.Numerics` — no Unity types anywhere
(CLAUDE.md §4). It is exercised headlessly by `tools/flightcore-tests/` with
`dotnet test`, running the same NUnit test files Unity Test Framework runs
in-editor. Unity's role is limited to: feeding `RigidBodyState` in, applying the
returned per-surface forces via `Rigidbody.AddForceAtPosition` at 200 Hz, and
rendering.

### Frames and sign conventions

Defined once, in `docs/decisions/0001-pure-csharp-aero-core.md`:

- Body frame: **X out the nose, Y out the right wing, Z up**, right-handed.
- World frame: X/Y horizontal, Z up.
- Consequences: nose-up pitch torque is **−Y**, right-roll torque is **−X**,
  nose-right yaw torque is **+Z**. `FlightDynamicsTests` pins all of these.
- Angular velocity is body-frame. Orientation quaternion maps body → world.

## Blade elements (`AeroSurfaceDefinition`, `FlightDynamics`)

The wing is 10 strips (5 per side), the horizontal tail 2, plus the fin — each an
independent blade element with position, chord line, and normal in the body frame.
Per strip, per step:

1. Local velocity through the air: `v = v_body + ω × r` (rotation makes roll/pitch/yaw
   damping and spin asymmetry emergent rather than scripted).
2. Strip theory: only the chord-plane flow components (forward `vf`, normal `vu`)
   matter; spanwise flow is dropped. Local `α = atan2(−vu, vf)` — valid at any
   flow angle, which is what keeps deep stall and tail-slides finite.
3. Airfoil model evaluates `Cl, Cd, Cm` (below); induced drag is added per strip as
   `Cl²/(π·AR·e)` using the parent surface's aspect ratio.
4. Force = `q·S·(Cl·liftDir + Cd·dragDir)` applied at the strip's aerodynamic
   centre; section moment `q·S·c·Cm` about the span axis.

Finite-wing correction: every strip uses the lifting-line-corrected slope
`a = a0 / (1 + a0/(π·AR·e))` with the whole surface's AR (Anderson,
*Fundamentals of Aerodynamics*, lifting-line theory). This is the standard
real-time compromise: per-strip induced downwash is not computed.

## Airfoil model (`AirfoilModel`)

Parametric, two regimes, blended with a smoothstep over ±`StallBlendRange`:

- **Attached**: `Cl = a·(α − α0)`, parabolic profile drag, constant `Cm` about the
  quarter chord.
- **Separated**: flat plate, `Cn = Cd90·sin α`, so `Cl = Cn·cos α`,
  `Cd = Cd90·sin²α + Cd_min`, `Cm = −0.25·Cn` (centre of pressure near mid-chord).
  Simplified from Viterna & Corrigan (NASA CP-2230, 1982) as commonly used in
  real-time sims.

The blend guarantees the CLAUDE.md §4 requirement: Cl rises to a maximum at the
stall angle then **drops**, before partially recovering toward 45° the way a flat
plate does. `AirfoilModelTests.LiftDropsAfterStall` pins this.

Controls and flaps are plain-flap theory: deflection δ shifts the zero-lift angle
by `τ·δ` (τ = `FlapEffectiveness`), lowers the stall angle by a fraction of that,
adds `ΔCm ≈ −0.25·ΔCl` and quadratic drag.

## Propulsion (`Propulsion.cs`)

- Engine: quasi-static, `P = P_rated · throttle · lapse`, lapse per Gagg & Ferrar:
  `(σ − 0.117)/0.883`.
- Propeller: two asymptotes blended by a p-norm (n = 3):
  static `T0 = FoM·(2ρA)^⅓·P^⅔` (momentum theory × figure of merit) and
  in-flight `T = η_max·P/V`.
- Reaction torque `P/ω_rated` about the prop axis: with US-rotation engines this
  is a **left-rolling** tendency, and it is why the trim solver carries lateral trim.

## Trim & performance validation (`TrimSolver`, `BeaverValidationTests`)

Quasi-static analysis driven by the full blade-element model (never closed-form
shortcuts): Newton iteration with numeric Jacobians solves level trim
(θ, elevator, throttle, then aileron/rudder); stall speed is the lowest speed at
which moment-trimmed maximum lift carries the weight; climb uses the
excess-thrust method; cruise is the speed where required throttle equals the
target power fraction.

Current measured values, DHC-2 Beaver landplane at MTOW (gates per CLAUDE.md §4):

| Quantity | Model | Published | Gate |
|---|---|---|---|
| Stall, full flap | 52.8 kt | 52.1 kt (60 mph) | ±3 kt |
| Stall, clean | 65.2 kt | (no verified figure; bracketed 55–68 kt) | — |
| Cruise, 75% power | 122.8 kt | 124.3 kt (143 mph) | ±5 kt |
| Best climb, sea level | 1045 fpm | 1020 fpm | ±100 fpm |

Published figures are the Wikipedia DHC-2 spec sheet (landplane) pending POH
verification — see `[W]`/`[EST]` source tags in `BeaverDefinition`. Tuning rule:
adjust `[EST]` constants only; `[W]` targets are never touched.

## Known limitations (accepted for now, revisit before M1 closes)

- **No propeller slipstream over wing/tail, no P-factor, no windmilling drag** —
  the prop is a point thrust plus reaction torque. Planned: blade-element prop.
- **No ground effect** — needs terrain height input; wire up alongside the
  ground-plane collision work.
- **Quasi-static engine** — no RPM dynamics, mixture, or carb ice.
- **No aileron droop / flaperons** (the real Beaver droops ailerons with flaps).
- **Fixed CG, diagonal inertia tensor** (Ixz dropped); loading/fuel-burn later.
- **Landplane, not floats** — the reference aircraft is the float variant, but
  reliable public performance data is landplane; float drag + mass increments
  get added when float POH data is on hand.
- Fuselage is an equivalent flat-plate drag at the CG — no fuselage lift/side
  force (the fin currently carries all weathervane stability, which suffices).
