# Orbital Control Simulator

A real-time orbital mechanics simulator built in Unity, with the simulation running in a native C++ backend.

The system runs continuously. Satellites can be added, reset, or thrust at any point, and the simulation responds immediately.

Unity is responsible for rendering, input, UI, and camera control. Physics integration, force accumulation, and orbital state are handled in native code to maintain numerical stability under time acceleration and sustained thrust.
Core orbital motion is integrated using a batched Dormand–Prince 5th-order (DoPri5) solver, while trajectory previews are computed separately using an RK4 integrator on the GPU.

Real satellite TLEs can be loaded. Thrust is applied live, and trajectories update without stopping the simulation.

[Watch the demo video on YouTube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be) (Old version of sim, needs an update)

![Track Cam](./Assets/Images/12-5Track.png)
![Track Cam with Vectors](./Assets/Images/12-5Vectors.png)
![Elliptical Orbit](./Assets/Images/12-5SatelliteUpClose.png)
![Free Cam](./Assets/Images/12-5Free.png)

---

## Why This Exists

This started because I realized I didn’t actually understand orbits as well as I thought I did. I was watching a lot of SpaceX launches and seeing the boosters come back and land, but it always made me curious what happens to the payload once it's in space.

I knew the high-level ideas, go fast sideways at a high altitude and you are in an orbit. But I had no real understanding of how maneuvers are performed, how missions like getting to the Moon actually work, how drag is managed, or how satellite constellations are maintained.

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
  - Burn timing can be adjusted via drag or slider
  - Burns support prograde, retrograde, radial in/out, and normal/anti-normal directions
  - Burns are simulated as **finite-duration thrust under gravity** (not impulsive Δv)
  - Post-burn trajectories are previewed in real time
  - Finalized nodes are pinned and locked to prevent accidental edits

### Physics & Numerical Architecture
- **Native C++ orbital integration using a batched Dormand–Prince 5th-order (DoPri5) solver**
  - Designed for numerical stability under long runtimes and high time scales

- **Asynchronous trajectory prediction**
  - GPU-based RK4 integration for baseline orbit previews
  - CPU-based integration for maneuver previews involving thrust (Will be moved to GPU)
  - Prediction runs independently so previews never stall the main simulation

### Satellite Creation & Configuration
- **Runtime creation and reset of satellites**
  - Cartesian placement using explicit position and velocity vectors
  - Real-world TLE loading (Two-Line Element sets)
  - Keplerian placement from classical orbital elements
  - Randomized orbits for rapid testing and experimentation

### Orbital Analysis & Visualization
- **Continuous computation of orbital parameters**
  - Apogee, perigee, altitude, velocity, inclination, eccentricity, and orbital period
- **Optional vector overlays**
  - Velocity, prograde, radial, and normal reference frames
- **Simplified atmospheric drag modeling**
  - Visible orbital decay for low-altitude orbits

### Time Control & Camera Systems
- **Adjustable simulation speed**
  - Real-time up to 100× time acceleration
- **Multiple camera modes**
  - Free camera for placement
  - Target tracking for following a body
  - Earth-relative camera for orbit visualization

---

## Architecture Overview

The simulation is split between Unity and native code.

All physics integration and force run inside a native C++ DLL. This avoids Unity’s single-precision limitations and keeps numerical drift stable, especially when increasing the simulation time scale or applying sustained thrust.

The main simulation step uses a fixed-timestep, batched Dormand–Prince 5th-order (DoPri5) integrator in native C++, with gravity, thrust, and drag evaluated inside the same integration loop. Trajectory previews use a separate RK4 integrator on the GPU to prioritize responsiveness over absolute accuracy.

Unity is responsible for visualization, input handling, UI state, and camera behavior. Trajectory previews are computed separately on the GPU using compute shaders so that prediction does not stutter or interfere with the main physics step.

The two layers communicate through a minimal interop boundary using DllImport.

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

Most tests cover orbital parameter calculations, camera behavior and transitions, TLE parsing and validation, object lifecycle edge cases, and math tied to UI state.

The goal is not full coverage. The goal is to catch regressions early and keep the core simulation behavior consistent as features are added.

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
3. Load 
```  
   Assets/Scenes/OrbitSimulation.unity
```
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

The focus is on experimentation and understanding orbital behavior.

---

<details>
<summary><strong>Codebase Layout</strong></summary>

```text
OrbitalControlSimulator/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/                          // Application core: bootstrapping, time, services, utilities
│   │   │   ├── Bootstrap/                 // Simulation startup, dependency wiring, runtime context
│   │   │   ├── Data/                      // Data helpers, parsing, TLE ingestion, frame utilities
│   │   │   │   └── Parsing/               // TLE and data parsing utilities
│   │   │   ├── Extensions/                // Shared extension methods
│   │   │   ├── Services/                  // Runtime services
│   │   │   │   └── Abstractions/
│   │   │   └── Time/                      // Simulation time scaling and control
│   │   │
│   │   ├── Physics/                       // Orbital mechanics, Kepler math, constants, native interop
│   │   │
│   │   ├── Gameplay/                      // Interactable simulation systems
│   │   │   ├── Abstractions/              // Shared gameplay abstractions and math helpers
│   │   │   ├── ManeuverNodeUtils/         // Maneuver node system: nodes, gizmos, drag handles, previews
│   │   │   └── System/                    // Small gameplay/system helpers
│   │   │
│   │   ├── Rendering/                     // Trajectory, vector, and orbit visualization
│   │   │   ├── Lines/                     
│   │   │   ├── Trajectories/              
│   │   │   │   ├── Helpers/
│   │   │   │   └── Utils/
│   │   │   └── Vectors/                   // Force, velocity, and direction overlays
│   │   │
│   │   ├── Placement/                     // Satellite spawning and initial-condition tools
│   │   │
│   │   ├── Camera/                        // Camera modes, tracking, and control systems
│   │   │   ├── Abstractions/              
│   │   │   ├── Controllers/               
│   │   │   ├── Implementations/           
│   │   │   └── Orbit/                     
│   │   │
│   │   ├── UI/                            // UI state, views, controllers, and helpers
│   │   │   ├── Camera/                    // Camera-related UI, calculations, and mode controls
│   │   │   ├── Components/                // Reusable UI helpers
│   │   │   │   ├── Buttons/               
│   │   │   │   ├── Dropdowns/             
│   │   │   │   │   └── SatelliteDropdown/
│   │   │   │   └── Indicators/            // Indicators for tracked satellites and maneuver nodes
│   │   │   ├── Core/                      // UI root object, shared references, wiring
│   │   │   ├── Drawing/                   
│   │   │   ├── Flight/                    // HUD, orbit preview, and spacecraft attitude UI
│   │   │   ├── Instructions/              
│   │   │   ├── Placement/                 // All satellite placement UI
│   │   │   ├── Time/                      
│   │   │   ├── Trajectory/                // Trajectory and vector overlay UI
│   │   │   └── Tutorial/                  
│   │   │
│   │   ├── Audio/                         // Ambient and feedback audio systems
│   │   │
│   │   └── Tutorial/                      // Guided tutorial
│   │
│   ├── Plugins/
│   │   ├── Source/                        // Native C++ physics backend (Dormand–Prince integrator)
│   │   └── x86_64/                        // Precompiled native physics binaries
│   │
│   └── Tests/
│       └── EditMode/
│           ├── CameraTests/
│           ├── PhysicsTests/
│           ├── PlacementTests/
│           ├── RenderingTests/
│           ├── UITests/
│           └── UtilsTests/
```
</details>

[⬆ Back to Top](#orbital-control-simulator)
