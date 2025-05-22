[⬅ Back to README](./README.md)

# Technical Breakdown – Orbital Control Simulator

A high-accuracy orbital mechanics simulation prototype using custom numerical integration and real-world physics.

## Who This Is For

This document is for aerospace engineers, simulation devs, and technical reviewers interested in orbital dynamics, integration techniques, or real-time performance.

---

## Table of Contents

- [Motivation](#motivation)
- [Simulation Core](#simulation-core)
- [Numerics & Physics Details](#numerics--physics-details)
- [Validation Results](#validation-results)
- [Physics Summary](#physics-summary)
- [TLE Parsing](#tle-parsing)
- [Features](#features)
- [Interop Architecture](#interop-architecture)
- [Directory Layout](#directory-layout)
- [How To Run It](#how-to-run-it)
- [Planned Enhancements](#planned-enhancements)
- [Limitations](#limitations)

---

## Motivation

This project started as an exploration into orbital mechanics after watching a few rocket launches. It evolved into a full technical sandbox for real-time, double-precision orbital dynamics using Unity and native C++.

---

## Simulation Core

The sim uses Newtonian gravitational dynamics with a native C++ Dormand–Prince 5(4) integrator.

Key components:
- **Integrator:** DOPRI5, fixed-step with substepping
- **Drag:** Modeled using empirical density interpolation
- **Thrust:** Instant/continuous vectors in standard orbital directions
- **Rendering:** GPU-predicted trajectory lines
- **TLE Support:** Users can initialize satellites using standard TLE format (see section [TLE Parsing](#tle-parsing))

---

## Validation Results

Accuracy was validated against both Keplerian predictions and long term orbital stability. Tests assume simplified Newtonian gravity (no drag, no J2, no higher-order perturbations). Initial development intentionally prioritized numerical accuracy in this idealized case before introducing additional perturbative forces like atmospheric drag, J2 oblateness, or solar pressure.

### Long-Term Orbital Stability (LEO, No Drag)

100× time scale over 50 full orbits (~77 hrs). No drift; apogee/perigee remained stable within sub-meter precision.

| Orbit Range | Apogee (km) | Perigee (km) |
|-------------|-------------|--------------|
| All Orbits  | 420.062     | 407.863      |

### Orbital Period Accuracy - Keplerian Calculations

| Orbit Type                  | Expected | Simulated | Accuracy |
|-----------------------------|----------|-----------|----------|
| LEO (408 km circular)       | 92.74 min | 92.62 min | ~99.87%  |
| Elliptical (7000–20007 km)  | 7.75 hrs  | 7.88 hrs  | ~98.32%  |

---

## Numerics & Physics Details

### Precision Strategy

The sim uses different numeric precisions depending on where the data is flowing and what level of accuracy is required.

| Quantity                     | Type     | Reason                                                            |
|-----------------------------|----------|----------------------------------------------------------------------|
| Orbital State (true pos/vel) | double3  | Prevents drift in long simulations, used for integration accuracy    |
| Unity Transform             | float    | Unity uses float natively, conversion applied for visualization      |
| GPU Trajectory Prediction   | float    | Optimized for performance, used for visual prediction only           |
| Integrator Internals        | double   | Dormand–Prince operates fully in double precision for stability      |

Different float types are intentional. The sim integrates in high precision, then converts to float for rendering or Unity interop. This avoids precision loss over long durations without impacting performance where it’s not critical.

### Integration Settings

- Step Size: Fixed
- Max Δt per substep: `0.002s`
- Time slicing based on Unity's `fixedDeltaTime`

No adaptive error controls are enabled yet, but the integrator code supports embedded 4th-order error estimation. This is a planned change.

### Edge-Case Handling

Several numerical protections are in place to prevent simulation blowups or instability.

- Division by zero guards (1e-20)
- Max force cap: `1e8 N`
- NaN checks each frame
- Earth collision = immediate removal
- Min mass cutoff = `1e-6 kg`

--- 

## Units and Reference Frames

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

---

## Physics Summary

Full gravity/thrust formulations and integration details are available in [PHYSICS_BREAKDOWN.md](./PHYSICS_BREAKDOWN.md). Key modeling elements:

- Newtonian gravity (multi-body)
- Thrust-based accelerations (impulse and continuous)
- Dormand–Prince 5(4) integrator (double precision)

---

## TLE Parsing

The sim supports runtime creation of satellites using standard Two-Line Element (TLE) sets. Both lines must be entered, but only Line 2 is parsed to extract orbital elements. These are then converted into position/velocity vectors and propagated using the simulation’s physics engine.

### Example

ISS (ZARYA):
```
1 25544U 98067A   25142.27988196  .00009799  00000+0  18159-3 0  9994
2 25544  51.6357  75.4283 0002161 135.6229 224.4933 15.49660308511147
```

### Parsed Fields (Line 2)

| Field                 | Value        | Purpose                                |
|----------------------|--------------|----------------------------------------|
| Inclination (°)      | 51.6357      | Orbit plane tilt                       |
| RAAN (°)             | 75.4283      | Longitude of ascending node            |
| Eccentricity         | 0.0002161    | Orbit shape (0 = circular)             |
| Argument of Perigee  | 135.6229     | Orientation of orbit’s closest point   |
| Mean Anomaly (°)     | 224.4933     | Position in orbit at epoch             |
| Mean Motion (rev/day)| 15.49660308  | Revolutions per day (orbital speed)    |

> Line 1 is included for structural compliance but ignored during parsing. Epoch and drag-related fields are currently not used. Epoch is intentionally excluded: while the sim does account for Earth’s rotation, it does not currently align satellite initialization to a specific UTC timestamp. The goal is to have accurate orbital geometry and motion without introducing unnecessary complexity. In most cases, visual behavior and orbital correctness are unaffected unless real-time Earth-relative positioning is required. However, I do plan to add this later on.

---

### Conversion Logic

The parser follows this process:

1. **Extract values** from Line 2 (RAAN, inclination, e, ω, M, n)
2. **Convert mean motion to semi-major axis:**
```
a = (μ / (n * 2π / 86400)²)^(1/3)
```

3. **Solve Kepler’s Equation** to find eccentric anomaly (E)
4. **Convert orbital elements to Cartesian** coordinates in orbital plane
5. **Apply 3D transformation** using RAAN, i, and ω
6. **Adjust for Unity’s coordinate system** (Y/Z swap)
7. **Convert to sim units** (1 unit = 10 km)

---

## Interop Architecture

Unity calls into native C++ functions via platform invoke (`DllImport`), keeping heavy calculations outside the managed runtime.

Example structure:
```
- Unity initializes simulation state and time step  
- C++ function computes new positions and velocities  
- Unity receives updated data and visualizes it  
```

This setup reduces CPU load.

---

## Unit Testing Details

To ensure stability and correctness in utility logic, unit tests were implemented for:

- `TLEParser`: Valid TLE length, valid parameters and numbers, and valid parsing to cartesian coordinates
- `CameraCalculations`: Angle clamping, normalization, and orbital camera distance computations
- `ParsingUtils`: Robust vector and mass parsing with support for error handling and validation
- `OrbitalCalculations`: Apogee/Perigee calculations, eccentricity, raan, etc.
- `Extensions`: Parsing Vector3 -> Double3 and Double3 -> Vector3

**Testing Strategy:**
- Isolated via `EditModeTests.asmdef`
- Runtime logic decoupled from Unity lifecycle methods for testability
- Covers both valid inputs and invalid edge cases
- Verified with Unity Test Runner (all 34 tests passing)

> These tests are not for physics correctness (which is validated separately), but rather for supporting logic.

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
