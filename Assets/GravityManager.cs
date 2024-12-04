using UnityEngine;
using System.Collections.Generic;

public class GravityManager : MonoBehaviour
{
    public static GravityManager Instance { get; private set; }
    private List<NBody> bodies = new List<NBody>();

    public float scale = 1.0f; // Scale to normalize distances (e.g., 1 unit = 1,000 km)
    public float minCollisionDistance = 0.5f; // Collision threshold (scaled)

    public List<NBody> Bodies => bodies;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void RegisterBody(NBody body)
    {
        if (!bodies.Contains(body))
        {
            bodies.Add(body);
        }
    }

    public void DeregisterBody(NBody body)
    {
        if (bodies.Contains(body))
        {
            bodies.Remove(body);
        }
    }

    void FixedUpdate()
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            for (int j = i + 1; j < bodies.Count; j++)
            {
                NBody bodyA = bodies[i];
                NBody bodyB = bodies[j];

                Vector3 direction = (bodyB.transform.position - bodyA.transform.position) / scale; // Scaled direction
                float distanceSquared = direction.sqrMagnitude;

                // Check for collisions
                float minDistanceSquared = minCollisionDistance * minCollisionDistance;
                if (distanceSquared <= minDistanceSquared)
                {
                    Debug.Log($"Collision detected between {bodyA.name} and {bodyB.name}!");
                    continue; // Skip force calculations if a collision occurs
                }

                float distance = Mathf.Sqrt(distanceSquared);
                Vector3 forceDirection = direction.normalized;

                // Calculate gravitational force
                float forceMagnitude = PhysicsConstants.G * (bodyA.mass * bodyB.mass) / distanceSquared;
                Vector3 force = forceDirection * forceMagnitude;

                // Apply forces to both bodies
                bodyA.AddForce(force);
                bodyB.AddForce(-force);
            }
        }
    }
}