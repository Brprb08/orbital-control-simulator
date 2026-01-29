# Orbital Control Simulator

A real-time orbital mechanics simulator built in Unity, with the core physics running in a native C++ backend.

This project made to learn what actually happens after a payload reaches orbit. Not the launch profile, but everything after: maintaining stability, performing maneuvers, small velocity changes, and watching how each affects an orbit.

The system runs continuously at runtime. Satellites can be added, modified, or thrust at any point, and the simulation responds immediately. Unity handles rendering, input, UI, and camera control. Physics, integration, and force accumulation run natively to maintain numerical stability as time scales increase.

You can load real satellite TLEs, apply thrust, and see trajectories update live without pausing or precomputing.

[Watch the demo video on YouTube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be)

![Track Cam](./Assets/Images/12-5Track.png)
![Track Cam with Vectors](./Assets/Images/12-5Vectors.png)
![Elliptical Orbit](./Assets/Images/12-5SatelliteUpClose.png)
![Free Cam](./Assets/Images/12-5Free.png)

---

## Why This Exists

I started this project after realizing that I did not actually understand orbital motion beyond the basic idea of going fast sideways. Concepts like rendezvous, station keeping, and orbit shaping were mostly just words to me, and I wanted something interactive that forced me to learn these concepts.

I did not come from an orbital mechanics background or a game development background. I had a computer science background and an interest in building a system involving real-time orbital physics, and numerical accuracy.

The project began as a simple gravity simulation. As it improved, I added thrust, attitude control, drag, better integration methods, and increasing separation between simulation and visualization. A lot of the work went into keeping everything stable while allowing the simulation to keep running at all times.

---

## What It Can Do

Everything operates live at runtime.

- Continuous computation of orbital parameters such as apogee, perigee, altitude, velocity, inclination, eccentricity, and orbital period
- Simplified atmospheric drag modeling for low orbits, causing visible orbital decay over time
- Live thrust application in directions including prograde, retrograde, radial, and normal, with instant trajectory updates
- Attitude control with selectable pointing modes, supporting both smooth slews and instant reorientation
- GPU-based trajectory prediction using RK4 integration, computed asynchronously so previews do not cause stutters to the sim
- Runtime creation and reset of satellites with configurable parameters:
  - Cartesian placement using direct position and velocity vectors
  - TLE placement using real Two-Line Element data
  - Keplerian placement from classical orbital elements such as semi-major axis and inclination
  - Randomized placement for quick testing and experimentation
- Adjustable simulation speed from 1x up to 100x to observe long-term behavior
- Multiple camera modes:
  - Free camera for inspection and placement
  - Target tracking for following a specific body
  - Earth camera for better view of an orbit
- Optional vector overlays for velocity and orbital reference frames

---

## Architecture Overview

The simulation is split between Unity and native code.

All physics integration and force run inside a native C++ DLL. This avoids Unity’s single-precision limitations and keeps numerical drift stable, especially when increasing the simulation time scale or applying sustained thrust.

Unity is responsible for visualization, input handling, UI state, and camera behavior. Trajectory previews are computed separately on the GPU using compute shaders so that prediction does not stall or interfere with the main physics step.

The two layers communicate through a interop boundary using DllImport. The intent is to have Unity manage interaction and UI, and native code control integration and physics state.

---

## Design Constraints and Tradeoffs

This is intentionally a single-central-body simulation. All satellites are integrated relative to one central body. This keeps the system fast, predictable, and easy to reason about while still supporting realistic maneuvers and long-duration orbits.

The focus is on stability and responsiveness rather than perfect physical accuracy with N-Body interactions. Some models such as drag and thrust ramping, are simplified to keep the system interactive in real time.

---

## Testing

The project includes a focused set of Unity edit-mode tests targeting areas where small mistakes become immediately visible or extremely difficult to debug later.

Most tests cover orbital parameter calculations, camera behavior and transitions, TLE parsing and validation, object lifecycle edge cases, and math tied to UI state.

The goal is not exhaustive coverage. The goal is to catch regressions early and keep the core simulation behavior consistent as features are added.

---

## Running the Project

### Requirements

- Unity 2020.3 or later
- Windows 64-bit due to native DLL support

### Run in the Unity Editor

1. Clone the repository:
```
  git clone https://github.com/Brprb08/space-orbit-simulation.git
```
2. Open the project in Unity Hub
3. Load Assets/Scenes/OrbitSimulation.unity
4. Press Play

The physics backend is included as a precompiled 64-bit DLL in Assets/Plugins/x86_64/, so no separate build step is required.

Supporting runtime libraries such as libgcc and libstdc++ are bundled to avoid dependency issues on systems without a local GCC install.

### Standalone Windows Build

1. Open Build Settings in Unity
2. Select Windows 64-bit as the target platform
3. Ensure the OrbitSimulation scene is included
4. Build and run

All required DLLs are included automatically as long as they remain in Assets/Plugins/x86_64/.

---

## What This Project Is Not

This is not a full n-body simulator. It does not attempt to model multi-body perturbations or full mission planning.

It is also not a game. The focus is on experimentation and understanding orbital behavior.


---

[⬆ Back to Top](#orbital-control-simulator)
