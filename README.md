# Orbital Control Simulator

A real-time orbital mechanics simulator with a Unity-based frontend and a native C++ physics backend. This project demonstrates accurate Newtonian multi-body dynamics using a Dormand–Prince 5(4) integrator, live thrust maneuvering, atmospheric drag modeling, and GPU-accelerated trajectory rendering.

Unlike typical Unity projects, this simulator **does not use Unity’s built-in physics**. All orbital dynamics are computed using custom double-precision integration methods offloaded to native C++ code for accuracy and performance.

Designed to model realistic spacecraft orbits and perturbations in real time, the simulator is positioned as a lightweight alternative to tools like GMAT or STK, built from scratch for educational, prototyping, and technical demonstration purposes.

[🎥 Watch the Demo Video on Youtube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be)
![Orbit Mechanics Simulator in Track Cam](./Assets/Images/04-17Track.png)
![Elliptical Orbit](./Assets/Images/04-17SatelliteUpClose.png)
![Free Cam](./Assets/Images/04-16Free.png)


---

## Capabilities & Features

- Runtime placement of orbiting bodies with mass, radius, velocity, and direction
- Instant thrust maneuvers in multiple directions (prograde, radial, normal, etc.)
- Real-time orbital decay via atmospheric drag modeling
- Continuous computation of apogee, perigee, velocity, and orbital period
- Time scaling from 1x to 100x for long-term simulations
- GPU-rendered trajectory paths for smooth orbital visualization
- Dual camera modes (free-fly and tracking)

---

## Purpose

I built this to explore and implement core orbital mechanics concepts in a real-time, interactive environment. It served as a platform to:

- Deepen my understanding of spacecraft dynamics and numerical integration
- Implement Dormand–Prince 5(4) for stable, high-accuracy propagation
- Handle real-world perturbation forces like drag
- Work on simulation-grade interoperability between Unity and native C++ (via DLLs)
- Optimize rendering pipelines and data flow in a live physics context

This was not a game prototype—it was a ground-up build of a small-scale orbital dynamics engine for educational, technical, and portfolio purposes.

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
