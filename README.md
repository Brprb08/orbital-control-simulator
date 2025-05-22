# Orbital Control Simulator

A real-time orbital mechanics sim built in Unity with a custom C++ physics backend. It models Newtonian orbital motion using a Dormand–Prince 5(4) integrator, supports live thrust, atmospheric drag, and visualizes full orbital paths with GPU-drawn trajectories.

All orbital motion is calculated externally in double-precision native code, no Unity physics involved.

[🎥 Watch the Demo Video on Youtube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be)
![Orbit Mechanics Simulator in Track Cam](./Assets/Images/04-17Track.png)
![Elliptical Orbit](./Assets/Images/04-17SatelliteUpClose.png)
![Free Cam](./Assets/Images/04-16Free.png)

---

## Why I Built This

I got interested in orbital mechanics after watching a few rocket launches and started digging into what happens after liftoff. I wanted a project that touched real-time code, game dev, physics, and low-level systems, so I built one to learn as I went. I don’t have an aerospace background, just a CS degree and a system that got interesting fast. This isn’t just visual, the goal was to simulate real orbital behavior.

---

## Capabilities & Features

- Runtime placement of satellites with mass, radius, velocity, and direction
- Add satellites at runtime via TLE input
- Instant thrust maneuvers in multiple directions (prograde, radial, normal, etc.)
- Real-time orbital decay via atmospheric drag modeling
- Continuously computes apogee, perigee, velocity, altitude, orbital period, inclination, eccentricity, semi-major axis, and RAAN
- Time scaling from 1x to 100x for long-term simulations
- GPU-rendered trajectory paths for smooth orbital visualization
- Dual camera modes (free roam and tracking)

---

## Purpose

I built this to explore and implement orbital mechanics concepts in a real-time environment. It served as a way to:

- Deepen my understanding of spacecraft dynamics and numerical integration
- Implement Dormand–Prince 5(4) for stable, high-accuracy propagation
- Handle real-world perturbation forces like drag
- Work on simulation-grade interoperability between Unity and native C++ (via DLLs)
- Optimize rendering pipelines and data flow in a live physics context

---

## Architecture Overview

- **Physics Core (C++ DLL):** Dormand–Prince 5(4) integrator, double-precision, real-time execution
- **Unity Frontend:** UI, scene management, camera controls, and GPU-based line rendering
- **Thrust Model:** Instantaneous impulse-based velocity change (scaled by body mass)
- **Atmospheric Drag:** Empirical model using interpolated density tables and cross-sectional area
- **Interop Layer:** Unity communicates with the C++ backend via `DllImport`

---

## Unit Testing

- Includes a dedicated Edit Mode unit tests for utility logic
- All tests written in C# using Unity Test Framework
- 34/34 tests passing as of latest commit (camera math, parsing, edge cases)
- Utility classes refactored for testability (static methods, no MonoBehaviours)

---

[See Technical README →](./TECHNICAL_README.md)

---

*This project was designed as a technical demonstration of my abilities in simulation engineering, physics programming, and real-time system development.*

[⬆ Back to Top](#orbital-control-simulator)
