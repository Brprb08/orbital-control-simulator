# Orbital Control Simulator

A real-time orbital mechanics simulator built in **Unity** with physics computations handled with **C++ DLL**. This project demonstrates accurate Newtonian multi-body dynamics using the Dormand–Prince 5(4) integrator, live thrust maneuvers, and GPU-rendered trajectories.

**No built-in Unity physics used.**

> **Update:** Atmospheric drag has been fully integrated into the simulation. Initially, non-gravitational forces like drag and J2 perturbations were excluded intentionally to ensure the Dormand–Prince 5(4) integrator's accuracy and stability. With foundational accuracy verified, atmospheric drag is now implemented to simulate realistic orbital decay and perturbations.

[🎥 Watch the Demo Video on Youtube](https://www.youtube.com/watch?v=aisBrqQ_A4o&feature=youtu.be)
![Orbit Mechanics Simulator in Track Cam](./Assets/Images/04-17Track.png)
![Elliptical Orbit](./Assets/Images/04-17SatelliteUpClose.png)
![Free Cam](./Assets/Images/04-16Free.png)


---

## What This Is

This is a simulation prototype that allows:

- Runtime placement of satellites with mass, radius, starting velocity and direction
- Thrust maneuvers: prograde, retrograde, radial, normal/anti-normal
- Atmospheric drag and realistic orbital decay
- Visualization of orbital trajectories with GPU acceleration
- Continuous tracking of apogee, perigee, velocity, and altitude
- Time scaling from 1x to 100x
- Two camera modes: Free and Track

The physics are offloaded to a native C++ library for improved performance, allowing Unity to focus on visualization and interaction.

---

## Why I Built This

After following real-world missions like SpaceX and exploring tools like GMAT, I wanted to build something hands-on that reflects actual orbital mechanics. This simulator became a platform to:

- Implementing advanced numerical integration (Dormand–Prince 5(4))
- Model realistic perturbations like atmospheric drag.
- Explore multi-body gravitational systems in real time
- Work with C++ and Unity interoperability (DLL calls)
- Optimize both simulation logic and rendering

It also served as a way to deepen my understanding of orbital mechanics and real-time spaceflight simulation.

---

## System & Tools Breakdown

- **Physics Core (C++ DLL):** Dormand–Prince 5(4) integrator for superior accuracy
- **Unity Frontend:** Object instantiation, camera controls, and trajectory visualization
- **Thrust Model:** Instantaneous velocity change (impulse burn) based on burn direction and current mass
- **Atmospheric Drag:** Real-time drag force calculations influencing orbital trajectories.
- **Trajectory Rendering:** GPU-drawn orbital paths for performance
- **Interop:** Unity uses `DllImport` to communicate with native physics functions

---

[See Technical README →](./TECHNICAL_README.md)

---

*This project was designed as a technical demonstration of my abilities in simulation engineering, physics programming, and real-time system development.*

[⬆ Back to Top](#orbital-control-simulator)
