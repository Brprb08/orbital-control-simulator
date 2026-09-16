using System.Collections.Generic;

public sealed class ConstellationRecord
{
    private readonly List<ConstellationPlaneRecord> planes;

    public string Id { get; }
    public ConstellationDefinition Definition { get; }
    public IReadOnlyList<ConstellationPlaneRecord> Planes => planes;

    public string Name => Definition.NamePrefix;

    public ConstellationRecord(
        string id,
        ConstellationDefinition definition,
        List<ConstellationPlaneRecord> planes)
    {
        Id = id;
        Definition = definition;
        this.planes = planes;
    }

    public ConstellationPlaneRecord GetPlane(int planeIndex)
    {
        if (planeIndex < 0 || planeIndex >= planes.Count)
            return null;

        return planes[planeIndex];
    }
}
