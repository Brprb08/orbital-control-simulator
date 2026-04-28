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
    public float minCollisionDistance = 0.5f;
    public int simulationStep = 0;
    public float simulationTime => simulationStep * Time.fixedDeltaTime;
    public bool IsNodeBurnInProgress =>
        ctx != null &&
        ctx.ThrustController != null &&
        ctx.ThrustController.IsNodeBurnActive;

    [Header("References - UI")]
    public TMP_Dropdown bodyDropdown;
    public ConfirmDialog confirmDialog;

    private readonly List<NBody> _pendingRemovals = new();
    private readonly HashSet<NBody> _pendingRemovalSet = new();

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
    public void AdvanceSimulationStep()
    {
        simulationStep++;
    }

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
        QueueRemoval(remove);
    }

    public void QueueRemoval(NBody body)
    {
        if (body == null) return;

        if (_pendingRemovalSet.Add(body))
        {
            _pendingRemovals.Add(body);
        }
    }

    public void FlushPendingRemovals()
    {
        if (_pendingRemovals.Count == 0)
            return;

        var tracker = ctx.CameraTracker;

        for (int i = 0; i < _pendingRemovals.Count; i++)
        {
            var remove = _pendingRemovals[i];
            if (remove == null) continue;
            if (!bodyService.Bodies.Contains(remove)) continue;

            remove.ForceStopBurnEffects();

            if (tracker != null && tracker.CurrentBody == remove)
            {
                ClearNodeStateForBody(remove);

                var remaining = bodyService
                    .GetSatellites()
                    .Where(x => x != remove && !_pendingRemovalSet.Contains(x))
                    .ToList();

                if (remaining.Count > 0)
                    tracker.TrackBody(remaining[0]);
                else
                    tracker.BreakToFreeCam();
            }

            bodyService.Deregister(remove);
            Destroy(remove.gameObject);

            Debug.Log($"[GRAVITY]: Removed {remove.name} due to collision.");
        }

        _pendingRemovals.Clear();
        _pendingRemovalSet.Clear();

        ctx.BodyDropdownManager?.UpdateDropdownSelection();
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
        ClearNodeStateForBody(currentBody);

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

    private void ClearNodeStateForBody(NBody body)
    {
        if (body == null || ctx == null)
            return;

        ManeuverNodeManager nodeManager = ctx.ManeuverNodeManager;
        ManeuverNode node = nodeManager != null ? nodeManager.CurrentNode : null;
        if (node == null || node.targetBody != body)
            return;

        body.ForceStopBurnEffects();
        nodeManager.ClearNode();
        ctx.UIRoot?.RefreshAllUi();
    }
}
