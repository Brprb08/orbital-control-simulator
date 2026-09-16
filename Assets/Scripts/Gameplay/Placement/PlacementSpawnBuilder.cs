using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads placement UI fields and builds validated spawn data for each placement path.
/// </summary>
public sealed class PlacementSpawnBuilder
{
    public readonly struct ManualPlaceholderData
    {
        public readonly string Name;
        public readonly Vector3 Position;
        public readonly Vector3 RadiusMeters;
        public readonly float Mass; // Dry mass in kilograms.
        public readonly double FuelMassKg;

        public ManualPlaceholderData(string name, Vector3 position, Vector3 radiusMeters, float mass,
            double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
        {
            Name = name;
            Position = position;
            RadiusMeters = radiusMeters;
            Mass = mass;
            FuelMassKg = fuelMassKg;
        }
    }

    public readonly struct SpawnData
    {
        public readonly string Name;
        public readonly double Mass; // Dry mass in kilograms.
        public readonly double FuelMassKg;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;

        public SpawnData(string name, double mass, Vector3 position, Vector3 velocity,
            double fuelMassKg = SimulationLimits.DefaultSatelliteFuelMassKg)
        {
            Name = name;
            Mass = mass;
            FuelMassKg = fuelMassKg;
            Position = position;
            Velocity = velocity;
        }
    }

    public readonly struct TleSpawnData
    {
        public readonly SpawnData Spawn;
        public readonly DateTime WhenUtc;
        public readonly DateTime EpochUtc;

        public TleSpawnData(SpawnData spawn, DateTime whenUtc, DateTime epochUtc)
        {
            Spawn = spawn;
            WhenUtc = whenUtc;
            EpochUtc = epochUtc;
        }
    }

    public readonly struct ConstellationSpawnData
    {
        public readonly ConstellationDefinition Definition;
        public readonly IReadOnlyList<ConstellationGenerator.MemberSpawn> Members;

        public ConstellationSpawnData(
            ConstellationDefinition definition,
            IReadOnlyList<ConstellationGenerator.MemberSpawn> members)
        {
            Definition = definition;
            Members = members;
        }
    }

    public static readonly PlacementValidators.RangeF MassRange = new(SimulationLimits.MinSatelliteMassKg, SimulationLimits.MaxSatelliteMassKg);
    public static readonly PlacementValidators.RangeF RadiusClamp = new(
        SatelliteSizing.MinPhysicalRadiusMeters,
        SatelliteSizing.MaxPhysicalRadiusMeters
    );
    public static readonly PlacementValidators.DistanceBoundsF PositionBounds = new(SimulationLimits.ManualPlacementMinDistanceUnits, SimulationLimits.ManualPlacementMaxDistanceUnits);

    private const int MaxSatelliteNameLength = 15;
    private const int MaxConstellationPrefixLength = 12;

    private readonly PlacementFieldsUI fields;
    private readonly Camera mainCamera;
    private readonly SatelliteSpawner satelliteSpawner;
    private readonly double metersPerUnit;
    private readonly double mu;
    private readonly double earthRadiusMeters;

    public PlacementSpawnBuilder(
        PlacementFieldsUI fields,
        Camera mainCamera,
        SatelliteSpawner satelliteSpawner,
        double metersPerUnit,
        double mu,
        double earthRadiusMeters)
    {
        this.fields = fields;
        this.mainCamera = mainCamera;
        this.satelliteSpawner = satelliteSpawner;
        this.metersPerUnit = metersPerUnit;
        this.mu = mu;
        this.earthRadiusMeters = earthRadiusMeters;
    }

    public bool TryBuildManualPlaceholder(out ManualPlaceholderData data, out string error)
    {
        data = default;
        error = null;

        if (!PlacementValidators.TryGetName(
                fields.ObjectNameInputField,
                "Satellite",
                satelliteSpawner.SatelliteCount,
                MaxSatelliteNameLength,
                out string name,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetPositionOrDefault(
                fields.PositionInput,
                mainCamera != null ? mainCamera.transform : null,
                10f,
                PositionBounds,
                out Vector3 position,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetRadius(fields.RadiusInput, RadiusClamp, out Vector3 radius, out error))
            return false;

        if (!PlacementValidators.TryGetMass(fields.MassInput, MassRange, out float mass, out error))
            return false;
        if (!PlacementValidators.TryGetFuelMass(fields.FuelMassInput, mass, out double fuelKg, out error))
            return false;

        data = new ManualPlaceholderData(name, position, radius, mass, fuelKg);
        return true;
    }

    public bool TryBuildKeplerSpawn(out SpawnData data, out string error)
    {
        data = default;
        error = null;

        if (!PlacementValidators.TryGetName(
                fields.KepNameInputField,
                "Kepler Sat",
                satelliteSpawner.SatelliteCount + 1,
                MaxSatelliteNameLength,
                out string name,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetMass(fields.KepMassInputField, MassRange, out float mass, out error))
            return false;
        if (!PlacementValidators.TryGetFuelMass(fields.KepFuelMassInputField, mass, out double fuelKg, out error))
            return false;

        if (!PlacementValidators.TryGetDouble(fields.KepADegOrMetersInputField, out double aMeters))
        {
            error = "Invalid semi-major axis 'a'.";
            return false;
        }

        if (!PlacementValidators.TryGetDouble(fields.KepEccInputField, out double e) || e < 0.0 || e >= 1.0)
        {
            error = "Invalid eccentricity 'e'. Use 0 <= e < 1.";
            return false;
        }

        if (!PlacementValidators.TryGetDouble(fields.KepIncDegInputField, out double iDeg) ||
            !PlacementValidators.TryGetDouble(fields.KepRAANDegInputField, out double raanDeg) ||
            !PlacementValidators.TryGetDouble(fields.KepArgPDegInputField, out double argpDeg) ||
            !PlacementValidators.TryGetDouble(fields.KepTrueAnomDegInputField, out double trueAnomDeg))
        {
            error = "Invalid angle(s): i / RAAN / argument of perigee / true anomaly.";
            return false;
        }

        if (!PlacementSafety.TryValidateOrbit(aMeters, e, earthRadiusMeters, metersPerUnit, out error))
            return false;

        try
        {
            var (rEci, vEci) = KeplerUtils.FromElements(
                aMeters,
                e,
                iDeg,
                raanDeg,
                argpDeg,
                trueAnomDeg,
                mu
            );

            data = new SpawnData(
                name,
                mass,
                FrameUtils.EciToUnity(rEci, metersPerUnit),
                FrameUtils.VelEciToUnity(vEci, metersPerUnit),
                fuelKg
            );
            return ValidateSpawn(data, out error);
        }
        catch (Exception ex)
        {
            error = $"Kepler placement failed: {ex.Message}";
            return false;
        }
    }

    public bool TryBuildTleSpawn(out TleSpawnData data, out string error)
    {
        data = default;
        error = null;
        DateTime whenUtc = DateTime.UtcNow;

        if (!PlacementValidators.TryGetMass(fields.TleMassInputField, MassRange, out float mass, out error))
            return false;
        if (!PlacementValidators.TryGetFuelMass(fields.TleFuelMassInputField, mass, out double fuelKg, out error))
            return false;

        if (!PlacementValidators.TryGetName(fields.TleNameInputField, "TLE Sat",
                satelliteSpawner.NextSatelliteIndex, MaxSatelliteNameLength, out string name, out error))
            return false;

        if (!TLEParser.TryPropagate(
                PlacementValidators.GetText(fields.TleLine1InputField),
                PlacementValidators.GetText(fields.TleLine2InputField),
                whenUtc,
                out Vector3d rEciMeters,
                out Vector3d vEciMetersPerSecond,
                out DateTime epochUtc, out double tleA, out double tleE))
        {
            error = "Invalid TLE input or propagation failed.";
            return false;
        }

        if (!PlacementSafety.TryValidateOrbit(tleA, tleE, earthRadiusMeters, metersPerUnit, out error))
            return false;

        SpawnData spawn = new(
            name,
            mass,
            FrameUtils.EciToUnity(rEciMeters, metersPerUnit),
            FrameUtils.VelEciToUnity(vEciMetersPerSecond, metersPerUnit),
            fuelKg
        );
        if (!ValidateSpawn(spawn, out error)) return false;
        data = new TleSpawnData(spawn, whenUtc, epochUtc);
        return true;
    }

    public bool TryBuildConstellationSpawn(
        int maxSatellites,
        out ConstellationSpawnData data,
        out string error)
    {
        data = default;
        error = null;

        if (!PlacementValidators.TryGetName(
                fields.ConstellationNamePrefixInputField,
                "Const",
                satelliteSpawner.NextSatelliteIndex,
                MaxConstellationPrefixLength,
                out string prefix,
                out error))
        {
            return false;
        }

        if (!PlacementValidators.TryGetMass(fields.ConstellationMassInputField, MassRange, out float mass, out error))
            return false;
        if (!PlacementValidators.TryGetFuelMass(fields.ConstellationFuelMassInputField, mass, out double fuelKg, out error))
            return false;

        if (!PlacementValidators.TryGetDouble(fields.ConstellationSemiMajorAxisInputField, out double aMeters))
        {
            error = "Invalid constellation semi-major axis 'a'.";
            return false;
        }

        if (!PlacementValidators.TryGetDouble(fields.ConstellationEccInputField, out double e) || e < 0.0 || e >= 1.0)
        {
            error = "Invalid constellation eccentricity 'e'. Use 0 <= e < 1.";
            return false;
        }

        if (!PlacementValidators.TryGetDouble(fields.ConstellationIncDegInputField, out double incDeg) ||
            !PlacementValidators.TryGetDouble(fields.ConstellationArgPDegInputField, out double argpDeg) ||
            !PlacementValidators.TryGetDouble(fields.ConstellationRAANStartDegInputField, out double raanStartDeg) ||
            !PlacementValidators.TryGetDouble(fields.ConstellationRAANSpreadDegInputField, out double raanSpreadDeg))
        {
            error = "Invalid constellation angle(s): inclination / argument of perigee / RAAN start / RAAN spread.";
            return false;
        }

        if (!TryGetRequiredInt(fields.ConstellationPlanesInputField, "planes", out int planes, out error) ||
            !TryGetRequiredInt(fields.ConstellationSatellitesPerPlaneInputField, "satellites per plane", out int satsPerPlane, out error))
        {
            return false;
        }

        if (!TryGetOptionalInt(fields.ConstellationWalkerPhaseInputField, 1, "Walker phase", out int walkerPhase, out error) ||
            !TryGetOptionalDouble(
                fields.ConstellationTrueAnomalyOffsetDegInputField,
                0.0,
                "true anomaly offset",
                out double trueAnomalyOffsetDeg,
                out error))
        {
            return false;
        }

        if (planes <= 0 || satsPerPlane <= 0)
        {
            error = "Constellation planes and satellites per plane must both be greater than zero.";
            return false;
        }

        long totalSatellites = (long)planes * satsPerPlane;
        if (totalSatellites <= 0 || totalSatellites > maxSatellites)
        {
            error = $"Constellation size must be between 1 and {maxSatellites} satellites.";
            return false;
        }

        if (!PlacementSafety.TryValidateOrbit(aMeters, e, earthRadiusMeters, metersPerUnit, out error))
            return false;

        try
        {
            var definition = new ConstellationDefinition(
                prefix,
                mass,
                aMeters,
                e,
                incDeg,
                argpDeg,
                raanStartDeg,
                raanSpreadDeg,
                planes,
                satsPerPlane,
                walkerPhase,
                trueAnomalyOffsetDeg,
                fuelKg
            );

            var members = ConstellationGenerator.Generate(definition, mu, metersPerUnit, maxSatellites);
            foreach (var member in members)
                if (!ValidateSpawn(member.Spawn, out error)) return false;
            data = new ConstellationSpawnData(definition, members);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Constellation placement failed: {ex.Message}";
            return false;
        }
    }

    private bool ValidateSpawn(SpawnData spawn, out string error)
    {
        return PlacementSafety.TryValidatePosition(spawn.Position, earthRadiusMeters / metersPerUnit,
                   PlacementSafety.MaxDistanceUnits, out error) &&
               PlacementSafety.TryValidateVelocity(spawn.Velocity, out error);
    }

    private static bool TryGetRequiredInt(TMPro.TMP_InputField field, string label, out int value, out string error)
    {
        return TryGetRequiredInt(PlacementValidators.GetText(field), label, out value, out error);
    }

    private static bool TryGetRequiredInt(string text, string label, out int value, out string error)
    {
        if (!int.TryParse(text, out value))
        {
            error = $"Invalid constellation {label}. Enter a whole number.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetOptionalInt(
        TMPro.TMP_InputField field,
        int fallback,
        string label,
        out int value,
        out string error)
    {
        string text = PlacementValidators.GetText(field);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = fallback;
            error = null;
            return true;
        }

        if (!int.TryParse(text, out value))
        {
            error = $"Invalid constellation {label}. Enter a whole number.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetOptionalDouble(
        TMPro.TMP_InputField field,
        double fallback,
        string label,
        out double value,
        out string error)
    {
        string text = PlacementValidators.GetText(field);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = fallback;
            error = null;
            return true;
        }

        if (!PlacementValidators.TryGetDouble(text, out value))
        {
            error = $"Invalid constellation {label}. Enter a number.";
            return false;
        }

        error = null;
        return true;
    }
}
