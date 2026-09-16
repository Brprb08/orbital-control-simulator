using System.Collections.Generic;

public sealed class ConstellationPlaneRecord
{
    private readonly List<NBody> members = new();

    public string ConstellationId { get; }
    public string ConstellationName { get; }
    public int PlaneIndex { get; }
    public IReadOnlyList<NBody> Members => members;

    public string DisplayName => $"{ConstellationName} Plane {PlaneIndex + 1}";

    public ConstellationPlaneRecord(string constellationId, string constellationName, int planeIndex)
    {
        ConstellationId = constellationId;
        ConstellationName = constellationName;
        PlaneIndex = planeIndex;
    }

    public void AddMember(NBody body)
    {
        if (body != null && !members.Contains(body))
            members.Add(body);
    }

    // Only removal from the simulation changes membership; maneuvering does not.
    internal void RemoveMember(NBody body) => members.Remove(body);

    public NBody RepresentativeBody
    {
        get
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] != null)
                    return members[i];
            }

            return null;
        }
    }
}
