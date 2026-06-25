using System;
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
        public readonly float Mass;

        public ManualPlaceholderData(string name, Vector3 position, Vector3 radiusMeters, float mass)
        {
            Name = name;
            Position = position;
            RadiusMeters = radiusMeters;
            Mass = mass;
        }
    }

    public readonly struct SpawnData
    {
        public readonly string Name;
        public readonly double Mass;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;

        public SpawnData(string name, double mass, Vector3 position, Vector3 velocity)
        {
            Name = name;
            Mass = mass;
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

    public static readonly PlacementValidators.RangeF MassRange = new(500f, 1000000f);
    public static readonly PlacementValidators.RangeF RadiusClamp = new(
        SatelliteSizing.MinPhysicalRadiusMeters,
        SatelliteSizing.MaxPhysicalRadiusMeters
    );
    public static readonly PlacementValidators.DistanceBoundsF PositionBounds = new(638f, 5000f);

    private const int MaxSatelliteNameLength = 15;

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
                mainCamera.transform,
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

        if (radius == Vector3.zero)
            radius = Vector3.one;

        data = new ManualPlaceholderData(name, position, radius, mass);
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

            double rp = aMeters * (1.0 - e);
            if (rp <= earthRadiusMeters * 1.001)
            {
                double altKm = (rp - earthRadiusMeters) / 1000.0;
                error = $"Orbit intersects Earth (perigee alt {altKm:F1} km). Increase 'a' or reduce 'e'.";
                return false;
            }

            data = new SpawnData(
                name,
                mass,
                FrameUtils.EciToUnity(rEci, metersPerUnit),
                FrameUtils.VelEciToUnity(vEci, metersPerUnit)
            );
            return true;
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

        string name = !string.IsNullOrWhiteSpace(fields.TleNameInputField?.text)
            ? fields.TleNameInputField.text.Trim()
            : $"TLE Satellite {satelliteSpawner.NextSatelliteIndex}";

        if (!TLEParser.TryPropagate(
                fields.TleLine1InputField.text,
                fields.TleLine2InputField.text,
                whenUtc,
                out Vector3d rEciMeters,
                out Vector3d vEciMetersPerSecond,
                out DateTime epochUtc))
        {
            error = "Invalid TLE input or propagation failed.";
            return false;
        }

        if (rEciMeters.magnitude <= earthRadiusMeters * 1.001)
        {
            error = "Computed position intersects Earth. Check TLE/time.";
            return false;
        }

        SpawnData spawn = new(
            name,
            mass,
            FrameUtils.EciToUnity(rEciMeters, metersPerUnit),
            FrameUtils.VelEciToUnity(vEciMetersPerSecond, metersPerUnit)
        );
        data = new TleSpawnData(spawn, whenUtc, epochUtc);
        return true;
    }
}
