using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System.Linq;

/// <summary>
/// Coordinates runtime state and management of NBody objects.
/// Links the BodyService, UI elements, and simulation systems to maintain
/// synchronized body registration, tracking, and removal during play.
/// </summary>
public class BodyRuntimeCoordinator : MonoBehaviour
{
    [Header("References - Scripts")]
    public NBody CentralBody { get; private set; }

    private readonly BodyDropdownManager bodyDropdownManager;
    private LineVisibilityController lineVisibilityController;
    private BodyService bodyService;
    private SimContext ctx;

    [Header("Body Tracking")]
    private readonly List<NBody> bodies = new();
    public List<NBody> Bodies => bodies;

    [Header("Simulation Settings")]
    public float simulationTime = 0f;
    public float minCollisionDistance = 0.5f;

    [Header("References - UI")]
    public TMP_Dropdown bodyDropdown;
    public ConfirmDialog confirmDialog;

    /// <summary>
    /// Initializes connections between body services, visibility controllers,
    /// and dropdown managers. Called by <see SimulationBootstrap.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        bodyService = ctx.BodyService;
        lineVisibilityController = ctx.LineVisibilityController;

        // Subscribe to body lifecycle events to maintain UI and visual consistency.
        bodyService.BodyAdded += b => lineVisibilityController.RegisterNBody(b);
        bodyService.BodyRemoved += _ => ctx.BodyDropdownManager.UpdateDropdownSelection();
    }

    /// <summary>
    /// Advances internal simulation time based on Unity’s fixed update step.
    /// </summary>
    void FixedUpdate() => simulationTime += Time.fixedDeltaTime;

    /// <summary>
    /// Returns all satellite bodies from the <see BodyService.
    /// </summary>
    public IReadOnlyList<NBody> GetAllSatellites() => bodyService.GetSatellites();

    /// <summary>
    /// Handles body collisions, removes the lighter object, updates visuals and tracking,
    /// and ensures the camera and UI remain consistent.
    /// </summary>
    public void HandleCollision(NBody a, NBody b)
    {
        var remove = (a.mass < b.mass) ? a : b;

        var tracker = ctx.CameraTracker;
        if (tracker != null && tracker.CurrentBody == remove)
        {
            var remaining = bodyService.GetSatellites().Where(x => x != remove).ToList();
            if (remaining.Count > 0)
                tracker.TrackBody(remaining[0]);
            else
                tracker.BreakToFreeCam();
        }

        bodyService.Deregister(remove);
        Destroy(remove.gameObject);

        ctx.BodyDropdownManager.UpdateDropdownSelection();
        Debug.Log($"[GRAVITY]: Removed {remove.name} due to collision.");
    }

    public void RemoveSatellite()
    {
        confirmDialog.Show("Are you sure you want to remove this satellite?", () =>
        {
            ActuallyRemoveSatellite();
        });
    }

    private void ActuallyRemoveSatellite()
    {
        var tracker = ctx.CameraTracker;
        NBody currentBody = tracker.CurrentBody;
        if (tracker != null)
        {
            var remaining = bodyService.GetSatellites().Where(x => x != currentBody).ToList();
            if (remaining.Count > 0)
                tracker.TrackBody(remaining[0]);
            else
                tracker.BreakToFreeCam();
        }

        bodyService.Deregister(currentBody);
        Destroy(currentBody.gameObject);

        ctx.BodyDropdownManager.UpdateDropdownSelection();
    }
}
