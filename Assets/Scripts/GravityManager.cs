/**
 * GravityManager class handles the registration, deregistration, and management of celestial bodies.
 * It tracks all NBody objects in the scene and provides access to their states.
 */
using UnityEngine;
using System.Collections.Generic;

public class GravityManager : MonoBehaviour
{
    public static GravityManager Instance { get; private set; }
    private List<NBody> bodies = new List<NBody>();

    public float minCollisionDistance = 0.5f; // In scaled units (1 unit = 10 km)

    public List<NBody> Bodies => bodies;

    /**
     * Initializes the singleton instance of GravityManager.
     */
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

    /**
     * Registers all pre-existing NBody objects in the scene.
     */
    void Start()
    {
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

    /**
     * Registers a new NBody object.
     * @param body The NBody object to register.
     */
    public void RegisterBody(NBody body)
    {
        if (!bodies.Contains(body))
        {
            bodies.Add(body);
        }
    }

    /**
     * Deregisters an NBody object.
     * @param body The NBody object to deregister.
     */
    public void DeregisterBody(NBody body)
    {
        if (bodies.Contains(body))
        {
            bodies.Remove(body);
        }
    }

    /**
     * Handles a collision between two NBody objects and removes the smaller body.
     * @param bodyA The first NBody involved in the collision.
     * @param bodyB The second NBody involved in the collision.
     */
    void HandleCollision(NBody bodyA, NBody bodyB)
    {
        NBody bodyToRemove = (bodyA.mass < bodyB.mass) ? bodyA : bodyB;

        CameraController cameraController = GravityManager.Instance.GetComponent<CameraController>();
        if (cameraController != null && cameraController.IsTracking(bodyToRemove))
        {
            cameraController.SwitchToNextValidBody(bodyToRemove);
        }

        DeregisterBody(bodyToRemove);
        Destroy(bodyToRemove.gameObject);

        if (cameraController != null)
        {
            cameraController.RefreshBodiesList();
        }

        Debug.Log($"Removed {bodyToRemove.name} due to collision.");
    }
}