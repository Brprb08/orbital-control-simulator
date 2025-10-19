using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

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

    private SimContext ctx;

    /// <summary>
    /// Initializes the service with a simulation context and discovers bodies in the scene.
    /// Bodies are ordered so the central body (if any) is processed first, then by name.
    /// </summary>
    /// <param name="ctx">Active simulation context used to initialize bodies and related subsystems.</param>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;

        foreach (var body in FindObjectsByType<NBody>(FindObjectsSortMode.None)
                 .OrderByDescending(b => b.isCentralBody)
                 .ThenBy(b => b.name, StringComparer.OrdinalIgnoreCase))
        {
            Register(body);
            ctx.LineVisibilityController.RegisterNBody(body);
        }
    }

    /// <summary>
    /// Raised when a body is registered.
    /// </summary>
    public event Action<NBody> BodyAdded;

    /// <summary>
    /// Raised when a body is deregistered.
    /// </summary>
    public event Action<NBody> BodyRemoved;

    private NBody _central;

    /// <summary>
    /// The current central body (if any). Set during registration when a body is flagged as central.
    /// </summary>
    public NBody CentralBody => _central;

    /// <summary>
    /// Registers a body with the service. Safe to call multiple times (no-op if already registered).
    /// Ensures the body is initialized against the current simulation context.
    /// Updates the central body reference when applicable and notifies listeners.
    /// </summary>
    /// <param name="body">The NBody to register.</param>
    public void Register(NBody body)
    {
        if (!body) return;
        if (_bodies.Contains(body)) return;

        if (ctx == null) Debug.LogError("[BodyService] ctx is NULL at Register!");
        body.Initialize(ctx);  // must be idempotent

        _bodies.Add(body);
        if (body.isCentralBody) _central = body;

        if (!CentralBody)
        {
            if (!body.TryGetComponent(out AttitudeController att))
                att = body.gameObject.AddComponent<AttitudeController>();

            att.Initialize(ctx);
        }
        Debug.Log($"[BodyService] Registered {body.name} tag={body.tag} central={body.isCentralBody} total={_bodies.Count}");
        BodyAdded?.Invoke(body);
    }

    /// <summary>
    /// Removes a body from the service. No-op if the body is unknown.
    /// Clears the central body reference if the removed body was central and notifies listeners.
    /// </summary>
    /// <param name="body">The <see NBody to deregister.</param>
    public void Deregister(NBody body)
    {
        if (!body) return;
        if (!_bodies.Remove(body)) return;
        if (body == _central) _central = null;
        BodyRemoved?.Invoke(body);
    }

    /// <summary>
    /// Returns all registered bodies tagged as <c>Satellite</c>.
    /// </summary>
    public IReadOnlyList<NBody> GetSatellites()
        => _bodies.Where(b => b.CompareTag("Satellite")).ToList();
}