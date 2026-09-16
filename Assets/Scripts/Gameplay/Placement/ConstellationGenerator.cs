using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts a constellation definition into individual satellite spawn states.
/// This class has no Unity scene side effects, which keeps generation testable.
/// </summary>
public static class ConstellationGenerator
{
    public const int DefaultMaxSatellites = SimulationLimits.DefaultMaxConstellationSatellites;

    public readonly struct MemberSpawn
    {
        public readonly PlacementSpawnBuilder.SpawnData Spawn;
        public readonly int PlaneIndex;
        public readonly int SlotIndex;
        public readonly int MemberIndex;

        public MemberSpawn(
            PlacementSpawnBuilder.SpawnData spawn,
            int planeIndex,
            int slotIndex,
            int memberIndex)
        {
            Spawn = spawn;
            PlaneIndex = planeIndex;
            SlotIndex = slotIndex;
            MemberIndex = memberIndex;
        }
    }

    public static IReadOnlyList<MemberSpawn> Generate(
        ConstellationDefinition definition,
        double mu,
        double metersPerUnit,
        int maxSatellites = DefaultMaxSatellites)
    {
        if (!NBody.IsValidMassComposition(definition.Mass, definition.FuelMassKg))
            throw new ArgumentException("Invalid constellation dry/fuel mass.");
        if (definition.Planes <= 0)
            throw new ArgumentException("Plane count must be greater than zero.");

        if (definition.SatellitesPerPlane <= 0)
            throw new ArgumentException("Satellites per plane must be greater than zero.");

        long requested = (long)definition.Planes * definition.SatellitesPerPlane;
        if (maxSatellites <= 0 || requested > maxSatellites)
            throw new ArgumentException($"Constellation cannot exceed {maxSatellites} satellites.");
        int total = (int)requested;
        var spawns = new List<MemberSpawn>(total);

        for (int plane = 0; plane < definition.Planes; plane++)
        {
            double raanDeg = (definition.RaanStartDeg % 360.0) +
                             (definition.RaanSpreadDeg / definition.Planes) * plane;

            for (int slot = 0; slot < definition.SatellitesPerPlane; slot++)
            {
                int memberIndex = spawns.Count + 1;
                double slotAnomalyDeg = 360.0 * slot / definition.SatellitesPerPlane;
                double phaseDeg = (definition.WalkerPhase % total) * 360.0 * plane / total;
                double trueAnomalyDeg = WrapDegrees(
                    (definition.TrueAnomalyOffsetDeg % 360.0) +
                    slotAnomalyDeg +
                    phaseDeg
                );

                var (rEci, vEci) = KeplerUtils.FromElements(
                    definition.SemiMajorAxisMeters,
                    definition.Eccentricity,
                    definition.InclinationDeg,
                    raanDeg,
                    definition.ArgumentOfPerigeeDeg,
                    trueAnomalyDeg,
                    mu
                );

                string name = BuildMemberName(definition.NamePrefix, plane, slot);
                var spawn = new PlacementSpawnBuilder.SpawnData(
                    name,
                    definition.Mass,
                    FrameUtils.EciToUnity(rEci, metersPerUnit),
                    FrameUtils.VelEciToUnity(vEci, metersPerUnit),
                    definition.FuelMassKg
                );

                spawns.Add(new MemberSpawn(spawn, plane, slot, memberIndex));
            }
        }

        return spawns;
    }

    private static string BuildMemberName(string prefix, int planeIndex, int slotIndex)
    {
        return $"{prefix} P{planeIndex + 1:D2}-{slotIndex + 1:D2}";
    }

    private static double WrapDegrees(double degrees)
    {
        degrees %= 360.0;
        if (degrees < 0.0)
            degrees += 360.0;

        return degrees;
    }
}
