// BodyService.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class BodyService : MonoBehaviour, IBodyService
{
    private readonly List<NBody> _bodies = new List<NBody>();
    public IReadOnlyList<NBody> Bodies => _bodies;

    private NBody _central;
    public NBody CentralBody
    {
        get => _central;
        private set
        {
            if (_central == value) return;
            _central = value;
            CentralBodyChanged?.Invoke(_central);
        }
    }

    public event Action<NBody> BodyAdded;
    public event Action<NBody> BodyRemoved;
    public event Action<NBody> CentralBodyChanged;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
    }

    public void Register(NBody body)
    {
        if (!body) return;
        if (_bodies.Contains(body)) return;

        _bodies.Add(body);
        if (body.isCentralBody) CentralBody = body;

        BodyAdded?.Invoke(body);
    }

    public void Deregister(NBody body)
    {
        if (!body) return;
        if (!_bodies.Remove(body)) return;

        if (body == _central) CentralBody = null;
        BodyRemoved?.Invoke(body);
    }

    public IReadOnlyList<NBody> GetSatellites()
        => _bodies.Where(b => b.CompareTag("Satellite")).ToList();
}
