# Orbital Control Simulator

This is a real-time orbital mechanics simulator built in Unity, with the physics simulation running in native C++.

The simulation is always running continuously. You can add satellites, change attitude, and apply thrust at any point and the orbit updates right away.

Unity is responsible for rendering, input, UI, and camera control. Physics integration, force accumulation, and orbital state are handled in native code to maintain numerical stability under time acceleration and sustained thrust. Core orbital motion is integrated using a batched Dormand-Prince 5th-order (DoPri5) solver, while trajectory previews run through separate preview systems so the interactive parts of the sim do not lag or stutter.

Real satellite TLEs can be loaded. Satellites can also be placed manually or from orbital elements, with live orbit previews, maneuver planning, and trajectory updates without stopping the simulation.

[Watch the demo video on YouTube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be) (Old version of sim, needs an update)

![Track Cam](./Assets/Images/12-5Track.png)
![Track Cam with Vectors](./Assets/Images/12-5Vectors.png)
![Elliptical Orbit](./Assets/Images/12-5SatelliteUpClose.png)
![Free Cam](./Assets/Images/12-5Free.png)

---

## Why This Exists

This started because I realized I did not actually understand orbits as well as I thought I did. I was watching a lot of SpaceX launches and seeing the boosters come back and land, but it always made me curious what happens to the payload once it is in space.

I knew the high-level ideas: go fast sideways at a high altitude and you are in an orbit. But I had no real understanding of how maneuvers are performed, how missions like getting to the Moon actually work, how drag is managed, or how satellite constellations are maintained.

This project gave me something I could use to answer those questions. It began as a gray sphere moving around another gray sphere, and changed significantly over time as I learned more about both game development and orbital mechanics.

---

## What It Can Do

Everything runs live.

### Real-Time Orbital Control & Planning
- **Continuous, real-time orbital simulation**
  - Orbits change continuously under gravity, thrust, and drag
  - Thrust can be applied freely at any time without stopping the simulation
  - Trajectories update immediately, even under time acceleration

- **Free thrust with attitude-based control**
  - Select thrust direction using orbital reference frames
    (prograde, retrograde, radial in/out, normal, anti-normal)
  - Supports both smooth attitude slews and instant reorientation

- **Interactive maneuver node planning**
  - Optional planning built on top of free thrust
  - A node can be placed along the current predicted orbit
  - Burn timing can be adjusted via drag, slider, exact numeric input, or step buttons
  - Burn duration and thrust scale can be tuned with synced sliders and direct-entry fields
  - Burns support prograde, retrograde, radial in/out, and normal/anti-normal directions
  - Burns are simulated as **finite-duration thrust under gravity** (not impulsive delta-v)
  - Post-burn trajectories and orbit readouts are previewed in real time, including T+ timing and predicted orbital parameters
  - Finalized nodes are pinned and locked to prevent accidental edits

### Physics & Numerical Architecture
- **Native C++ orbital integration using a batched Dormand-Prince 5th-order (DoPri5) solver**
  - Designed for numerical stability under long runtimes and high time scales

- **Asynchronous trajectory prediction**
  - GPU-based RK4 integration for baseline orbit previews
  - Extra prediction handling for maneuver previews and cases where drag changes the orbit more noticeably
  - Prediction runs independently so previews never stall the main simulation

### Satellite Creation & Configuration
- **Runtime creation and reset of satellites**
  - Cartesian placement using explicit position and velocity vectors
  - Guided manual placement flow with live orbit preview while setting initial velocity
  - Earth-relative camera support during manual placement to inspect the full orbit path before launch
  - Immediate placement readouts for apogee, perigee, inclination, and eccentricity
  - Real-world TLE loading (Two-Line Element sets)
  - Keplerian placement from classical orbital elements
  - Randomized orbits for rapid testing and experimentation

### Orbital Analysis & Visualization
- **Continuous computation of orbital parameters**
  - Apogee, perigee, altitude, velocity, inclination, eccentricity, and orbital period
  - Readouts are used both for live tracked bodies and for placement / maneuver previews
- **Optional vector overlays**
  - Velocity, prograde, radial, and normal reference frames
- **Simplified atmospheric drag modeling**
  - Visible orbital decay for low-altitude orbits

### Time Control & Camera Systems
- **Adjustable simulation speed**
  - Real-time up to 100x time acceleration
- **Multiple camera modes**
  - Free camera for placement and inspection
  - Target tracking for following a body
  - Earth-relative camera for orbit visualization
  - Camera and UI state handling for placement, velocity setup, and maneuver workflows

---

## Architecture Overview

The simulation is split between Unity and native code.

All physics integration and force run inside a native C++ DLL. This avoids Unity's single-precision limitations and keeps numerical drift stable, especially when increasing the simulation time scale or applying sustained thrust.

The main simulation step uses a fixed-timestep, batched Dormand-Prince 5th-order (DoPri5) integrator in native C++, with gravity, thrust, and drag evaluated inside the same integration loop. Trajectory previews use separate prediction paths so baseline orbit previews stay responsive while thrust- and drag-sensitive previews can use more appropriate backends.

Unity is responsible for visualization, input handling, UI state, and camera behavior. A big part of the challenge has been keeping placement, camera modes, trajectory previews, and maneuver editing from stepping on each other.

The two layers communicate through a minimal interop boundary using `DllImport`.

---

## Performance Characteristics

On typical desktop hardware, the simulator can handle approximately 200 satellites in real time comfortably. It can support closer to 300 active bodies, though frame time begins to degrade at that point due to increased integration and rendering load.

Performance scales primarily with satellite count and simulation speed, as all bodies are integrated continuously even when not under thrust.

## Design Constraints and Tradeoffs

This is a single-central-body simulation. All satellites are integrated relative to one body to keep behavior predictable and performance consistent.

The focus is on stability and responsiveness rather than full physical completeness. Some models, such as drag and thrust ramping, are simplified to keep the system interactive in real time.

---

## Testing

The project includes a set of Unity edit-mode tests for areas where mistakes become immediately visible or difficult to debug later.

I have mostly focused testing on the parts that are easy to break and hard to notice immediately, especially orbital math, camera behavior, placement state, and trajectory preview logic.

The goal is not full coverage. The goal is to catch regressions early and keep the core simulation behavior consistent as features are added.

---

## Running the Project

### Requirements

- Unity 2020.3 or later
- Windows 64-bit due to native DLL support

### Run in the Unity Editor

1. Clone the repository:
```text
git clone https://github.com/Brprb08/space-orbit-simulation.git
```
2. Open the project in Unity Hub
3. Load
```text
Assets/Scenes/OrbitSimulation.unity
```
4. Press Play

The physics backend is included as a precompiled 64-bit DLL in `Assets/Plugins/x86_64/`, so no separate build step is required.

Supporting runtime libraries such as `libgcc` and `libstdc++` are bundled to avoid dependency issues on systems without a local GCC install.

### Standalone Windows Build

1. Open Build Settings in Unity
2. Select Windows 64-bit as the target platform
3. Ensure the `OrbitSimulation` scene is included
4. Build and run

All required DLLs are included automatically as long as they remain in `Assets/Plugins/x86_64/`.

---

## What This Project Is Not

This is not a full n-body simulator. It does not attempt to model multi-body perturbations or full mission planning.

The focus is on experimentation and understanding orbital behavior.

---

<details>
<summary><strong>Codebase Layout</strong></summary>

```text
OrbitalControlSimulator/
|-- Assets/
|   |-- Scripts/
|   |   |-- Audio/                         // Ambient and feedback audio systems
|   |   |
|   |   |-- Camera/                        // Camera modes, tracking, movement, and zoom rules
|   |   |   |-- Abstractions/              // Camera-facing interfaces and mode enums
|   |   |   |-- Controllers/               // High-level camera coordination
|   |   |   |-- Movement/                  // Camera rig movement and free-camera controls
|   |   |   |-- Tracking/                  // Tracked target/state models
|   |   |   `-- Zoom/                      // Camera distance, clamp, and zoom calculations
|   |   |
|   |   |-- Core/                          // Application core: bootstrapping, time, services, utilities
|   |   |   |-- Bootstrap/                 // Simulation startup, dependency wiring, runtime context
|   |   |   |-- Data/                      // Data helpers, TLE ingestion, and parsing utilities
|   |   |   |   `-- Parsing/
|   |   |   |-- Extensions/                // Shared extension methods
|   |   |   |-- Services/                  // Runtime services
|   |   |   |   `-- Abstractions/
|   |   |   `-- Time/                      // Simulation time scaling and control
|   |   |
|   |   |-- Gameplay/                      // Player-facing simulation features
|   |   |   |-- Flight/                    // Flight control and thrust interaction
|   |   |   |-- Maneuvers/                 // Maneuver nodes, burn math, previews, gizmos, drag handles
|   |   |   |-- Placement/                 // Satellite spawning and initial-condition tools
|   |   |   `-- System/                    // Small gameplay/system helpers
|   |   |
|   |   |-- Physics/                       // Simulation bodies, orbital mechanics, frames, and attitude math
|   |   |   |-- Attitude/                  // Orientation, burn-frame, and attitude helpers
|   |   |   |-- Bodies/                    // Runtime body state and body simulation coordination
|   |   |   `-- Orbit/                     // Kepler/orbital calculations, constants, native interop
|   |   |
|   |   |-- Rendering/                     // Trajectory, vector, line, and orbit visualization
|   |   |   |-- Lines/
|   |   |   |-- Trajectories/
|   |   |   |   |-- Helpers/
|   |   |   |   `-- Utils/
|   |   |   `-- Vectors/                   // Force, velocity, and direction overlays
|   |   |
|   |   |-- Tutorial/                      // Guided tutorial flow and progress state
|   |   |
|   |   `-- UI/                            // UI state, views, controllers, and reusable widgets
|   |       |-- Camera/                    // Camera mode and tracking readout UI
|   |       |-- Components/                // Reusable UI controls and visual widgets
|   |       |   |-- Buttons/
|   |       |   |-- Dialogs/
|   |       |   |-- Drawing/
|   |       |   |-- Dropdowns/
|   |       |   |   `-- SatelliteDropdown/
|   |       |   |-- Indicators/
|   |       |   `-- Utils/
|   |       |-- Core/                      // UI root object, shared references, wiring
|   |       |-- Flight/                    // HUD, orbit preview, and spacecraft attitude UI
|   |       |-- Instructions/
|   |       |-- Maneuvers/                 // Maneuver-node UI controls
|   |       |-- Placement/                 // Satellite placement UI
|   |       |-- Time/
|   |       |-- Trajectory/                // Trajectory and vector overlay UI
|   |       `-- Tutorial/
|   |
|   |-- Plugins/
|   |   |-- Source/                        // Native C++ physics backend (Dormand-Prince integrator)
|   |   `-- x86_64/                        // Precompiled native physics binaries
|   |
|   `-- Tests/
|       `-- EditMode/
|           |-- CameraTests/
|           |-- PhysicsTests/
|           |-- PlacementTests/
|           |-- RenderingTests/
|           |-- UITests/
|           `-- UtilsTests/
```
</details>

[↑ Back to Top](#orbital-control-simulator)
