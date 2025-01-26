[⬅ Back to README](../README.md)

# Physics Documentation

This file contains a detailed breakdown of the physics and methods used in the Orbit Mechanics Simulator, including how the **Runge-Kutta 4th Order (RK4)** integration is implemented and the underlying calculations for forces, acceleration, and velocity.

---

## Numerical Integration: RK4 in Detail

The **Runge-Kutta 4th Order Method (RK4)** is used to update the position and velocity of celestial bodies in the simulation. Here’s how it works in the context of this project:

---

### 1. What Does RK4 Do?

RK4 predicts the future state of an object (its position and velocity) by calculating intermediate steps (called "slopes") at different points within the time step (`deltaTime`). These slopes represent derivatives of the position and velocity, and they are used to compute a weighted average for the final result. This ensures the accuracy of the simulation.

---

### 2. How RK4 is Implemented Here

In this simulation, RK4 works by repeatedly calling the method `CalculateDerivatives`. This method takes the current state of the object (position and velocity), calculates the acceleration due to gravity and thrust, and then returns the derivatives needed for the RK4 algorithm.

The RK4 process is broken down into the following steps:

- **Step 1: Initial Derivatives (`k1`)**  
  The derivatives for the current position and velocity are calculated directly using the method `CalculateDerivatives`.

- **Step 2 & 3: Intermediate Steps (`k2` and `k3`)**  
  For `k2` and `k3`, the derivatives are calculated at a midpoint, adjusting the position and velocity slightly forward in time using the previous derivatives.

- **Step 4: Final Derivatives (`k4`)**  
  The derivatives are calculated at the end of the time step, using the state adjusted fully forward in time.

- **Step 5: Weighted Average**  
  The derivatives (`k1`, `k2`, `k3`, `k4`) are combined in a weighted average to compute the object's new position and velocity.

---

### 3. `CalculateDerivatives`

The method `CalculateDerivatives` is central to the RK4 implementation. Here’s how it works:

#### Purpose:
This method computes the velocity and acceleration (i.e., the "derivatives") for a given state of the object. It uses the current position, velocity, and any applied thrust.

#### Steps:
1. **Compute Gravitational Acceleration**  
   The method calls `ComputeAccelerationFromData`, which calculates the total gravitational acceleration acting on the object due to all other bodies in the system.

2. **Combine Thrust Impulse**  
   If a thrust impulse is applied, it adds the corresponding acceleration to the gravitational acceleration.

3. **Return Derivatives**  
   The velocity and acceleration are packaged into an `OrbitalState` object, which is used in the RK4 process.

---

### 4. `ComputeAccelerationFromData`

This method handles the detailed calculation of gravitational forces and acceleration for an object. It ensures that all interactions between bodies are accounted for. Here’s how it works:

#### Purpose:
To calculate the total acceleration on an object due to:
- Gravitational forces from other celestial bodies.
- Any external forces, such as thrust.

#### Steps:
1. **Gravitational Force Calculation**  
   For each other body in the system:
   - Calculate the direction of the gravitational pull using the vector difference between positions.
   - Compute the distance squared to avoid expensive square root calculations.
   - Use Newton’s Law of Gravitation:

```
 F = G * (m1 * m2) / r^2 
```

   - Add the resulting force vector to the total force.

2. **Avoid Singularities**  
   To prevent division by zero or excessive force magnitudes when two objects are very close, a minimum distance threshold is enforced.

3. **Add External Forces**  
   Combine the gravitational acceleration with the thrust impulse (if applied).

4. **Return Total Acceleration**  
   The total acceleration is returned and used by `CalculateDerivatives`.

---

### 5. Example: Pulling It All Together

At each time step, the RK4 process combines everything:

1. **Start with the Current State**  
   Use the current position and velocity of the object.

2. **Call `CalculateDerivatives`**  
   Compute `k1`, `k2`, `k3`, and `k4` derivatives at different points within the time step.

3. **Update State**  
   Compute the new position and velocity using the weighted average of the derivatives:

```
 newPosition = currentState.position + (deltaTime / 6) * (k1.position + 2 * k2.position + 2 * k3.position + k4.position)
```

```
newVelocity = currentState.velocity + (deltaTime / 6) * (k1.velocity + 2 * k2.velocity + 2 * k3.velocity + k4.velocity)
```

This process repeats for every body in the system, ensuring accurate updates to their orbits.

---

### Why This Approach?

Using RK4 allows the simulation to:
- Maintain stable orbits over long periods.
- Accurately handle complex multi-body interactions.
- Allow for external forces (like thrust) to be seamlessly integrated into the simulation.

By combining physical accuracy with numerical stability, RK4 ensures that the simulator is both realistic and reliable for a variety of orbital scenarios.

---

## Orbital Dynamics

Combining gravitational forces and RK4 integration creates realistic orbital behavior, including:
- **Stable orbits**: Elliptical or circular orbits naturally form.
- **Orbital transfers**: Adjustments to velocity simulate real-world orbital maneuvers.
- **Gravitational slingshots**: Smaller bodies gain or lose energy after close encounters.

---

## Thrust Mechanics (Prototype)

Thrust is applied to a tracked body as an instantaneous force. It is proportional to the object's mass:

```
F_thrust = m * a 
```

Directions include:
- Prograde/Retrograde (along the orbit).
- Radial (toward or away from the central body).
- Lateral (perpendicular to the orbital plane).

[⬅ Back to README](../README.md)
