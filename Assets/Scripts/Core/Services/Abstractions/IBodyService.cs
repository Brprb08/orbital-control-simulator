// IBodyService.cs
using System;
using System.Collections.Generic;

// Interface used by BodyService
public interface IBodyService
{
    IReadOnlyList<NBody> Bodies { get; }
    NBody CentralBody { get; }
    event Action<NBody> BodyAdded;
    event Action<NBody> BodyRemoved;

    void Register(NBody body);
    void Deregister(NBody body);
    IReadOnlyList<NBody> GetSatellites(); // convenience
}
