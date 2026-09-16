using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>Discovers and registers simulation bodies and publishes membership changes.</summary>
public class BodyService : MonoBehaviour, IBodyService
{
    private readonly List<NBody> _bodies = new();
    public IReadOnlyList<NBody> Bodies => _bodies;
    public NBody CentralBody => _central;
    private NBody _central;
    private SimContext ctx;
    private BodyPhysicsStepper physicsStepper;
    private int _bulkRegistrationDepth;
    private bool _membershipDirty;

    // Retained here to preserve scene serialization and the existing fixed-update order.
    public bool DrivePhysics = true;

    public event Action<NBody> BodyAdded;
    public event Action<NBody> BodyRemoved;
    internal event Action MembershipChanged;

    public void Initialize(SimContext ctx)
    {
        physicsStepper?.Dispose();
        this.ctx = ctx;

        var bodies = FindObjectsByType<NBody>(FindObjectsSortMode.None)
            .OrderByDescending(b => b.isCentralBody)
            .ThenBy(b => b.name, StringComparer.OrdinalIgnoreCase);

        foreach (var body in bodies)
            Register(body);

        physicsStepper = new BodyPhysicsStepper(this, ctx);
    }

    private void FixedUpdate()
    {
        if (DrivePhysics)
            physicsStepper?.Step(Mathf.Max(0f, Time.fixedDeltaTime));
    }

    private void OnDestroy() => physicsStepper?.Dispose();

    public void Register(NBody body)
    {
        if (!body || _bodies.Contains(body)) return;
        if (ctx == null) { Debug.LogError("[BodyService] ctx is NULL at Register!"); return; }

        body.Initialize(ctx);
        SatelliteSizing.ApplyVisualScale(body);

        _bodies.Add(body);
        if (body.isCentralBody) { _central = body; }

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

        NotifyMembershipChanged();
    }

    public void Deregister(NBody body)
    {
        if (!body) return;
        if (!_bodies.Remove(body)) return;

        if (body == _central) { _central = null; }

        BodyRemoved?.Invoke(body);
        NotifyMembershipChanged();
    }

    public void BeginBulkRegistration()
    {
        _bulkRegistrationDepth++;
    }

    public void EndBulkRegistration()
    {
        if (_bulkRegistrationDepth <= 0)
            return;

        _bulkRegistrationDepth--;
        if (_bulkRegistrationDepth > 0)
            return;

        if (_membershipDirty)
        {
            _membershipDirty = false;
            MembershipChanged?.Invoke();
        }
    }

    private void NotifyMembershipChanged()
    {
        if (_bulkRegistrationDepth > 0)
        {
            _membershipDirty = true;
            return;
        }

        MembershipChanged?.Invoke();
    }

    /// <summary>
    /// Returns all registered bodies tagged as <c>Satellite</c>.
    /// </summary>
    public IReadOnlyList<NBody> GetSatellites()
        => _bodies.Where(b => b.CompareTag("Satellite")).ToList();

}
