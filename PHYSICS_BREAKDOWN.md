[⬅ Back to TECHNICAL README](./TECHNICAL_README.md)

# Orbital Physics Breakdown

This document outlines how the simulation models orbital mechanics, including gravity, integration, and thrust. The goal is to simulate accurate, real-time orbital behavior for short to mid-duration maneuvers using direct numerical methods, without relying on any external physics engine.

> **Note:** The simulation originally used RK4 (Runge-Kutta 4th Order), but has since transitioned to the Dormand–Prince 5(4) integrator (DOPRI5) for better error control and future support for adaptive stepping.

---

## Table of Contents
- [Integration Method: Dormand–Prince 5(4)](#integration-method-dormand–prince-54)
- [Numerics & Physics Details](#numerics--physics-details)
- [Units and Reference Frames](#units-and-reference-frames)
- [Atmospheric Drag Model](#atmospheric-drag-model)
- [Physics Flow with Drag](#physics-flow-with-drag)
- [Gravity Calculations](#gravity-calculations)
- [Thrust Mechanics](#thrust-mechanics)

---

## Integration Method: Dormand–Prince 5(4)

The simulation previously used Runge-Kutta 4th Order (RK4) for motion integration. RK4 was selected for its simplicity and high local accuracy, especially during short-duration events like burns and transfers. However, it lacked support for adaptive time stepping and error estimation, which limited its scalability and precision over variable time scales.

The new integrator is a fifth-order Dormand–Prince method with an embedded fourth-order estimate. This enables high accuracy while supporting future features like variable timesteps and GPU acceleration.

### Why the switch from RK4?
- RK4 offers good local accuracy but no built-in error control
- Dormand–Prince maintains precision while supporting adaptive methods
- Better handling of edge cases and long-duration simulations

Dormand–Prince evaluates seven stages per step, blending multiple estimates to form a more accurate and stable trajectory update.

### Dormand–Prince 5(4) Flow Per Frame:

```
   current_state (pos, vel)
           │
     ┌─────┴─────┐
     │ Compute k1│
     └─────┬─────┘
           ▼
  estimate intermediate state (k1)
           │
     ┌─────┴─────┐
     │ Compute k2│
     └─────┬─────┘
           ▼
  estimate intermediate state (k2)
           │
     ┌─────┴─────┐
     │ Compute k3│
     └─────┬─────┘
           ▼
           ...
           ▼
     ┌─────┴─────┐
     │ Compute k7│
     └─────┬─────┘
           ▼
 Combine all stages using weighted sum:
 final_state = pos + Σ(b[i] * kx[i])
               vel + Σ(b[i] * kv[i])

 (b[i] = 5th-order weights for position/velocity)
```
---

## Numerics & Physics Details

### Precision Strategy

The sim uses different numeric precisions depending on where the data is flowing and what level of accuracy is required.

| Quantity                     | Type     | Reason                                                            |
|-----------------------------|----------|----------------------------------------------------------------------|
| Orbital State (true pos/vel) | double3  | Prevents drift in long simulations, used for integration accuracy    |
| Unity Transform             | float    | Unity uses float natively, conversion applied for visualization      |
| GPU Trajectory Prediction   | float    | Optimized for performance, used for visual prediction only           |
| Integrator Internals        | double   | Dormand–Prince operates fully in double precision for stability      |

Different float types are intentional. The sim integrates in high precision, then converts to float for rendering or Unity interop. This avoids precision loss over long durations without impacting performance where it’s not critical.

### Integration Settings

- Step Size: Fixed
- Max Δt per substep: `0.002s`
- Time slicing based on Unity's `fixedDeltaTime`

No adaptive error controls are enabled yet, but the integrator code supports embedded 4th-order error estimation. This is a planned change.

### Edge-Case Handling

Several numerical protections are in place to prevent simulation blowups or instability.

- Division by zero guards (1e-20)
- Max force cap: `1e8 N`
- NaN checks each frame
- Earth collision = immediate removal
- Min mass cutoff = `1e-6 kg`

--- 

## Units and Reference Frames

The simulator assumes a consistent unit system and reference frame throughout.

| Dimension | Unit             | Reference Frame        |
|----------|------------------|------------------------|
| Length   | Kilometers (km)  | Earth-Centered Inertial (ECI) |
| Velocity | Kilometers/second (km/s) | ECI                        |
| Time     | Seconds (s)      | Unity time (scaled)    |
| Mass     | Kilograms (kg)   | Body mass (used in thrust and gravity) |

### Core Physical Constants and Body Parameters

These are the key physical constants and simulation parameters used in the orbital model. The simulation operates in a scaled unit system where **1 unit = 10 km**, and all internal physics calculations are performed in double precision.

| Parameter                   | Symbol        | Value              | Units (Sim / Real)    | Description                                                  |
|----------------------------|---------------|--------------------|------------------------|--------------------------------------------------------------|
| Gravitational Constant     | G             | ~6.674e-23         | units³·kg⁻¹·s⁻²        | Scaled for sim units (1 unit = 10 km); matches Newton’s law |
| Earth Mass                 | Mₑ            | 5.972e24           | kg                     | Real Earth mass                                              |
| Earth Radius               | Rₑ            | 637.8137 units (≈6378 km)                   | Used for collision detection and reference altitude          |
| Atmosphere Top             | —             | 50 units           | ~500 km                | Above this altitude, atmospheric drag is assumed negligible |
| Satellite Mass Range       | m_sat         | 500 – 500,000       | kg                     | Typical user-set mass for satellites                         |
| Satellite Radius Range     | r_sat         | 0.0001 – 0.1        | units (1m – 1 km)      | Used to compute cross-sectional area                        |
| Drag Coefficient           | C_d           | 2.2                 | unitless               | Standard default for bodies like satellites           |
| Cross-sectional Area       | A             | π·r² (derived)      | units²                 | Used in drag computation: A = πr²                            |

---

## Atmospheric Drag Model

The simulation includes a realistic atmospheric drag model based on empirical atmospheric density data. Drag force is computed using:

$$
F_{\text{drag}} = \frac{1}{2} C_d \rho v^2 A
$$

where:
- $C_d$ = Drag coefficient (user-defined per satellite)
- $rho$ = Atmospheric density (interpolated from standard atmospheric tables)
- $v$ = Satellite velocity relative to Earth’s rotating atmosphere
- $A$ = Cross-sectional area of the spacecraft

Atmospheric density decreases exponentially with altitude and is computed using a logarithmic interpolation of real atmospheric density data up to 500 km altitude. The Earth’s rotation is included to compute relative velocity accurately, which improves drag modeling, especially at low altitudes.

---

## Physics Flow with Drag:

1. Compute gravitational acceleration based on body interactions.
2. Add thrust acceleration if applicable.
3. Compute and add atmospheric drag acceleration:
   - Relative velocity to Earth's rotation is calculated.
   - Atmospheric density is determined at the current altitude.
   - Drag force is computed and translated into acceleration.
4. Update orbital state (position and velocity) using Dormand–Prince integrator steps.

Drag implementation significantly enhances realism, accurately modeling orbital decay especially prominent in low-Earth orbit scenarios.

---

## Gravity Calculations

Gravity follows Newton’s law:
```
F = G * (m1 * m2) / r^2
```

Each object computes gravitational acceleration based only on a central body (e.g., Earth). A minimum `r` threshold is applied to avoid singularities and floating-point blowups.

Acceleration is fed into the integrator (now DOPRI5) for position and velocity updates.

---

## Thrust Mechanics

Thrust applies continuous acceleration while user input is active. Available thrust directions:

- Prograde (along velocity)
- Retrograde (opposite velocity)
- Radial In/Out (toward/away from central body)
- Normal Up/Down (for inclination changes)

Acceleration is calculated using:
```
a = F / m
```

Thrust currently does not consume fuel. It is unlimited while input is active. Object mass is configurable, and thrust scales accordingly.

---

### Back to Top

[⬆ Back to Top](#orbital-physics-breakdown)
