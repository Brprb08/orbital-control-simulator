[⬅ Back to TECHNICAL README](./TECHNICAL_README.md)

# Orbital Physics Breakdown

This doc explains how the simulation handles gravity, integration, thrust, and time scaling. Focus is on accurate short-term orbital mechanics using math-based systems, no external physics engine.

### Table of Contents
- [RK4 Integration](#rk4-integration)
- [Gravity Calculations](#gravity-calculations)
- [Thrust Mechanics](#thrust-mechanics)
- [Time Scaling](#time-scaling)
- [Limitations and Plans](#limitations-and-plans)
- [Back to Top](#back-to-top)

---

### RK4 Integration

The sim uses Runge-Kutta 4th Order (RK4) for all motion updates. Euler was too unstable and inaccurate over time. Symplectic integrators conserve energy better for long-term sims, but RK4 gives better local accuracy per step, which matters more here.

RK4 was chosen for:
- Short-to-mid duration accuracy (maneuvers, transfers, burns)
- Real-time visualization without long-term drift being an issue
- Better precision during thrust events and fast-forwarded simulation

RK4 evaluates four derivatives and averages them to compute the next position and velocity.

RK4 Flow Per Frame:
```
   current_state
        │
     compute k1
        │
  estimate midpoint (k1)
        │
     compute k2
        │
  estimate midpoint (k2)
        │
     compute k3
        │
  estimate endpoint (k3)
        │
     compute k4
        │
    final update: state + (dt/6) * (k1 + 2k2 + 2k3 + k4)
```

---

### Gravity Calculations

Gravity follows Newton’s law:
```
F = G * (m1 * m2) / r^2
```

Every object calculates gravitational pull from every other object in the system. Acceleration is computed per-body and summed. Position and velocity are updated through RK4 using these force-derived accelerations.

Close approaches apply a minimum r threshold to prevent singularities and floating point errors.

---

### Thrust Mechanics

Thrust applies continuous acceleration in the chosen direction while input is active.

Available thrust directions:
- Prograde (along velocity vector)
- Retrograde (opposite velocity vector)
- Radial in / out (toward or away from central body)
- Normal up / down (inclination change)

Acceleration is computed as:
```
a = F / m
```

Mass is adjustable per object, and thrust strength is scaled accordingly. There's no fuel system yet, so thrust is unlimited during input.

---

### Time Scaling

User can increase time scale up to 100x. RK4 remains stable but with reduced accuracy as dt increases. Currently using fixed timestep per frame, but may switch to adaptive timesteps or offload RK4 to the GPU later.

---

### Limitations and Plans

Current limitations:
- No relativity, strictly Newtonian
- No drag model, so low orbits don’t decay
- No collision physics, objects are removed on impact
- Thrust is always available, no fuel use yet

Planned features:
- Maneuver planning UI like Kerbal
- Fuel-based delta-v tracking
- Earth as a dynamic object (currently static)
- Barnes-Hut optimization for scaling up body count

---

### Back to Top

[⬆ Back to Top](#orbital-physics-breakdown)
