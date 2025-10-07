// IBodyService.cs
using System;
using System.Collections.Generic;

public interface IBodyService
{
    IReadOnlyList<NBody> Bodies { get; }
    NBody CentralBody { get; }
    event Action<NBody> BodyAdded;
    event Action<NBody> BodyRemoved;
    event Action<NBody> CentralBodyChanged;

    void Register(NBody body);
    void Deregister(NBody body);
    IReadOnlyList<NBody> GetSatellites(); // convenience
}
