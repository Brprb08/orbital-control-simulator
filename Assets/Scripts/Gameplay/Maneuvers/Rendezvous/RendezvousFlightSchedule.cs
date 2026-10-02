using System;

/// <summary>Timing of the burns still ahead in an active rendezvous plan.</summary>
public static class RendezvousFlightSchedule
{
    public static bool TryGetLastBurnEnd(
        TrajectoryMatchedPredictor.EncounterBurn[] remainingBurns, out double end)
    {
        end = 0;
        if (remainingBurns == null || remainingBurns.Length == 0)
            return false;

        end = remainingBurns[remainingBurns.Length - 1].End;
        return double.IsFinite(end);
    }
}
