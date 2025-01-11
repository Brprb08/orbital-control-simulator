using UnityEngine;
using System.Collections.Generic;

public class PhysicsController
{
    public struct OrbitalState
    {
        public Vector3 position;
        public Vector3 velocity;

        public OrbitalState(Vector3 position, Vector3 velocity)
        {
            this.position = position;
            this.velocity = velocity;
        }
    }

    public OrbitalState RungeKuttaStep(OrbitalState currentState, float deltaTime, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        OrbitalState k1 = CalculateDerivatives(currentState, bodyPositions, thrustImpulse);
        OrbitalState k2 = CalculateDerivatives(new OrbitalState(
            currentState.position + k1.position * (deltaTime / 2f),
            currentState.velocity + k1.velocity * (deltaTime / 2f)
        ), bodyPositions, thrustImpulse);

        OrbitalState k3 = CalculateDerivatives(new OrbitalState(
            currentState.position + k2.position * (deltaTime / 2f),
            currentState.velocity + k2.velocity * (deltaTime / 2f)
        ), bodyPositions, thrustImpulse);

        OrbitalState k4 = CalculateDerivatives(new OrbitalState(
            currentState.position + k3.position * deltaTime,
            currentState.velocity + k3.velocity * deltaTime
        ), bodyPositions, thrustImpulse);

        Vector3 newPosition = currentState.position + (deltaTime / 6f) * (k1.position + 2f * k2.position + 2f * k3.position + k4.position);
        Vector3 newVelocity = currentState.velocity + (deltaTime / 6f) * (k1.velocity + 2f * k2.velocity + 2f * k3.velocity + k4.velocity);

        return new OrbitalState(newPosition, newVelocity);
    }

    private OrbitalState CalculateDerivatives(OrbitalState state, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse = default)
    {
        Vector3 acceleration = ComputeAccelerationFromData(state.position, bodyPositions, thrustImpulse);
        return new OrbitalState(state.velocity, acceleration);
    }

    private Vector3 ComputeAccelerationFromData(Vector3 position, Dictionary<NBody, Vector3> bodyPositions, Vector3 thrustImpulse)
    {
        Vector3 totalForce = Vector3.zero;
        float minDistance = 0.001f;  // Prevent divide-by-zero issues.

        foreach (var body in bodyPositions.Keys)
        {
            Vector3 direction = bodyPositions[body] - position;
            float distanceSquared = Mathf.Max(direction.sqrMagnitude, minDistance * minDistance);

            float forceMagnitude = PhysicsConstants.G * (body.mass) / distanceSquared;
            totalForce += direction.normalized * forceMagnitude;
        }

        Vector3 externalAcceleration = thrustImpulse;
        return (totalForce) + externalAcceleration;
    }
}