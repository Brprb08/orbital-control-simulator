[⬅ Back to README](./README.md)

# Technical Breakdown – Orbital Control Simulator

## Who This Is For

This document is intended for developers, tech leads, and simulation engineers who want a deeper look into the architecture, math, and design decisions behind the simulator. If you’re evaluating technical depth, performance strategy, or physics implementation, this breakdown is for you.

---

## Table of Contents

- [Motivation](#motivation)
- [Simulation Core](#simulation-core)
- [Features](#features)
- [Physics Breakdown](#physics-breakdown)
- [Interop Architecture](#interop-architecture)
- [Validation Results](#validation-results)
- [How To Run It](#how-to-run-it)
- [Planned Enhancements](#planned-enhancements)
- [Limitations](#limitations)
- [Repo & Setup](#repo--setup)

---

## Motivation

This project began as a personal exploration into orbital dynamics after being inspired by real-world launches and missions. It’s now an area for experimenting with numerical methods and optimizing real-time simulation performance using Unity and C++.

---

## Simulation Core

- RK4 numerical integrator ensures long-term stability of orbits
- Newtonian multi-body simulation with mutual gravitational attraction
- Thrust system allows for real-time orbital maneuvers
- GPU-rendered trajectories keep performance smooth
- All physics offloaded to a C++ DLL for max performance

---

## Features

- Custom satellite/object placement with initial velocity and mass
- Thrust controls (prograde, retrograde, radial, normal, etc.)
- Dynamic apogee/perigee/orbital period computation
- Adjustable time scaling (1x to 100x)
- Two camera modes: Track and Free
- Unity frontend for visualization and interaction

---

## Physics Breakdown

See [PHYSICS_BREAKDOWN.md](./PHYSICS_BREAKDOWN.md) for full math.

- Newton’s Law of Gravity:
  ```
   F = G * (m1 * m2) / r²
  ```
- RK4 integration applied to both position and velocity vectors
- Thrust modeled as instantaneous velocity delta based on burn direction and mass

---

## Interop Architecture

Unity calls into native C++ functions via platform invoke (`DllImport`), keeping heavy calculations outside the managed runtime.

Example structure:
```
- Unity initializes simulation state and time step  
- C++ function computes new positions and velocities  
- Unity receives updated data and visualizes it  
```

This setup reduces CPU load and avoids garbage collection overhead.

---

## Validation Results

Orbital mechanics accuracy was validated against both Keplerian predictions and NASA's GMAT tool using real-time RK4 integration. Tests assume simplified Newtonian gravity (no drag, no J2, no higher-order perturbations).

---

### Orbital Period Comparison

| Orbit Type                  | Reference Tool | Expected Period (avg) | Simulated Period (avg) | Accuracy   |
|-----------------------------|----------------|-------------------------------|--------------------------------|------------|
| LEO (408 km circular)       | Keplerian Calc | 92.74 min                     | 92.62 min                      | ~99.87%    |
| Elliptical (7000–20007 km)  | Keplerian Calc | 7.75 hrs                      | 7.88 hrs                       | ~98.32%    |

---

## How to Run It

### Requirements
- Unity 2020.3 or later (tested on LTS versions)
- Windows OS (for C++ DLL compatibility)

### Getting Started
1. Clone the repo:
```
git clone https://github.com/Brprb08/space-orbit-simulation.git
```
2. Open the project in Unity Hub
3. Load the scene: `Assets/Scenes/OrbitSimulation.unity`
4. Press `Play`

Controls:
```
- WASD / Right Mouse: Free camera navigation  
- Object Dropdown: Switch tracked body  
- Arrow keys: Apply thrust (prograde/retrograde/etc.)  
- R: Reset time scaling  
```

---

## Planned Enhancements

- Orbital burn planning interface (UI + node system)
- Delta-v and fuel mass modeling
- Add perturbation forces (J2, drag, solar pressure)
- Performance scaling using Barnes-Hut for large body counts

---

## Limitations

- No atmospheric drag or decay modeling
- Earth is fixed; no back-reaction from satellite mass
- No relativistic corrections
- Simplified collision behavior (non-elastic removal)

---

[⬆ Back to Top](#technical-breakdown--orbital-control-simulator)
