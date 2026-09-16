using UnityEngine;

/// <summary>
/// Lightweight metadata attached to satellites spawned as part of a constellation.
/// The satellite remains a normal NBody; this component enables later grouped UI,
/// filtering, collision reports, and constellation-level controls.
/// </summary>
public sealed class ConstellationMember : MonoBehaviour
{
    public string constellationId;
    public string constellationName;
    public int planeIndex;
    public int slotIndex;
    public int memberIndex;
    public bool hasDepartedIdealSlot;

    public void Configure(
        string id,
        string name,
        int plane,
        int slot,
        int index)
    {
        constellationId = id;
        constellationName = name;
        planeIndex = plane;
        slotIndex = slot;
        memberIndex = index;
        hasDepartedIdealSlot = false;
    }

    public void MarkDepartedFromIdeal()
    {
        hasDepartedIdealSlot = true;
    }
}
