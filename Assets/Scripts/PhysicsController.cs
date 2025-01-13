// using UnityEngine;
// using System.Collections.Generic;

// /** 
//  * Handles physics calculations related to orbital dynamics using numerical integration methods.
//  * Provides functions for calculating next orbital states using Runge-Kutta methods.
//  */
// public class PhysicsController
// {
//     public struct OrbitalState
//     {
//         public Vector3 position;
//         public Vector3 velocity;

//         public OrbitalState(Vector3 position, Vector3 velocity)
//         {
//             this.position = position;
//             this.velocity = velocity;
//         }
//     }

//     public static NBody.OrbitalState RungeKuttaStep(NBody.OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
//     {
//         NBody.OrbitalState k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);
//         NBody.OrbitalState k2 = CalculateDerivatives(new NBody.OrbitalState(
//             currentState.position + k1.position * (deltaTime / 2f),
//             currentState.velocity + k1.velocity * (deltaTime / 2f)
//         ), bodyPositions, thrustImpulse);

//         NBody.OrbitalState k3 = CalculateDerivatives(new NBody.OrbitalState(
//             currentState.position + k2.position * (deltaTime / 2f),
//             currentState.velocity + k2.velocity * (deltaTime / 2f)
//         ), bodyPositions, thrustImpulse);

//         NBody.OrbitalState k4 = CalculateDerivatives(new NBody.OrbitalState(
//             currentState.position + k3.position * deltaTime,
//             currentState.velocity + k3.velocity * deltaTime
//         ), bodyPositions, thrustImpulse);

//         Vector3 newPosition = currentState.position + (deltaTime / 6f) * (k1.position + 2f * k2.position + 2f * k3.position + k4.position);
//         Vector3 newVelocity = currentState.velocity + (deltaTime / 6f) * (k1.velocity + 2f * k2.velocity + 2f * k3.velocity + k4.velocity);

//         return new NBody.OrbitalState(newPosition, newVelocity);
//     }

//     public static NBody.OrbitalState RungeKutta2Step(NBody.OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
//     {
//         // Calculate the first derivative (k1)
//         NBody.OrbitalState k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);

//         // Calculate the midpoint state using k1
//         NBody.OrbitalState midState = new NBody.OrbitalState(
//             currentState.position + k1.position * (deltaTime / 2f),
//             currentState.velocity + k1.velocity * (deltaTime / 2f)
//         );

//         // Calculate the second derivative (k2) using the midpoint
//         NBody.OrbitalState k2 = CalculateDerivatives(midState, bodyPositions, thrustImpulse);

//         // Update position and velocity using k2
//         Vector3 newPosition = currentState.position + deltaTime * k2.position;
//         Vector3 newVelocity = currentState.velocity + deltaTime * k2.velocity;

//         return new NBody.OrbitalState(newPosition, newVelocity);
//     }

//     private static NBody.OrbitalState CalculateDerivatives(NBody.OrbitalState state, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
//     {
//         Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions, thrustImpulse);
//         return new NBody.OrbitalState(state.velocity, acceleration);
//     }

//     private static Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse)
//     {
//         Vector3 totalForce = Vector3.zero;
//         float minDistance = 0.001f;  // Prevent divide-by-zero issues.

//         foreach (var body in bodyPositions.Keys)
//         {
//             Vector3 direction = bodyPositions[body] - position;
//             float distanceSquared = Mathf.Max(direction.sqrMagnitude, minDistance * minDistance);

//             float forceMagnitude = PhysicsConstants.G * (body.mass) / distanceSquared;
//             totalForce += direction.normalized * forceMagnitude;
//         }

//         Vector3 externalAcceleration = thrustImpulse;
//         return (totalForce) + externalAcceleration;
//     }
// }