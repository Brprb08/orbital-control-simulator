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

    void Start()
    {
        // Register all pre-existing NBody objects in the scene
        NBody[] allBodies = FindObjectsByType<NBody>(FindObjectsSortMode.None);
        foreach (var body in allBodies)
        {
            if (!bodies.Contains(body))
            {
                bodies.Add(body);
                Debug.Log($"Registered pre-existing NBody: {body.gameObject.name}");
            }
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

                // Check for collision based on minCollisionDistance
                float combinedRadii = bodyA.radius + bodyB.radius;
                float combinedRadiiSquared = combinedRadii * combinedRadii;

                if (distanceSquared <= combinedRadiiSquared)
                {
                    Debug.Log($"Collision detected between {bodyA.name} and {bodyB.name}!");

                    HandleCollision(bodyA, bodyB);
                    continue;
                }

                float forceMagnitude = PhysicsConstants.G * (bodyA.mass * bodyB.mass) / distanceSquared;
                Vector3 force = direction.normalized * forceMagnitude;

                bodyA.AddForce(force);
                bodyB.AddForce(-force);
            }
        }
    }
    void HandleCollision(NBody bodyA, NBody bodyB)
    {
        // Example: Remove the smaller body in a collision
        NBody bodyToRemove = (bodyA.mass < bodyB.mass) ? bodyA : bodyB;

        // Check if the camera is tracking the body being removed
        CameraController cameraController = GravityManager.Instance.GetComponent<CameraController>();
        if (cameraController != null && cameraController.IsTracking(bodyToRemove))
        {
            // Switch the camera to track another body or free cam
            cameraController.SwitchToNextValidBody(bodyToRemove);
        }

        // Remove from GravityManager list
        DeregisterBody(bodyToRemove);

        // Destroy the GameObject
        Destroy(bodyToRemove.gameObject);

        // Refresh the camera's bodies list
        if (cameraController != null)
        {
            cameraController.RefreshBodiesList();
        }

        Debug.Log($"Removed {bodyToRemove.name} due to collision.");
    }
}