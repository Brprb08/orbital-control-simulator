[⬅ Back to README](./README.md)

# Technical Breakdown – Orbital Control Simulator

A high-accuracy orbital mechanics simulation prototype using custom numerical integration and real-world physics.

## Who This Is For

This document is for aerospace engineers, simulation devs, and technical reviewers interested in orbital dynamics, integration techniques, or real-time performance.

---

## Table of Contents

- [Motivation](#motivation)
- [Simulation Core](#simulation-core)
- [Validation Results](#validation-results)
- [Physics Summary](#physics-summary)
- [TLE Parsing](#tle-parsing)
- [Interop Architecture](#interop-architecture)
- [Trajectory Prediction](#trajectory-prediction)
- [Unit Testing Details](#unit-testing-details)
- [Planned Enhancements](#planned-enhancements)
- [Limitations](#limitations)
- [Directory Layout](#directory-layout)

---

## Motivation

This project started as an exploration into orbital mechanics after watching a few rocket launches. It gradually turned into a way to simulate real orbital mechanics in real time, using Unity for rendering and C++ for the physics.

---

## Simulation Core

The sim uses Newtonian gravitational dynamics with a native C++ Dormand–Prince 5(4) integrator.

Key components:
- **Integrator:** DOPRI5, fixed-step with substepping
- **Drag:** Modeled using empirical density interpolation
- **Thrust:** Instant/continuous vectors in standard orbital directions
- **Rendering:** GPU-predicted trajectory lines
- **TLE Support:** Users can initialize satellites using standard TLE format (see section [TLE Parsing](#tle-parsing))
- **Maneuver Nodes (early):** Select from standard burn directions and place maneuver nodes along the orbit. Nodes automatically execute when the satellite reaches the specified point in its orbit. Each burn currently applies a fixed impulse: 5 seconds at 1 newton. Slider-based node positioning is functional but approximate. Visuals show before/after trajectories on burn.

---

## Validation Results

Accuracy was validated against both Keplerian predictions and long term orbital stability. Tests assume simplified Newtonian gravity (no drag, no J2, no higher-order perturbations). Early development focused on numerical accuracy in this idealized case before introducing perturbative forces like drag, J2 oblateness, or solar pressure.

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

## Physics Summary

Full gravity/thrust formulations and integration details are available in [PHYSICS_BREAKDOWN.md](./PHYSICS_BREAKDOWN.md). Key modeling elements:
- Full Dormand–Prince 5(4) integrator breakdown with frame-by-frame flow
- Detailed numeric precision strategy (double vs float handling)
- Scaled Earth-Centered Inertial (ECI) reference frame and unit system
- Atmospheric drag model using real density interpolation
- Newtonian gravity and thrust formulations with edge-case protections

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

> Line 1 is included for validation but ignored during parsing. The epoch and drag-related fields are currently unused. While the sim does account for Earth’s rotation, it does not align satellite initialization to a specific UTC timestamp. In most cases, orbital geometry and motion remain accurate without this. Earth-relative alignment will be added in a future version for more realism.

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

## Trajectory Prediction

The simulator uses two separate systems for physics simulation and trajectory visualization:

### 🔹 CPU Simulation – Dormand–Prince 5(4)
All actual orbital motion is computed in double-precision via a custom C++ Dormand–Prince 5(4) integrator. This handles real-time position and velocity updates with thrust and drag.

### 🔹 GPU Prediction – Runge-Kutta 4 (RK4)
Trajectory prediction (for orbit previews and maneuver node planning) is computed using a GPU-based RK4 integrator. This runs asynchronously in float precision and does **not** affect live physics.

> The GPU version is used solely for rendering future orbital paths in real time. This decouples rendering from simulation and allows smooth path visualization even at high time scales or under thrust.

RK4 was chosen here for its performance and simplicity, while DOPRI5 remains the core of the simulation backend.

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

## Planned Enhancements

- Additional perturbation forces including J2 oblateness and solar radiation pressure.
- Enhanced performance and scaling via Barnes-Hut algorithm for increased object counts
- Trajectory preview before burn while moving maneuver node
- Delta-v targeting and fuel budgeting
- Improved burn direction control and support for variable thrust duration

---

## Limitations

- Earth is fixed; no back-reaction from satellite mass
- No relativistic corrections
- Simplified collision handling (objects removed on collision without detailed physical interaction).

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

[⬆ Back to Top](#technical-breakdown--orbital-control-simulator)
