[⬅ Back to README](./README.md)

# Technical Breakdown – Orbital Control Simulator

This is a high-accuracy orbital mechanics simulation prototype using custom numerical integration and real-world physics.

## Who This Is For

This document is intended for aerospace engineers, simulation developers, and technical reviewers interested in orbital dynamics, numerical integration strategies, and performance architecture for real-time propagation tools.

If you're assessing physics accuracy, C++/Unity interop, or simulation reliability, this breakdown covers those design choices.

---

## Table of Contents

- [Motivation](#motivation)
- [Simulation Core](#simulation-core)
- [Numerics & Physics Details](#numerics--physics-details)
- [Validation Results](#validation-results)
- [Physics Summary](#physics-summary)
- [Features](#features)
- [Interop Architecture](#interop-architecture)
- [Directory Layout](#directory-layout)
- [How To Run It](#how-to-run-it)
- [Planned Enhancements](#planned-enhancements)
- [Limitations](#limitations)

---

## Motivation

This project began as a personal exploration into orbital dynamics after being inspired by real-world launches and missions. It’s now an area for experimenting with numerical methods and optimizing real-time simulation performance using Unity and C++.

---

## Simulation Core

The simulator models Newtonian central-body gravitational dynamics using a custom Dormand–Prince 5(4) integrator, implemented in native C++ and interfaced with Unity through platform invoke.

Unlike typical Unity-based simulations, all orbital propagation is handled externally for numerical stability and performance, aligning more closely with engineering tools like GMAT than with standard game loops.

Key elements include:
- **Integrator:** Dormand–Prince 5(4) (non-adaptive, fixed step, substepped)
- **Drag:** Atmospheric drag modeled with empirical density interpolation
- **Thrust:** Discrete and continuous thrust models in multiple orbital directions
- **Rendering:** GPU-drawn trajectories for performant, long-duration visualization

---

## Validation Results

Orbital mechanics accuracy was validated against both Keplerian predictions and long term orbital stability. Tests assume simplified Newtonian gravity (no drag, no J2, no higher-order perturbations). Initial development intentionally prioritized numerical accuracy in this idealized case before introducing additional perturbative forces like atmospheric drag, J2 oblateness, or solar pressure.

### Long-Term Orbital Stability (LEO, No Drag)

> Validation was performed at **100× time scale** over a simulated duration of **~77.3 hours** (50 full LEO orbits).
> Apogee and perigee were sampled at **each orbit's high and low point**, not per frame, to more accurately track orbital drift.

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

> **Note:** Across 50 orbits at 100× simulation speed, orbital high and low points remained stable within ±25 meters (apogee) and ±12 meters (perigee), validating the integrator.

### Orbital Period Comparison

| Orbit Type                  | Reference Tool | Expected Period (avg) | Simulated Period (avg) | Accuracy   |
|-----------------------------|----------------|-------------------------------|--------------------------------|------------|
| LEO (408 km circular)       | Keplerian Calc | 92.74 min                     | 92.62 min                      | ~99.87%    |
| Elliptical (7000–20007 km)  | Keplerian Calc | 7.75 hrs                      | 7.88 hrs                       | ~98.32%    |

---

## Numerics & Physics Details

This section outlines the numerical precision strategy, units, time step logic, and edge-case handling used in the simulator. It’s meant to help understand the assumptions and tolerances behind the integration process and overall stability.

### Precision Strategy

The sim uses different numeric precisions depending on where the data is flowing and what level of accuracy is required.

| Quantity                     | Type     | Rationale                                                            |
|-----------------------------|----------|----------------------------------------------------------------------|
| Orbital State (true pos/vel) | double3  | Prevents drift in long simulations, used for integration accuracy    |
| Unity Transform             | float    | Unity uses float natively, conversion applied for visualization      |
| GPU Trajectory Prediction   | float    | Optimized for performance, used for visual prediction only           |
| Integrator Internals        | double   | Dormand–Prince operates fully in double precision for stability      |

Different float types are intentional. The sim integrates in high precision, then converts to float for rendering or Unity interop. This avoids precision loss over long durations without impacting performance where it’s not critical.

### Units and Reference Frames

The simulator assumes a consistent unit system and reference frame throughout.

| Dimension | Unit             | Reference Frame        |
|----------|------------------|------------------------|
| Length   | Kilometers (km)  | Earth-Centered Inertial (ECI) |
| Velocity | Kilometers/second (km/s) | ECI                        |
| Time     | Seconds (s)      | Unity time (scaled)    |
| Mass     | Kilograms (kg)   | Body mass (used in thrust and gravity) |

### Core Physical Constants and Body Parameters

These are the key physical constants and simulation parameters used in the orbital model. The simulation operates in a scaled unit system where **1 unit = 10 km**, and all internal physics calculations are performed in double precision.

| Parameter                   | Symbol        | Value              | Units (Sim / Real)    | Description                                                  |
|----------------------------|---------------|--------------------|------------------------|--------------------------------------------------------------|
| Gravitational Constant     | G             | ~6.674e-23         | units³·kg⁻¹·s⁻²        | Scaled for sim units (1 unit = 10 km); matches Newton’s law |
| Earth Mass                 | Mₑ            | 5.972e24           | kg                     | Real Earth mass                                              |
| Earth Radius               | Rₑ            | 637.8137 units (≈6378 km)                   | Used for collision detection and reference altitude          |
| Atmosphere Top             | —             | 50 units           | ~500 km                | Above this altitude, atmospheric drag is assumed negligible |
| Satellite Mass Range       | m_sat         | 500 – 500,000       | kg                     | Typical user-set mass for satellites                         |
| Satellite Radius Range     | r_sat         | 0.0001 – 0.1        | units (1m – 1 km)      | Used to compute cross-sectional area                        |
| Drag Coefficient           | C_d           | 2.2                 | unitless               | Standard default for bodies like satellites           |
| Cross-sectional Area       | A             | π·r² (derived)      | units²                 | Used in drag computation: A = πr²                            |

### Integrator Error Controls

The Dormand–Prince 5(4) method is used for orbit integration. Integration is performed in fixed steps to ensure consistent performance and simplify threading.

- Integrator: Dormand–Prince 5(4), implemented in native C++
- Step size: Fixed
- Substepping: Enabled, with a max dt of 0.002s per substep
- Step logic: Unity’s Time.fixedDeltaTime is divided into smaller substeps to stay within dt limits

No adaptive error controls are enabled yet, but the integrator code supports embedded 4th-order error estimation. This is a planned change.

### Stability Constraints

The integrator is kept stable by enforcing a maximum time step per substep.

| Parameter         | Value        | Reasoning                                       |
|------------------|--------------|-------------------------------------------------|
| Max substep Δt   | 0.002 s      | Prevents large integration errors or instability |
| Substeps         | Variable     | Derived from Time.fixedDeltaTime per frame      |

### Edge-Case Handling

Several numerical protections are in place to prevent simulation blowups or instability.

- Singularity Avoidance: Minimum distance squared threshold (1e-20) used to prevent division by zero during force calculations
- Max Force Cap: 1e8 N to avoid extreme accelerations from close approaches
- Overflow Checks: NaN checks are performed each frame on Unity transform positions
- Collision Handling: Collisions with Earth are detected based on radius overlap and objects are removed immediately
- Mass Threshold: Bodies with mass below 1e-6 are ignored from simulation updates

More robust collision modeling and adaptive error controls are planned but not yet implemented.

---

## Physics Summary

Full gravity/thrust formulations and integration details are available in [PHYSICS_BREAKDOWN.md](./PHYSICS_BREAKDOWN.md). Key modeling elements:

- Newtonian gravity (multi-body)
- Thrust-based accelerations (impulse and continuous)
- Dormand–Prince 5(4) integrator (double precision)

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

## ✅ Unit Testing Details

To ensure stability and correctness in utility logic, unit tests were implemented for:

- `CameraCalculations`: Angle clamping, normalization, and orbital camera distance computations
- `ParsingUtils`: Robust vector and mass parsing with support for error handling and validation

**Testing Strategy:**
- Isolated via `EditModeTests.asmdef`
- Runtime logic decoupled from Unity lifecycle methods for testability
- Covers both valid inputs and invalid edge cases
- Verified with Unity Test Runner (all 18 tests passing)

> These tests are not for physics correctness (which is validated separately), but rather for supporting logic that feeds the simulation pipeline.

---

## Directory Layout

<details>
<summary><strong>Click to expand full project structure</strong></summary>

```
OrbitalControlSimulator/
├── Assets/
│   ├── Fonts/                     # Custom fonts for UI (Orbitron, FuturaLight)
│   ├── Images/                    # Screenshot assets for README/demo
│   ├── Materials/                 # Shaders and grouped material sets (Earth, Satellites, etc.)
│   ├── Scenes/
│   │   └── OrbitSimulation.unity  # Main Unity scene
│   ├── Scripts/
│   │   ├── Camera/                # Camera control logic (Free, Track)
│   │   ├── Controllers/           # Game state and thrust/time control
│   │   ├── LineRender/            # Trajectory rendering & GPU line logic
│   │   ├── ObjectPhysics/         # Physics constants, body definitions
│   │   ├── ObjectPlacement/       # Runtime placement and drag manager
│   │   ├── UI/                    # UI toggle buttons and HUD controls
│   │   ├── Utils/                 # Shared calculations (orbital math, parsing)
│   │   └── SimulationCore.asmdef  # Defines the core runtime assembly, and enables references in tests
│   ├── Tests/
│   │   └── EditMode/              # Unit tests (Edit Mode) using Unity Test Framework
│   │       ├── CameraCalculationsTests.cs
│   │       ├── ParsingUtilsTests.cs
│   │       ├── ExtensionTests.cs
│   │       └── EditModeTesting.asmdef 
├── Packages/                      # Unity package configuration
├── Plugins/
│   ├── Source/                    # Native C++ integrator source (DOPRI5)
│   └── x86_64/                    # Compiled DLLs for Unity interop
├── ProjectSettings/               # Unity project settings
├── LICENSE
├── .gitignore
├── README.md                      # Project overview and usage
├── TECHNICAL_README.md            # Integration details and architecture
└── PHYSICS_BREAKDOWN.md           # Gravity, thrust, and integrator math
```
</details>

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

- Orbital burn planning and delta-v/fuel mass modeling
- Additional perturbation forces including J2 oblateness and solar radiation pressure.
- Enhanced performance and scaling via Barnes-Hut algorithm for increased object counts

---

## Limitations

- Earth is fixed; no back-reaction from satellite mass
- No relativistic corrections
- Simplified collision handling (objects removed on collision without detailed physical interaction).

---

[⬆ Back to Top](#technical-breakdown--orbital-control-simulator)
