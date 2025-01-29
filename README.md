# Orbit Simulator

![Orbit Mechanics Simulator in Track Cam](./Assets/Images/01-28Track.png)
![Satellite Close Up Elliptical Orbit](./Assets/Images/01-28SatelliteCloseUp.png)
_Current state of the simulation. Top image shows the Track cam with current object you are tracking, as well as velocity and altitude. The second image is Track cam up close showing the Satellite model. Work in progress._

# Orbit Mechanics Simulator

An educational **orbital mechanics** simulator built in Unity, showcasing real-time gravitational interactions, trajectory predictions, and user-driven placement. This project is built for anyone who’s fascinated by space. It gives you the tools to interact with orbital physics in real-time, manipulate celestial bodies, tweak initial velocities, and scale time to watch how gravity affects all objects.

---

## Table of Contents
- [Overview](#overview)
- [Project Motivation and Goals](#project-motivation-and-goals)
- [Key Features](#key-features)
- [Orbit Mechanics](#orbit-mechanics)
- [How to Use](#how-to-use)
- [Physics Breakdown](#physics-breakdown)
- [Planned Updates](#planned-updates)
- [Limitations](#limitations)
- [Getting Started](#getting-started)

---

## Project Motivation and Goals

I’ve always been into space and wanted to build something that wasn’t just visually cool but actually simulated real orbital mechanics. I started with the basics—getting orbits to behave realistically—but as I kept working, it evolved into something much bigger. Now it has GPU-based trajectory rendering, real-time thrust mechanics, and RK4 integration for accurate physics.

Now, the goal is to refine it, improve the UI, and expand features like collision handling and orbital maneuvers. It’s both a passion project and a technical showcase, proving I can take complex systems and make them work. Long-term, I’d love for this to help me break into space-related simulation or aerospace work.

---

## Key Features                                                                                                                                                                                                                                 1. **Central Body Rotation**                                                                                               
   - A central body (e.g., Earth) remains in place but **rotates**, simulating real-world planetary spin.  

2. **Custom Planet Placement**
   - **Free Camera Mode**: Create planets on the fly, specifying their mass and radius.                                    
   - **Manual Velocity Assignment**: Use a slider or drag mechanics to set initial velocity vectors for newly placed bodies

3. **Runge-Kutta Integration**
   - Employs **RK4** for numerically stable and accurate orbital trajectory updates.

4. **Collision Detection & Removal**
   - Automatically removes smaller bodies upon collision.
   - If the tracked body is removed, the camera switches to the next available target or to Free Camera.

5. **Multiple Camera Modes**
   - **Track Camera**: Follows a selected celestial body, with UI for velocity/altitude readouts.
   - **Free Camera**: Roam freely to observe the entire scene and place new objects.

6. **Time Control**
   - **Slider** for simulation speed (e.g., 1× to 100×).
   - **Pause/Resume** with a button.
   - **Reset** time scale by pressing **R**.

7. **Real-Time Feedback**
   - **Velocity** displayed in m/s and mph.
   - **Altitude** displayed in km and ft.
   - **Apogee & Perigee** lines (and future expansions) visible for the tracked object.

8. **Interactive Thrust (Prototype)**
   - Basic **prograde, retrograde, radial in/out, and lateral** thrust controls for the **tracked** body.
   - Force is scaled by the object’s mass, altering orbits in real time.
   - Allows users to experiment with orbital maneuvers at a fundamental level (e.g., quick burns to raise/lower altitude)

---

## Orbit Mechanics

### Physics Breakdown
This project uses real physics principals and numerical methods to model orbital mechanics [Physics.md](./Physics.md)

---

## How to Use

### Track Camera Mode
- **Select a Body**: Press **Tab** to cycle through existing bodies.
- **Camera Controls**: Right mouse button to rotate around the target; mouse wheel to zoom.
- **UI**: Displays the tracked body’s velocity (m/s and mph) and altitude (km and ft).

### Free Camera Mode
- **Navigation**: Use WASD/arrow keys to move; right mouse drag to rotate; mouse wheel to zoom.
- **Placement**: Allows real-time creation of new planets.

### Time Control
- **Slider**: Adjust the simulation speed from real-time up to high-speed time-lapse.
- **Pause/Resume**: Halt or restart the entire simulation at will.
- **Reset**: Press **R** to revert speed to the default.

### Placing New Celestial Bodies
1. **Enter Mass & Radius**: In the UI, specify the planet’s properties (1–500,000 kg mass, and any radius scale).
2. **Place Object**: Click **Place Planet** to instantiate a placeholder.
3. **Set Velocity**: Drag to form a velocity vector or enter numeric values directly. Click **Set Velocity** to finalize orbit insertion.

### Early Thrust Implementation
- Available **only** when a body is tracked (in Track Camera mode).
- **Prograde/Retrograde**: Increase or decrease orbital velocity along the current velocity vector.
- **Radial In/Out**: Burn toward or away from the planet’s center vector for orbital radius changes.
- **Lateral (Left/Right)**: Burn perpendicular to the orbit path, altering inclination or path shape.
- Thrust is **scaled by the object’s mass**, so heavier bodies require more force for the same effect.

---

## Planned Features

- **Refined Thrust & Maneuvers**: Planning realistic orbital maneuvers (e.g., circularization, Hohmann transfers) with fuel usage baked in.
- **Advanced Collision Effects**: Right now, collisions just remove smaller bodies, but I want to add realistic effects like explosions or merging.
- **Extended Physics**: Stuff like orbital decay or atmospheric drag to make long-term orbits more realistic.
- **Polished Visuals**: The graphics are rough right now, but I’m working on smoother camera transitions, cleaner UI, and better models.
- **Fuel Tracking**: Add real engine burn times and consumption to make mission planning more challenging (and rewarding).

---

## Limitations
- **No Aerodynamic Effects**: Currently, there’s no atmosphere or drag modeling.
- **No Relativistic Corrections**: Strictly Newtonian physics—relativistic effects are not accounted for.
- **Simplified Collisions**: Bodies are removed rather than merged; no physical collision response.
- **Prototype Thrust**: Thrust controls are still basic. More detailed burn planning is not yet implemented.

## Status

Visuals are still a work in progress, but the core physics engine is up and running. Updates will focus on polishing usability and expanding features like orbital maneuvers and collision realism.

## Getting Started

### Prerequisites

- **Unity:** Ensure you have Unity installed (version 2020.3 or later recommended).
- **Git:** For version control and cloning the repository.

### Installation

1. **Clone the Repository:**

- HTTPS:
  ```bash
  git clone https://github.com/Brprb08/space-orbit-simulation.git
  ```
- SSH:
  ```
  git clone git@github.com:Brprb08/space-orbit-simulation.git
  ```
- Github CLI:
  ```
  gh repo clone Brprb08/space-orbit-simulation
  ```

2. **Open in Unity:**

- Launch Unity Hub.
- Click on `Add` and navigate to the cloned repository folder.
- Open the project.

2. **Run the Simulation:**

- Open the `SampleScene.unity` file located in the `Assets/Scenes` directory.
- If no hierarchy or GameObjects are visible, ensure you have opened the correct scene by double-clicking `SampleScene.unity`.
- Click the `Play` button to start the simulation.

[⬆ Back to Top](#orbit-simulator)
