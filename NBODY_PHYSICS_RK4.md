[⬅ Back to README](https://github.com/Brprb08/space-orbit-simulation#readme)

# Orbital Physics Breakdown  

This document details the physics behind the Orbit Mechanics Simulator, including numerical integration methods, gravitational force calculations, and thrust mechanics.

---

## Table of Contents  
- [Numerical Integration: Runge-Kutta 4 (RK4)](#numerical-integration-runge-kutta-4-rk4)  
- [Gravitational Force Calculations](#gravitational-force-calculations)  
- [Thrust Mechanics](#thrust-mechanics)  
- [Time Scaling and Accuracy](#time-scaling-and-accuracy)  
- [Limitations and Future Improvements](#limitations-and-future-improvements)  

---

## Numerical Integration: Runge-Kutta 4 (RK4)  

### Why RK4?  
Most basic physics simulations use **Euler integration**, but Euler’s method accumulates errors over time, leading to **instability in orbital paths**. RK4 (Runge-Kutta 4th Order) is a **higher-order numerical method** that significantly improves accuracy.  

### Why RK4 Instead of a Symplectic Integrator?
Symplectic integrators like Leapfrog or Verlet are great for **long-term orbital simulations** (years or centuries) because they naturally conserve **energy and angular momentum**, keeping orbits stable over time. But this sim isn’t about running orbits for centuries—it’s built for **short-duration maneuvering** and **visualizing trajectories over days or months**, where **local accuracy matters more than long-term conservation**.

RK4 gives **higher accuracy per step** than symplectic methods, making it the better choice for:
- **Precise maneuvering** during burns and trajectory tweaks.
- **Short-term orbit propagation**, where small numerical drift isn’t a big deal.
- **Real-time visualization**, since RK4 lets you adjust time steps freely.

It’s true that RK4 doesn’t conserve energy over the long haul, so if you tried running a system for centuries, orbits would slowly drift. But for **maneuvering, short-term predictions, and visualization**, this trade-off makes sense.

### RK4 Process  
The RK4 method estimates the next position and velocity of a body in four steps:  

1. Compute initial derivatives (k1) using the current position and velocity.  
2. Compute k2 and k3 using values at the midpoint of the timestep.  
3. Compute k4 at the end of the timestep.  
4. Combine k1, k2, k3, and k4 using a weighted average to get the final state update.  

**Mathematical Formulation:**  
``` newPosition = currentPosition + (dt / 6) * (k1.position + 2 * k2.position + 2 * k3.position + k4.position) ```  
``` newVelocity = currentVelocity + (dt / 6) * (k1.velocity + 2 * k2.velocity + 2 * k3.velocity + k4.velocity) ```  

This ensures that **orbital calculations remain stable** even over long time periods.  

---

## Gravitational Force Calculations  

Gravity is modeled using **Newton’s Law of Universal Gravitation**, where each body exerts a force on every other body in the system:  

**Equation:**  
``` 
F = G * (m1 * m2) / r^2 
```

Where:  
- **G** is the gravitational constant  
- **m1, m2** are the masses of the interacting bodies  
- **r** is the distance between the centers of mass  

### Implementation in N-Body Simulation  
For every time step, each body’s acceleration is computed based on the sum of all gravitational influences from other objects.  

**Steps:**  
1. Calculate distance vectors between all bodies.  
2. Compute individual gravitational forces.  
3. Apply acceleration based on **F = ma**.  
4. Update velocity and position using RK4 integration.  

### Avoiding Singularities and Overflows  
- **Minimum Distance Threshold:** Prevents division by zero when objects are too close.  
- **Softened Gravity (Optional):** If needed, small **epsilon terms** can smooth interactions.  

---

## Thrust Mechanics  

Thrust is applied as an instantaneous force that modifies a body’s velocity.  

**Equation:**  
```
 F_thrust = mass * acceleration  
```
Thrust can be applied in four directions:  
- **Prograde (along velocity vector)** → Increases orbit altitude.  
- **Retrograde (opposite velocity vector)** → Lowers orbit altitude.  
- **Radial In/Out (toward/away from central body)** → Adjusts eccentricity.  
- **Lateral (perpendicular to orbit plane)** → Changes inclination.  

The magnitude of acceleration follows:  
``` 
a_thrust = F_thrust / mass 
```

---

## Time Scaling and Accuracy  

The simulation supports **adjustable time scaling** from **real-time to 100x speed**.  
- RK4 integration maintains accuracy even at high speeds.  
- Time steps are kept **adaptive** to ensure stability in fast-forward modes.  

---

## Limitations and Future Improvements  

### Current Limitations  
- **No Relativity** → The simulation is **Newtonian only**, meaning no relativistic corrections.  
- **No Atmospheric Drag** → Objects in low orbits remain indefinitely.  
- **Simplified Collisions** → Objects are removed upon collision instead of merging.  

### Future Upgrades  
- **Barnes-Hut Optimization for N-Body Physics** → Improves performance for large simulations.  
- **GPU Acceleration for RK4 Computations** → Moving integration to the GPU for better scaling.  
- **Maneuver Node System** → Pre-plan orbital transfers like in Kerbal Space Program.  

---

## Summary  
This physics model ensures **high-accuracy orbital calculations** while balancing **performance and stability**. The use of **RK4, Newtonian gravity, and real-time thrust mechanics** allows users to experiment with real orbital mechanics in an interactive environment.  

[⬆ Back to Top](#orbital-physics-breakdown)
