# Orbital Control Simulator

A real-time orbital mechanics simulator built in Unity with a native C++ physics core. It computes satellite motion using a double-precision Dormand–Prince integrator and supports live thrust, drag, and orbital propagation.

You can load real TLE data, apply thrust, and visualize trajectory changes as they happen. The Unity layer handles visualization and camera controls; all physics runs natively in the C++ backend for accuracy.

[Watch the Demo Video on Youtube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be)
![Track Cam](./Assets/Images/12-5Track.png)
![Track Cam with Vectors](./Assets/Images/12-5Vectors.png)
![Elliptical Orbit](./Assets/Images/12-5SatelliteUpClose.png)
![Free Cam](./Assets/Images/12-5Free.png)

---

## Why I Built This

I got curious about orbital mechanics after watching a few SpaceX launches and wondering what really happens once the second stage reaches orbit, and how things like rendezvous or satellite parking actually work. I didn’t have a background in orbital dynamics or game development, but I did have one in computer science and wanted a project that would be a real challenge and strong enough to show in a portfolio.

It started as a simple scene with a few moving spheres, and eventually I was adding new features, fixing bugs, and improving performance to make it smoother and more fun to use. It turned into a great way to learn by building, figuring out how to simulate spacecraft motion while keeping everything running smoothly. I just got addicted to working on it.

---

## Capabilities & Features

_All functionality runs live at runtime._

1. Live computation of orbital parameters: apogee, perigee, altitude, velocity, period, inclination, eccentricity, semi-major axis, RAAN, mean anomaly, and time until perigee/apogee.
2. Real-time orbital decay using an atmospheric drag model
3. Thrust in the direction you’re pointing (prograde, retrograde, nadir/zenith, normal/antinormal)
4. Attitude control system with selectable pointing modes and smooth or snap slewing
5. GPU-predicted trajectories rendered with async RK4 integration (separate from the live sim)
6. Runtime placement of satellites with configurable mass, radius, velocity, and direction
   - **Cartesian Placement:** position + velocity vectors
   - **TLE Placement:** import real satellites via Two-Line Elements
   - **Keplerian Placement:** create orbits from classical elements (a, e, i, Ω, ω, ν)
   - **Randomized Placement:** spawn satellites in random orbits
7. Adjustable time scale from 1× to 100× for long-run propagation
8. Dual camera modes (free roam and tracking)
9. Vector Overlay System
  - Velocity, radial, and normal vectors rendered in real time
  - World-space labels that face the camera
  - Smooth distance-based scaling and fade-out
  - Minimal, unobtrusive scene visualization

---

## Architecture Overview

- **Physics Core (C++ DLL):** Dormand–Prince 5(4) integrator, double-precision, real-time execution
- **Unity Frontend:** UI, scene management, camera controls, and GPU-based line rendering
- **Thrust Model:** Continuous force integration (F = m·a, scaled by mass)
- **Interop Layer:** Unity communicates with the C++ backend via `DllImport`

---

## Unit Testing

**Total:** 50 Edit-Mode Tests

- Camera logic (angles, clamping, tracking)
- Orbital parameters (apogee, perigee, eccentricity, etc.)
- Body registration and lifecycle
- TLE parsing and validation
- Vector precision conversions
- UI state handling

---

## How to Run

### Requirements

- Unity 2020.3 or later
- Windows 64-bit (required for native C++ DLL support)

### Run from the Unity Editor

1. Clone the repo:
   ```bash
   git clone https://github.com/Brprb08/space-orbit-simulation.git
   ```
2. Open the project in Unity Hub
3. Load the scene: `Assets/Scenes/OrbitSimulation.unity`
4. Press `Play` in the Unity Editor

The native physics backend is provided as a precompiled 64-bit DLL in `Assets/Plugins/x86_64/`.  
You do not need to compile the DLL yourself.

The following runtime dependencies are also included to support the C++ plugin:

- `libgcc_s_seh-1.dll`
- `libstdc++-6.dll`
- `libwinpthread-1.dll`

These are required on systems that do not have the GCC installed.

### Build and Run (Standalone Executable)

To build and run the simulator as a Windows application:

1. In Unity, open the menu:

   - File → Build Settings (or Build Profiles, depending on your version)

2. Select **Windows** as the target platform
3. Check these settings:

   - **Architecture**: `Intel 64-bit`
   - **Build and Run on**: `Local Machine`
   - Make sure `Scenes/OrbitSimulation` is checked in the Scene List

4. Click **Build**, then select a folder to save the build output.
5. After the build completes, open the output folder and run the `SpaceOrbit.exe` file

All required DLLs (including the native physics plugin and its runtime dependencies) are included automatically, as long as they are in `Assets/Plugins/x86_64/`.

---

_This project was designed as a technical demonstration of my abilities in simulation engineering, physics programming, and real-time system development._

[⬆ Back to Top](#orbital-control-simulator)
