[⬅ Back to README](./README.md)

# Satellite Maneuver Simulator - Unity + C++

A real-time orbital mechanics simulator with RK4 integration, full N-body Newtonian gravity, thrust maneuvering, and GPU-drawn trajectories. Physics is offloaded to a native C++ DLL for better performance and control.

---

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

This project began as a personal exploration into orbital dynamics after being inspired by real-world launches and missions. It’s now a hands-on testbed for building simulation systems, digging into numerical integration, and optimizing real-time systems using Unity and native C++.

---

## Simulation Core

- RK4 numerical integrator ensures long-term stability of orbits
- Newtonian N-body simulation with mutual gravitational attraction
- Thrust system allows for real-time orbital maneuvers
- GPU-rendered trajectories keep performance smooth
- All physics offloaded to a C++ DLL for max performance

---

## Features

- Real-time N-body orbital dynamics
- Custom satellite/object placement with initial velocity and mass
- Thrust controls (prograde, retrograde, radial, normal, etc.)
- Dynamic apogee/perigee/orbital period computation
- Adjustable time scaling (1x to 100x)
- Two camera modes: Track and Free
- Unity frontend for visualization and interaction

---

## Physics Breakdown

See [NBODY_PHYSICS_RK4.md](./NBODY_PHYSICS_RK4.md) for full math.

- Newton’s Law of Gravity:
  ```
   F = G * (m1 * m2) / r²
  ```
- RK4 integration applied to both position and velocity vectors
- Thrust modeled as instantaneous velocity delta based on burn direction and mass

---

## Interop Architecture

Unity calls into native C++ functions via platform invoke (`DllImport`). The physics core runs outside of Unity’s managed runtime.

Example structure:
```
- Unity initializes simulation state and time step  
- C++ function computes new positions and velocities  
- Unity receives updated data and visualizes it  
```

This setup reduces CPU load and avoids garbage collection overhead.

---

## Validation Results

Accuracy tested against theoretical Keplerian predictions:

| Orbit Type | Expected Period | Measured | Accuracy |
|------------|----------------|----------|----------|
| LEO (408 km) | 92.74 min | 92.62 min | 99.87% |
| Elliptical (7000–20007 km) | 7.75 hrs | 7.88 hrs | 98.32% |

Timing captured through high-speed simulation and stopwatch measurement. No atmospheric drag or J2 modeling applied yet.

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

## Repo & Setup

```
git clone https://github.com/Brprb08/space-orbit-simulation.git
```

Open in Unity Hub, load the main scene, and start the simulation.

---

[⬆ Back to Top](#satellite-maneuver-simulator---unity--c)
