using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Mathematics;

/// <summary>
/// Tracks and manages NBody instances in the scene.
/// Handles discovery, registration, and deregistration; exposes lookup utilities and events.
/// Centralizes ownership so other systems can observe body lifecycle changes.
/// </summary>
public class BodyService : MonoBehaviour, IBodyService
{
    private readonly List<NBody> _bodies = new();

    /// <summary>
    /// All registered bodies in the current simulation context.
    /// </summary>
    public IReadOnlyList<NBody> Bodies => _bodies;
    public bool UseCentralStepping = true;

    // caches for batch call
    private double3[] _posD;
    private double3[] _velD;
    private double[] _mass;
    private Vector3[] _thrust;

    private List<NBody> _satCache = new(); // satellites only

    private double3[] _posBuf;
    private double3[] _velBuf;
    private double[] _massBuf;
    private Vector3[] _thrustBuf;
    private float[] _cdBuf;
    private float[] _areaBuf;

    public bool DrivePhysics = true;

    private NBody _central;
    private double _muUnity; // cached μ (= G*M) in Unity units
    private const double G_unity = 6.67430e-23; // 1u = 10 km

    private byte[] _isThrustingBuf;
    private Vector3 _nodeBurnVCache = Vector3.right;
    private Vector3 _nodeBurnHCache = Vector3.up;
    private sbyte[] _latchedParityBuf; // −1/0/+1

    private const float EarthRotationRate = 360f / 86164f; // deg/sec, sidereal
    private const float MaxDistanceFromEarth = 40000f;
    private const float AttitudeLeadTime = 20f;

    // /// <summary>
    /// Raised when a body is registered.
    /// </summary>
    public event Action<NBody> BodyAdded;

    /// <summary>
    /// Raised when a body is deregistered.
    /// </summary>
    public event Action<NBody> BodyRemoved;

    /// <summary>
    /// The current central body (if any). Set during registration when a body is flagged as central.
    /// </summary>
    public NBody CentralBody => _central;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;

        var bodies = FindObjectsByType<NBody>(FindObjectsSortMode.None)
            .OrderByDescending(b => b.isCentralBody)
            .ThenBy(b => b.name, StringComparer.OrdinalIgnoreCase);

        foreach (var body in bodies)
            Register(body);

        RebuildSatelliteCache();
        UpdateMu();
    }

    private void EnsureBatchBuffers(int n)
    {
        if (_posD == null || _posD.Length != n)
        {
            _posD = new double3[n];
            _velD = new double3[n];
            _mass = new double[n];
            _thrust = new Vector3[n];
        }
    }

    private void FixedUpdate()
    {
        if (!DrivePhysics) return;
        PrepareBodyStep();
        StepAllBodiesBatch();
    }

    private void PrepareBodyStep()
    {
        for (int i = 0; i < _bodies.Count; i++)
        {
            NBody body = _bodies[i];
            if (body == null)
                continue;

            ReportNaNPosition(body);

            if (body.isCentralBody)
            {
                RotateCentralBodyVisual(body);
                continue;
            }

            if (!body.isReferenceOrbit)
                UpdateNodeBurnLifecycle(body);
        }
    }

    private void ReportNaNPosition(NBody body)
    {
        Vector3 pos = body.transform.position;
        if (!float.IsNaN(pos.x) && !float.IsNaN(pos.y) && !float.IsNaN(pos.z))
            return;

        Debug.LogError(
            $"[NBODY]: {body.name} has NaN transform.position! " +
            $"velocity={body.velocity}, force={body.state.force}"
        );
    }

    private static void RotateCentralBodyVisual(NBody body)
    {
        float deltaAngle = -EarthRotationRate * Time.deltaTime;
        body.transform.Rotate(Vector3.up, deltaAngle);
    }

    private void UpdateNodeBurnLifecycle(NBody body)
    {
        ManeuverNodeManager nodeManager = ctx?.ManeuverNodeManager;
        BodyRuntimeCoordinator runtime = ctx?.BodyRuntimeCoordinator;
        ThrustController thrustController = ctx?.ThrustController;

        if (body == null || nodeManager == null || runtime == null)
            return;

        float simTime = runtime.simulationTime;
        int currentStep = runtime.simulationStep;
        bool burnInProgress = false;
        bool shouldRemoveNode = false;

        ManeuverNode node = nodeManager.CurrentNode;
        AttitudeController attitude = body.GetComponent<AttitudeController>();

        if (node != null && node.targetBody == body && node.isFinalized)
        {
            UpdateNodeBurnAttitude(attitude, node, simTime, currentStep);

            if (currentStep >= node.burnStartStep)
            {
                if (ManeuverBurnMath.IsBurnActiveForStep(node, body, currentStep))
                {
                    thrustController?.StartNodeBurn(node);
                    burnInProgress = true;
                }
                else
                {
                    thrustController?.StopAllThrust();
                    shouldRemoveNode = true;
                }
            }
        }
        else if (attitude != null)
        {
            attitude.lockNormalParity = false;
        }

        UpdateBurnEffects(body, burnInProgress, thrustController);

        if (shouldRemoveNode)
            nodeManager.RemoveNode(node);
    }

    private static void UpdateNodeBurnAttitude(
        AttitudeController attitude,
        ManeuverNode node,
        float simTime,
        int currentStep)
    {
        if (attitude == null || node == null)
            return;

        bool inBurnPhase =
            simTime >= node.burnTime - AttitudeLeadTime &&
            currentStep < node.burnStartStep + node.burnStepCount;

        if (inBurnPhase)
        {
            AttitudeController.PointingMode desiredMode = MapBurnTypeToAttitude(node.burnType);
            if (attitude.mode != desiredMode)
                attitude.SetMode(desiredMode);

            attitude.lockNormalParity = true;
            return;
        }

        attitude.lockNormalParity = false;
    }

    private void UpdateBurnEffects(NBody body, bool burnInProgress, ThrustController thrustController)
    {
        if (burnInProgress)
        {
            if (!body.isThrusting)
            {
                ctx?.RocketThrustAudio?.StartThrust();
                body.isThrusting = true;
            }

            return;
        }

        if (!body.isThrusting)
            return;

        ctx?.RocketThrustAudio?.StopThrust();
        thrustController?.StopAllThrust();
        body.isThrusting = false;
    }

    private static AttitudeController.PointingMode MapBurnTypeToAttitude(BurnType burnType)
    {
        switch (burnType)
        {
            case BurnType.Prograde:
                return AttitudeController.PointingMode.Velocity;

            case BurnType.Retrograde:
                return AttitudeController.PointingMode.Retrograde;

            case BurnType.RadialIn:
                return AttitudeController.PointingMode.Nadir;

            case BurnType.RadialOut:
                return AttitudeController.PointingMode.Zenith;

            case BurnType.Normal:
                return AttitudeController.PointingMode.Normal;

            case BurnType.AntiNormal:
                return AttitudeController.PointingMode.AntiNormal;

            default:
                return AttitudeController.PointingMode.Velocity;
        }
    }

    private void SetCentralBody(NBody earth)
    {
        double massEarthKg = (earth != null) ? earth.trueMass : 5.972e24;
        _muUnity = G_unity * massEarthKg;
    }

    private void EnsureArraysForN(int n)
    {
        void Alloc<T>(ref T[] arr, int len) { if (arr == null || arr.Length != len) arr = (len > 0) ? new T[len] : Array.Empty<T>(); }

        Alloc(ref _posBuf, n);
        Alloc(ref _velBuf, n);
        Alloc(ref _massBuf, n);
        Alloc(ref _thrustBuf, n);
        Alloc(ref _cdBuf, n);
        Alloc(ref _areaBuf, n);

        if (_isThrustingBuf == null || _isThrustingBuf.Length != n)
            _isThrustingBuf = new byte[n]; // zeroed

        if (_latchedParityBuf == null || _latchedParityBuf.Length != n)
            _latchedParityBuf = new sbyte[n]; // zeroed 

    }

    private void StepAllBodiesBatch()
    {
        if (_satCache == null || _satCache.Count == 0) return;
        int n = _satCache.Count;

        EnsureArraysForN(n);
        var normalSign = new sbyte[n];
        ManeuverNode node = ctx?.ManeuverNodeManager != null ? ctx.ManeuverNodeManager.CurrentNode : null;
        int simulationStep = ctx?.BodyRuntimeCoordinator != null ? ctx.BodyRuntimeCoordinator.simulationStep : 0;
        float nodeBurnThrustMagnitude = ctx?.ThrustController != null
            ? ctx.ThrustController.EffectiveForwardThrustMagnitude
            : 0f;
        bool nodeBurnActiveThisStep = node != null &&
                                      node.isFinalized &&
                                      simulationStep >= node.burnStartStep &&
                                      simulationStep < node.burnStartStep + node.burnStepCount;
        if (nodeBurnActiveThisStep)
            ctx?.ThrustController?.EnsureThrustTimeScaleLimit(showNodeFeedback: true);

        Vector3 center = _central != null
            ? _central.state.position.ToVector3()
            : Vector3.zero;

        for (int i = 0; i < n; i++)
        {
            var b = _satCache[i];
            if (b == null)
            {
                _posBuf[i] = default; _velBuf[i] = default; _massBuf[i] = 0.0;
                _thrustBuf[i] = Vector3.zero; _cdBuf[i] = 0f; _areaBuf[i] = 0f;
                normalSign[i] = 0;
                _isThrustingBuf[i] = 0;              // not thrusting
                                                     // _latchedParityBuf[i] stays as-is (but native will clear when not thrusting)
                continue;
            }

            _posBuf[i] = b.state.position;
            _velBuf[i] = b.state.velocity;
            _massBuf[i] = b.state.mass;

            // Raw commanded thrust from AttitudeController / ThrustController
            _thrustBuf[i] = b.state.force;

            var att = b.GetComponent<AttitudeController>();

            if (ManeuverBurnMath.IsBurnActiveForStep(node, b, simulationStep) &&
                ManeuverBurnMath.TryBuildBurnCommand(
                    node.burnType,
                    b.state.position.ToVector3(),
                    b.state.velocity.ToVector3(),
                    center,
                    nodeBurnThrustMagnitude,
                    ref _nodeBurnVCache,
                    ref _nodeBurnHCache,
                    out Vector3 nodeBurnForce,
                    out sbyte nodeBurnNormalSign))
            {
                _thrustBuf[i] += nodeBurnForce;
                normalSign[i] = nodeBurnNormalSign;
            }
            else if (att != null && att.mode == AttitudeController.PointingMode.Normal)
            {
                normalSign[i] = +1;   // Normal
            }
            else if (att != null && att.mode == AttitudeController.PointingMode.AntiNormal)
            {
                normalSign[i] = -1;   // AntiNormal
            }
            else
            {
                normalSign[i] = 0;    // Free thrust: native uses thrust vector as-is
            }

            // any nonzero thrust
            bool thrusting = _thrustBuf[i].sqrMagnitude > 1e-12f;
            _isThrustingBuf[i] = thrusting ? (byte)1 : (byte)0;

            // going to ignore latched parity in native now, keep it cleared.
            if (_latchedParityBuf != null)
                _latchedParityBuf[i] = 0;

            // Drag inputs
            _cdBuf[i] = (float)b.dragCoefficient;

            float areaUU = (float)b.state.crossSectionArea;
            if (!(areaUU > 0f))
            {
                double rUU = b.radius;
                areaUU = (float)(Math.PI * rUU * rUU);
            }
            _areaBuf[i] = areaUU;
        }

        const float dtMax = 0.02f;
        int substeps = Mathf.Max(1, Mathf.CeilToInt(Time.fixedDeltaTime / dtMax));

        NativePhysics.BatchTwoBodyIntegrateMuEx(
            _posBuf, _velBuf, _massBuf, _thrustBuf,
            _cdBuf, _areaBuf, normalSign,
            _isThrustingBuf, _latchedParityBuf,
            n, _muUnity, Time.fixedDeltaTime, substeps
        );

        // Write back & sync
        for (int i = 0; i < n; i++)
        {
            var b = _satCache[i];
            if (b == null) continue;
            double3 previousPosition = b.state.position;
            b.state.position = _posBuf[i];
            b.state.velocity = _velBuf[i];

            b.SyncAfterBatch(previousPosition);
            CheckPostStepRemoval(b);
        }

        ctx?.BodyRuntimeCoordinator?.AdvanceSimulationStep();
        ctx?.BodyRuntimeCoordinator?.FlushPendingRemovals();
    }

    private void CheckPostStepRemoval(NBody body)
    {
        if (body == null || body.isCentralBody)
            return;

        NBody earth = CentralBody;
        BodyRuntimeCoordinator runtime = ctx?.BodyRuntimeCoordinator;
        if (earth == null || earth == body || runtime == null)
            return;

        float distance = Vector3.Distance(body.transform.position, earth.transform.position);
        float collisionThreshold = body.cameraDistanceRadius + earth.radius;

        if (distance < collisionThreshold)
        {
            Debug.Log($"[NBODY]: [COLLISION] {body.name} collided with Earth");
            runtime.HandleCollision(body, earth);
            return;
        }

        if (distance > MaxDistanceFromEarth)
        {
            Debug.Log(
                $"[NBODY]: [ESCAPE] {body.name} exceeded {MaxDistanceFromEarth * 10f:N0} km and is removed."
            );

            runtime.HandleCollision(body, earth);
        }
    }

    private void RebuildSatelliteCache()
    {
        if (_satCache == null) _satCache = new List<NBody>(128);
        _satCache.Clear();

        for (int i = 0; i < _bodies.Count; i++)
        {
            var b = _bodies[i];
            if (b && !b.isCentralBody) _satCache.Add(b);
        }

        int n = _satCache.Count;

        // Allocate buffers for current native signature
        _posBuf = (n > 0) ? new double3[n] : Array.Empty<double3>();
        _velBuf = (n > 0) ? new double3[n] : Array.Empty<double3>();
        _massBuf = (n > 0) ? new double[n] : Array.Empty<double>();
        _thrustBuf = (n > 0) ? new Vector3[n] : Array.Empty<Vector3>();
        _cdBuf = (n > 0) ? new float[n] : Array.Empty<float>();
        _areaBuf = (n > 0) ? new float[n] : Array.Empty<float>();
    }

    public void Register(NBody body)
    {
        if (!body || _bodies.Contains(body)) return;
        if (ctx == null) { Debug.LogError("[BodyService] ctx is NULL at Register!"); return; }

        body.Initialize(ctx);
        SatelliteSizing.ApplyVisualScale(body);

        _bodies.Add(body);
        if (body.isCentralBody) { _central = body; UpdateMu(); }

        if (!body.TryGetComponent(out AttitudeController att))
            att = body.gameObject.AddComponent<AttitudeController>();

        if (!body.isCentralBody)
        {
            att.Initialize(ctx);
        }
        else
        {
            // Central body, no attitude logic
            att.enabled = false;
        }

        ctx.LineVisibilityController?.RegisterNBody(body);
        BodyAdded?.Invoke(body);

        RebuildSatelliteCache();
    }

    public void Deregister(NBody body)
    {
        if (!body) return;
        if (!_bodies.Remove(body)) return;

        if (body == _central) { _central = null; UpdateMu(); }

        BodyRemoved?.Invoke(body);
        RebuildSatelliteCache();
    }

    /// <summary>
    /// Returns all registered bodies tagged as <c>Satellite</c>.
    /// </summary>
    public IReadOnlyList<NBody> GetSatellites()
        => _bodies.Where(b => b.CompareTag("Satellite")).ToList();

    private void UpdateMu()
    {
        const double G_unity = 6.67430e-23;               // 1 unit = 10 km setup
        double m = (_central != null) ? _central.mass : 5.972e24; // fallback Earth
        _muUnity = G_unity * m;
    }
}
