using UnityEngine;
using System.Collections.Generic;

public class GravityManager : MonoBehaviour
{
    public static GravityManager Instance { get; private set; }
    private List<NBody> bodies = new List<NBody>();

    public float minCollisionDistance = 0.5f; // In scaled units (1 unit = 10 km)

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

                Vector3 direction = (bodyB.transform.position - bodyA.transform.position);
                float distanceSquared = direction.sqrMagnitude;

                float minDistanceSquared = minCollisionDistance * minCollisionDistance;
                if (distanceSquared <= minDistanceSquared)
                {
                    Debug.Log($"Collision detected between {bodyA.name} and {bodyB.name}!");
                    continue;
                }

                float forceMagnitude = PhysicsConstants.G * (bodyA.mass * bodyB.mass) / distanceSquared;
                Vector3 force = direction.normalized * forceMagnitude;

                bodyA.AddForce(force);
                bodyB.AddForce(-force);
            }
        }
    }
}