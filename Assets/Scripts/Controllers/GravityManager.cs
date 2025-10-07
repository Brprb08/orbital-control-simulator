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
    private BodyService bodyService;
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
        bodyService = ctx.BodyService;                   // <-- use service
        lineVisibilityManager = ctx.LineVisibilityManager;
        bodyDropdownManager = ctx.BodyDropdownManager;

        // bootstrap discover & register
        foreach (var body in FindObjectsByType<NBody>(FindObjectsSortMode.None)
                 .OrderByDescending(b => b.isCentralBody)
                 .ThenBy(b => b.name, StringComparer.OrdinalIgnoreCase))
        {
            bodyService.Register(body);
            lineVisibilityManager?.RegisterNBody(body);
        }

        // listen for future changes, forward to line visibility / dropdown
        bodyService.BodyAdded += b => lineVisibilityManager?.RegisterNBody(b);
        bodyService.BodyRemoved += _ => bodyDropdownManager?.UpdateDropdownSelection();
        bodyService.CentralBodyChanged += _ => bodyDropdownManager?.UpdateDropdownSelection();
    }

    void FixedUpdate() => simulationTime += Time.fixedDeltaTime;

    // callers should ask the service now:
    public IReadOnlyList<NBody> GetAllSatellites() => bodyService.GetSatellites();

    public void HandleCollision(NBody a, NBody b)
    {
        var remove = (a.mass < b.mass) ? a : b;

        var tracker = ctx.CameraTracker;
        if (tracker != null && tracker.CurrentBody == remove)
        {
            var remaining = bodyService.GetSatellites().Where(x => x != remove).ToList();
            if (remaining.Count > 0) tracker.TrackBody(remaining[0]);
            else tracker.BreakToFreeCam();
        }

        bodyService.Deregister(remove);
        Destroy(remove.gameObject);

        bodyDropdownManager?.UpdateDropdownSelection();
        Debug.Log($"[GRAVITY]: Removed {remove.name} due to collision.");
    }
}