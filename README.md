# Orbital Control Simulator

A real-time orbital mechanics sim built in Unity with a custom C++ physics backend. It models Newtonian multi-body dynamics with a Dormand–Prince 5(4) integrator, supports live thrust, atmospheric drag, and visualizes full orbital paths with GPU-drawn trajectories.

All orbital motion is calculated externally in double-precision native code, no Unity physics involved.

> This project started as a way to teach myself orbital mechanics and numerical integration. I'm from a computer science background, so everything here from the integrator to the drag model, was built to understand how real spacecraft dynamics work, beyong just visuals or simplified motion.

[🎥 Watch the Demo Video on Youtube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be)
![Orbit Mechanics Simulator in Track Cam](./Assets/Images/04-17Track.png)
![Elliptical Orbit](./Assets/Images/04-17SatelliteUpClose.png)
![Free Cam](./Assets/Images/04-16Free.png)


---

## Capabilities & Features

- Runtime placement of satellites with mass, radius, velocity, and direction
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

[See Technical README →](./TECHNICAL_README.md)

---

*This project was designed as a technical demonstration of my abilities in simulation engineering, physics programming, and real-time system development.*

[⬆ Back to Top](#orbital-control-simulator)
