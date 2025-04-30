[⬅ Back to TECHNICAL README](./TECHNICAL_README.md)

# Orbital Physics Breakdown

This document outlines how the simulation models orbital mechanics, including gravity, integration, thrust, and time scaling. The goal is accurate, real-time orbital behavior for short to mid-duration maneuvers using purely mathematical systems, no external physics engine.

> **Note:** The simulation originally used RK4 (Runge-Kutta 4th Order), but has since transitioned to the Dormand–Prince 5(4) integrator (DOPRI5) for better error control and future support for adaptive stepping.

---

### Integration Method: Dormand–Prince 5(4)

The simulation previously used Runge-Kutta 4th Order (RK4) for motion integration. RK4 was selected for its simplicity and high local accuracy, especially during short-duration events like burns and transfers. However, it lacked support for adaptive time stepping and error estimation, which limited its scalability and precision over variable time scales.

The new integrator is **Dormand–Prince 5(4)**, a fifth-order method with an embedded fourth-order estimate, often referred to as DOPRI5. This allows the simulation to maintain high accuracy while enabling future improvements like variable timesteps and GPU acceleration.

#### Why the switch from RK4?
- RK4 offers good local accuracy but no built-in error control
- Dormand–Prince maintains precision while supporting adaptive methods
- Better handling of edge cases and long-duration simulations

Dormand–Prince evaluates seven stages per step, blending multiple estimates to form a more accurate and stable trajectory update.

---

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

### Gravity Calculations

Gravity follows Newton’s law:
```
F = G * (m1 * m2) / r^2
```

Each object computes gravitational acceleration by summing contributions from every other body. A minimum `r` threshold is applied to avoid singularities and floating-point blowups.

Acceleration is fed into the integrator (now DOPRI5) for position and velocity updates.

---

### Thrust Mechanics

Thrust applies continuous acceleration while user input is active. Available thrust directions:

- Prograde (along velocity)
- Retrograde (opposite velocity)
- Radial In/Out (toward/away from central body)
- Normal Up/Down (for inclination changes)

Acceleration is calculated using:
```
a = F / m
```

Thrust does not consume fuel yet, it's unlimited during input. Object mass is configurable, and thrust scales accordingly.

---

### Back to Top

[⬆ Back to Top](#orbital-physics-breakdown)
