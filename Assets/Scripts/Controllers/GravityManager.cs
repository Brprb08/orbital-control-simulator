using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System.Linq;

/// <summary>
/// Manages the registration, deregistration, and tracking of celestial bodies (NBody objects).
/// Tracks all NBody instances in the scene and provides access to their states.
/// </summary>
public class GravityManager : MonoBehaviour
{
    public static GravityManager Instance { get; private set; }

    public NBody CentralBody { get; private set; }

    private List<NBody> bodies = new List<NBody>();

    public float minCollisionDistance = 0.5f;

    public List<NBody> Bodies => bodies;

    public TMP_Dropdown bodyDropdown;

    /// <summary>
    /// Initializes the singleton instance of the GravityManager.
    /// </summary>
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        bodyDropdown.ClearOptions();
    }

    /// <summary>
    /// Registers all existing NBody objects in the scene on startup.
    /// </summary>
    void Start()
    {
        NBody[] allBodies = FindObjectsByType<NBody>(FindObjectsSortMode.None);
        foreach (var body in allBodies.OrderByDescending(b => b.isCentralBody))
        {
            RegisterBody(body);
        }
    }

    /// <summary>
    /// Registers a new NBody object into the simulation.
    /// </summary>
    /// <param name="body">The NBody object to register.</param>
    public void RegisterBody(NBody body)
    {
        if (body.isCentralBody) CentralBody = body;

        if (!bodies.Contains(body))
        {
            bodies.Add(body);
            if (body.name != "Earth")
            {
                bodyDropdown.options.Add(new TMP_Dropdown.OptionData(body.name));
                bodyDropdown.RefreshShownValue();
            }
        }

        if (LineVisibilityManager.Instance != null)
        {
            LineVisibilityManager.Instance.RegisterNBody(body);
            Debug.Log($"[GRAVITY MANAGER]: Registered NBody with LineVisibilityManager: {body.gameObject.name}");
        }
        else
        {
            Debug.LogError("[GRAVITY MANAGER]: LineVisibilityManager.Instance is null. Ensure LineVisibilityManager is in the scene.");
        }
    }

    /// <summary>
    /// Deregisters an NBody object from the simulation.
    /// </summary>
    /// <param name="body">The NBody object to deregister.</param>
    public void DeregisterBody(NBody body)
    {
        if (body == CentralBody) CentralBody = null;

        if (bodies.Contains(body))
        {
            bodies.Remove(body);
        }

        int indexToRemove = bodyDropdown.options.FindIndex(option => option.text == body.name);
        if (indexToRemove != -1)
        {
            bodyDropdown.options.RemoveAt(indexToRemove);
            bodyDropdown.RefreshShownValue();
        }
    }

    /// <summary>
    /// Handles a collision between two bodies by removing the one with lesser mass.
    /// </summary>
    /// <param name="bodyA">The first body involved in the collision.</param>
    /// <param name="bodyB">The second body involved in the collision.</param>
    public void HandleCollision(NBody bodyA, NBody bodyB)
    {
        NBody bodyToRemove = (bodyA.mass < bodyB.mass) ? bodyA : bodyB;

        CameraController cameraController = GravityManager.Instance.GetComponent<CameraController>();
        if (cameraController != null && cameraController.IsTracking(bodyToRemove))
        {
            cameraController.SwitchToNextValidBody(bodyToRemove);
        }

        DeregisterBody(bodyToRemove);
        Debug.Log(bodyToRemove.gameObject);
        Destroy(bodyToRemove.gameObject);

        if (cameraController != null)
        {
            cameraController.RefreshBodiesList();
        }

        cameraController.UpdateDropdownSelection();

        Debug.Log($"[GRAVITY MANAGER]: Removed {bodyToRemove.name} due to collision.");
    }
}