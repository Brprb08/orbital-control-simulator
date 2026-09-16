using UnityEngine;
using System.Globalization;

/// <summary>
/// A helper for parsing string inputs into Unity types.
/// Mainly used to convert comma-separated numbers into Vector3s or validate mass values.
/// </summary>
public static class ParsingUtils
{
    /// <summary>
    /// Attempts to parse a string like "1.0, 2.0, 3.0" into a Vector3/>.
    /// </summary>
    /// <param name="input">The comma-separated string to parse.</param>
    /// <param name="result">Output parameter receiving the parsed Vector3.</param>
    /// <returns>
    /// True if parsing succeeded and three float values were found; otherwise false.
    /// </returns>
    public static bool TryParseVector3(string input, out Vector3 result)
    {
        result = Vector3.zero;
        if (string.IsNullOrWhiteSpace(input)) return false;
        string[] parts = input.Split(',');

        if (parts.Length != 3)
            return false;

        float x, y, z;
        if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.IsFinite(x) &&
            float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y) && float.IsFinite(y) &&
            float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z) && float.IsFinite(z))
        {
            result = new Vector3(x, y, z);
            return true;
        }

        return false;
    }


    /// <summary>
    /// Validates and parses a mass value from a string.
    /// Only allows numeric values between 500 and 1,000,000 kg.
    /// </summary>
    /// <param name="input">The string representing mass.</param>
    /// <param name="mass">Output parameter receiving the parsed mass if valid.</param>
    /// <returns>
    /// True if input is a number within the valid range; false otherwise.
    /// </returns>
    public static bool TryParseMass(string input, out float mass)
    {
        mass = 0f;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedMass) || !float.IsFinite(parsedMass))
            return false;

        if (parsedMass < SimulationLimits.MinSatelliteMassKg || parsedMass > SimulationLimits.MaxSatelliteMassKg)
            return false;

        mass = parsedMass;
        return true;
    }
}