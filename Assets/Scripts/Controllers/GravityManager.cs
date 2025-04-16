using UnityEngine;
using System.Collections.Generic;
using TMPro;

/**
* GravityManager class handles the registration, deregistration, and management of celestial bodies.
* It tracks all NBody objects in the scene and provides access to their states.
**/
public class GravityManager : MonoBehaviour
{
    public static GravityManager Instance { get; private set; }

    public NBody CentralBody { get; private set; }

    private List<NBody> bodies = new List<NBody>();

    public float minCollisionDistance = 0.5f;

    public List<NBody> Bodies => bodies;

    public TMP_Dropdown bodyDropdown;

    /**
    * Initializes the singleton instance of GravityManager.
    **/
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

        bodyDropdown.ClearOptions();
    }

    /**
    * Registers all pre-existing NBody objects in the scene.
    **/
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
    * @param body - The NBody object to register.
    **/
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
            Debug.Log($"Registered NBody with LineVisibilityManager: {body.gameObject.name}");
        }
        else
        {
            Debug.LogError("LineVisibilityManager.Instance is null. Ensure LineVisibilityManager is in the scene.");
        }
    }

    /**
    * Deregisters an NBody object.
    * @param body - The NBody object to deregister.
    **/
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

    /**
    * Handles a collision between two NBody objects and removes the smaller body.
    * @param bodyA - The first NBody involved in the collision.
    * @param bodyB - The second NBody involved in the collision.
    **/
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

        Debug.Log($"Removed {bodyToRemove.name} due to collision.");
    }
}