using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System;
using System.Linq;

/// <summary>
/// Manages the registration, deregistration, and tracking of celestial bodies (NBody objects).
/// Tracks all NBody instances in the scene and provides access to their states.
/// </summary>
public class GravityManager : MonoBehaviour
{
    [Header("References - Scripts")]
    public NBody CentralBody { get; private set; }
    private CameraController cameraController;
    private BodyDropdownManager bodyDropdownManager;
    private LineVisibilityManager lineVisibilityManager;
    private SimContext ctx;

    [Header("Body Tracking")]
    private List<NBody> bodies = new List<NBody>();
    public List<NBody> Bodies => bodies;

    [Header("Simulation Settings")]
    public float simulationTime = 0f;
    public float minCollisionDistance = 0.5f;

    [Header("References - UI")]
    public TMP_Dropdown bodyDropdown;

    /// <summary>
    /// Called by SimulationBootstrap once all public refs are set.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.lineVisibilityManager = ctx.LineVisibilityManager;
        this.bodyDropdownManager = ctx.BodyDropdownManager;

        bodyDropdown.ClearOptions();
        var allBodies = FindObjectsByType<NBody>(FindObjectsSortMode.None);

        foreach (var body in allBodies
        .OrderByDescending(b => b.isCentralBody)
        .ThenBy(b => b.name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(b => b.GetInstanceID()))
        {
            RegisterBody(body);
        }

        if (ctx.CameraController == null)
            Debug.LogError("GravityManager: CameraController missing from context!");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    void FixedUpdate()
    {
        simulationTime += Time.fixedDeltaTime;
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

        if (lineVisibilityManager != null)
        {
            lineVisibilityManager.RegisterNBody(body);
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

    public List<NBody> GetAllSatellites()
    {
        return bodies.FindAll(body => body.CompareTag("Satellite"));
    }

    /// <summary>
    /// Handles a collision between two bodies by removing the one with lesser mass.
    /// If the camera is tracking the removed one, hand off to another body or FreeCam.
    /// </summary>
    public void HandleCollision(NBody bodyA, NBody bodyB)
    {
        NBody bodyToRemove = (bodyA.mass < bodyB.mass) ? bodyA : bodyB;

        var tracker = ctx.CameraTracker; // Phase 3: use interface
        if (tracker != null && tracker.CurrentBody == bodyToRemove)
        {
            var remaining = GetAllSatellites();
            remaining.Remove(bodyToRemove);
            if (remaining.Count > 0)
            {
                tracker.TrackBody(remaining[0]);
            }
            else
            {
                tracker.BreakToFreeCam();
            }
        }

        DeregisterBody(bodyToRemove);
        Destroy(bodyToRemove.gameObject);

        if (bodyDropdownManager != null)
            bodyDropdownManager.UpdateDropdownSelection();

        Debug.Log($"[GRAVITY MANAGER]: Removed {bodyToRemove.name} due to collision.");
    }
}