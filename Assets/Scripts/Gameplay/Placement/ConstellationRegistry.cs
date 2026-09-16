using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ConstellationRegistry : MonoBehaviour
{
    private readonly List<ConstellationRecord> constellations = new();
    private readonly Dictionary<NBody, ConstellationPlaneRecord> planeByBody = new();
    private readonly Dictionary<string, ConstellationRecord> constellationById = new();

    private BodyService bodyService;

    public event Action Changed;

    public IReadOnlyList<ConstellationRecord> Constellations => constellations;

    public void Initialize(SimContext ctx)
    {
        Unsubscribe();
        bodyService = ctx.BodyService;
        if (bodyService != null)
            bodyService.BodyRemoved += HandleBodyRemoved;
    }

    private void OnDestroy() => Unsubscribe();

    private void Unsubscribe()
    {
        if (bodyService != null)
            bodyService.BodyRemoved -= HandleBodyRemoved;
    }

    private void HandleBodyRemoved(NBody body)
    {
        if (ReferenceEquals(body, null) || !planeByBody.TryGetValue(body, out var plane))
            return;

        planeByBody.Remove(body);
        plane.RemoveMember(body);
        Changed?.Invoke();
    }

    public ConstellationRecord RegisterConstellation(
        string id,
        ConstellationDefinition definition,
        IReadOnlyList<NBody> members)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Constellation id is required.", nameof(id));

        if (definition.Planes <= 0)
            throw new ArgumentException("Constellation must have at least one plane.", nameof(definition));

        var planes = new List<ConstellationPlaneRecord>(definition.Planes);
        for (int plane = 0; plane < definition.Planes; plane++)
            planes.Add(new ConstellationPlaneRecord(id, definition.NamePrefix, plane));

        if (members != null)
        {
            for (int i = 0; i < members.Count; i++)
            {
                NBody body = members[i];
                if (body == null)
                    continue;

                var member = body.GetComponent<ConstellationMember>();
                if (member == null || member.constellationId != id)
                    continue;

                ConstellationPlaneRecord plane = member.planeIndex >= 0 && member.planeIndex < planes.Count
                    ? planes[member.planeIndex]
                    : null;

                if (plane == null)
                    continue;

                plane.AddMember(body);
                planeByBody[body] = plane;
            }
        }

        var record = new ConstellationRecord(id, definition, planes);
        constellations.Add(record);
        constellationById[id] = record;

        Changed?.Invoke();
        return record;
    }

    public bool TryGetConstellation(string id, out ConstellationRecord record)
    {
        if (!string.IsNullOrWhiteSpace(id) && constellationById.TryGetValue(id, out record))
            return true;

        record = null;
        return false;
    }

    public bool TryGetPlaneForBody(NBody body, out ConstellationRecord constellation, out ConstellationPlaneRecord plane)
    {
        constellation = null;
        plane = null;

        if (body == null || !planeByBody.TryGetValue(body, out plane) || plane == null)
            return false;

        return TryGetConstellation(plane.ConstellationId, out constellation);
    }

    public bool IsConstellationMember(NBody body)
    {
        return body != null && planeByBody.ContainsKey(body);
    }

    public bool HasDepartedIdeal(NBody body)
    {
        if (body == null || !planeByBody.ContainsKey(body))
            return false;

        var member = body.GetComponent<ConstellationMember>();
        return member != null && member.hasDepartedIdealSlot;
    }

    public void MarkDepartedIdeal(NBody body)
    {
        if (body == null || !planeByBody.ContainsKey(body))
            return;

        var member = body.GetComponent<ConstellationMember>();
        if (member == null)
            return;

        if (member.hasDepartedIdealSlot)
            return;

        member.MarkDepartedFromIdeal();
        Changed?.Invoke();
    }
}
