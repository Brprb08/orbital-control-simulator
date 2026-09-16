using UnityEngine;
using System.Collections.Generic;
using TMPro;
using System.Linq;
using System;
using Unity.Mathematics;

/// <summary>
/// Coordinates runtime state and management of NBody objects.
/// Links the BodyService, UI elements, and simulation systems to maintain
/// synchronized body registration, tracking, and removal during play.
/// </summary>
public class BodyRuntimeCoordinator : MonoBehaviour
{
    public const float BaseSimulationStep = 0.02f;
    private const float MaxDistanceFromEarth = SimulationLimits.MaxBodyDistanceUnits;

    [Header("References - Scripts")]
    public NBody CentralBody => bodyService != null ? bodyService.CentralBody : null;

    private BodyService bodyService;
    private SimContext ctx;
    private ConstellationNavigationController constellationNavigation;

    [Header("Body Tracking")]
    public IReadOnlyList<NBody> Bodies => bodyService != null ? bodyService.Bodies : Array.Empty<NBody>();

    [Header("Simulation Settings")]
    public int simulationStep = 0;
    [SerializeField] private float accumulatedSimulationTime;
    public float simulationTime => accumulatedSimulationTime;
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
        UnsubscribeBodyEvents();
        this.ctx = ctx;
        bodyService = ctx.BodyService;
        constellationNavigation = new ConstellationNavigationController(ctx);
        if (bodyService != null)
            bodyService.BodyRemoved += HandleBodyRemoved;
    }

    private void HandleBodyRemoved(NBody body) => ctx?.BodyDropdownManager?.UpdateDropdownSelection();

    private void UnsubscribeBodyEvents()
    {
        if (bodyService != null)
            bodyService.BodyRemoved -= HandleBodyRemoved;
    }

    private void OnDestroy() => UnsubscribeBodyEvents();

    /// <summary>
    /// Advances internal simulation time based on Unity’s fixed update step.
    /// </summary>
    public void AdvanceSimulationStep()
    {
        AdvanceSimulation(Time.fixedDeltaTime);
    }

    public void AdvanceSimulation(float deltaTime)
    {
        simulationStep++;
        accumulatedSimulationTime += Mathf.Max(0f, deltaTime);
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
        var remove = (a.TotalMassKilograms < b.TotalMassKilograms) ? a : b;
        QueueRemoval(remove);
    }

    internal void CheckPostStepRemoval(NBody body)
    {
        if (body == null || body.isCentralBody)
            return;

        NBody earth = bodyService != null ? bodyService.CentralBody : null;
        if (earth == null || earth == body)
            return;

        double3 offset = body.state.position - earth.state.position;
        double distanceSq = math.lengthsq(offset);
        double collisionThreshold = Math.Max(0.0, body.radius) + Math.Max(0.0, earth.radius);
        double collisionThresholdSq = collisionThreshold * collisionThreshold;

        if (distanceSq < collisionThresholdSq)
        {
            Debug.Log($"[NBODY]: [COLLISION] {body.name} collided with Earth");
            HandleCollision(body, earth);
            return;
        }

        if (distanceSq > MaxDistanceFromEarth * MaxDistanceFromEarth)
        {
            Debug.Log(
                $"[NBODY]: [ESCAPE] {body.name} exceeded {MaxDistanceFromEarth * SimulationUnits.KilometersPerUnit:N0} km and is removed."
            );

            HandleCollision(body, earth);
        }
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

        for (int i = 0; i < _pendingRemovals.Count; i++)
        {
            var remove = _pendingRemovals[i];
            if (remove == null) continue;
            if (!bodyService.Bodies.Contains(remove)) continue;

            remove.ForceStopBurnEffects();
            ClearNodeStateForBody(remove);

            RetargetBeforeRemoval(remove);

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
        NBody currentBody = tracker != null ? tracker.CurrentBody : null;
        if (currentBody == null)
            return;

        currentBody.ForceStopBurnEffects();
        ClearNodeStateForBody(currentBody);

        RetargetBeforeRemoval(currentBody);

        bodyService.Deregister(currentBody);
        Destroy(currentBody.gameObject);

        ctx.BodyDropdownManager.UpdateDropdownSelection();
    }

    private void RetargetBeforeRemoval(NBody removed)
    {
        var tracker = ctx.CameraTracker;
        if (tracker == null || tracker.CurrentBody != removed)
            return;

        NBody replacement = constellationNavigation.FindRemovalReplacement(removed, _pendingRemovalSet);
        if (replacement == null)
        {
            tracker.BreakToFreeCam();
            return;
        }

        bool removingConstellationMember = ctx.ConstellationRegistry != null &&
            ctx.ConstellationRegistry.IsConstellationMember(removed);
        if (removingConstellationMember && ctx.CameraController != null)
            ctx.CameraController.TrackBodyPreservingCameraMode(replacement);
        else
            tracker.TrackBody(replacement);
    }

    private void ClearNodeStateForBody(NBody body)
    {
        if (body == null || ctx == null)
            return;

        ManeuverNodeManager nodeManager = ctx.ManeuverNodeManager;
        ManeuverNode node = nodeManager != null ? nodeManager.CurrentNode : null;
        if (node == null || node.targetBody != body)
            return;

        nodeManager.ClearNode();
        ctx.UIRoot?.RefreshAllUi();
    }
}
