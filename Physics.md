# Physics Documentation

This file contains a detailed breakdown of the physics behind the Orbit Mechanics Simulator.

---

## Gravitational Interactions

The simulation uses **Newton’s Law of Universal Gravitation** to calculate forces between objects. The force between two masses, `m1` and `m2`, separated by a distance `r`, is:

```
F = G * (m1 * m2) / r^2 
```

- **Direction**: The force acts along the line connecting the centers of the two objects.
- **Calculation**: A unit vector (`r_hat`) determines the direction of the force. The final force vector is:

```
 F_vector = F * r_hat
```

---

## Numerical Integration

To simulate motion, the project uses the **Runge-Kutta 4th Order Method (RK4)**, which balances accuracy and computational cost.

### Why RK4?
RK4 is more accurate than simpler methods like Euler integration, which can introduce instability in orbital paths over time.

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
