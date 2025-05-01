[⬅ Back to README](./README.md)

# Technical Breakdown – Orbital Control Simulator

## Who This Is For

This document is intended for developers, tech leads, and simulation engineers who want a deeper look into the architecture, math, and design decisions behind the simulator. If you’re evaluating technical depth, performance strategy, or physics implementation, this breakdown is for you.

---

## Table of Contents

- [Motivation](#motivation)
- [Simulation Core](#simulation-core)
- [Validation Results](#validation-results)
- [Physics Breakdown](#physics-breakdown)
- [Features](#features)
- [Interop Architecture](#interop-architecture)
- [How To Run It](#how-to-run-it)
- [Planned Enhancements](#planned-enhancements)
- [Limitations](#limitations)

---

## Motivation

This project began as a personal exploration into orbital dynamics after being inspired by real-world launches and missions. It’s now an area for experimenting with numerical methods and optimizing real-time simulation performance using Unity and C++.

---

## Simulation Core

- **Integrator:** Dormand–Prince 5(4), providing high-accuracy orbital state propagation
- **Dynamics:** Newtonian multi-body gravitational interactions
- **Thrust Model:** Real-time continuous acceleration (or impulse burns)
- **GPU Rendering:** Efficient trajectory visualization
- **Performance:** Physics fully offloaded to native C++ DLL

---

## Validation Results

Orbital mechanics accuracy was validated against both Keplerian predictions and long term orbital stability. Tests assume simplified Newtonian gravity (no drag, no J2, no higher-order perturbations). Initial development intentionally prioritized numerical accuracy in this idealized case before introducing additional perturbative forces like atmospheric drag, J2 oblateness, or solar pressure.

---

### Long-Term Orbital Stability (LEO, No Drag)

> Validation was performed at **100× time scale** over a simulated duration of **~77.3 hours** (50 full LEO orbits).
> Apogee and perigee were sampled at **each orbital extremum crossing**, not per frame, to ensure precise evaluation of orbital drift.

| Orbit # | Apogee (km) | Perigee (km) |
|---------|-------------|--------------|
| 1       | 421.551     | 408.198      |
| 10      | 421.549     | 408.197      |
| 20      | 421.550     | 408.189      |
| 30      | 421.549     | 408.189      |
| 40      | 421.546     | 408.188      |
| 50      | 421.526     | 408.188      |

#### Apogee & Perigee Drift Summary

| Metric        | Initial Value (km) | Final Value (km) | Total Drift (km) | Drift per Orbit (km) | Error %      |
|---------------|--------------------|------------------|------------------|-----------------------|--------------|
| Apogee        | 421.551            | 421.526          | -0.025           | -0.0005               | ~0.0059%     |
| Perigee       | 408.200            | 408.188          | -0.012           | -0.00024              | ~0.0029%     |

> **Note:** Across 50 orbits at 100× simulation speed, orbital extrema remained stable within ±25 meters (apogee) and ±12 meters (perigee), validating the Dormand>

### Orbital Period Comparison

| Orbit Type                  | Reference Tool | Expected Period (avg) | Simulated Period (avg) | Accuracy   |
|-----------------------------|----------------|-------------------------------|--------------------------------|------------|
| LEO (408 km circular)       | Keplerian Calc | 92.74 min                     | 92.62 min                      | ~99.87%    |
| Elliptical (7000–20007 km)  | Keplerian Calc | 7.75 hrs                      | 7.88 hrs                       | ~98.32%    |

---

## Physics Breakdown

See [PHYSICS_BREAKDOWN.md](./PHYSICS_BREAKDOWN.md) for full math.

- Newton’s Law of Gravity:
  ```
   F = G * (m1 * m2) / r²
  ```

- Dormand–Prince 5(4) integrator used for all motion updates, greatly improving upon previous RK4 method
- Thrust accelerations applied directly within integrator steps

---

## Features

- Custom satellite/object placement with initial velocity and mass
- Thrust controls (prograde, retrograde, radial, normal, etc.)
- Dynamic apogee/perigee/orbital period computation
- Adjustable time scaling (1x to 100x)
- Two camera modes: Track and Free
- Unity frontend for visualization and interaction

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

- Integration of atmospheric drag (currently in development)
- Orbital burn planning and delta-v/fuel mass modeling
- Perturbation forces including J2 and solar pressure
- Enhanced performance and scaling via Barnes-Hut algorithm for increased object counts

---

## Limitations

- Atmospheric drag and orbital decay modeling are currently experimental and nearing integration.
- Earth is fixed; no back-reaction from satellite mass
- No relativistic corrections
- Simplified collision handling (objects removed on collision without detailed physical interaction).

---

[⬆ Back to Top](#technical-breakdown--orbital-control-simulator)
