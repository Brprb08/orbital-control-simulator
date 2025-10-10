using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Utility methods for validating and parsing user placement inputs (names, masses, vectors, radii, and positions).
/// Designed for use with UI fields (TMP_InputField) and raw strings without altering application logic.
/// </summary>
public static class PlacementValidators
{
    /// <summary>
    /// Inclusive floating-point range helper.
    /// </summary>
    public readonly struct RangeF
    {
        /// <summary>The minimum inclusive value.</summary>
        public readonly float Min;
        /// <summary>The maximum inclusive value.</summary>
        public readonly float Max;

        /// <summary>
        /// Creates an inclusive range.
        /// </summary>
        public RangeF(float min, float max) { Min = min; Max = max; }

        /// <summary>
        /// Returns <c>true</c> if v lies within the inclusive range; otherwise <c>false</c>.
        /// </summary>
        public bool Contains(float v) => v >= Min && v <= Max;
    }

    /// <summary>
    /// Inclusive bounds for a distance measurement.
    /// </summary>
    public readonly struct DistanceBoundsF
    {
        /// <summary>The minimum inclusive distance.</summary>
        public readonly float Min;
        /// <summary>The maximum inclusive distance.</summary>
        public readonly float Max;

        /// <summary>
        /// Creates inclusive distance bounds.
        /// </summary>
        public DistanceBoundsF(float min, float max) { Min = min; Max = max; }

        /// <summary>
        /// Returns <c>true</c> if d lies within the inclusive bounds; otherwise <c>false</c>.
        /// </summary>
        public bool Contains(float d) => d >= Min && d <= Max;
    }

    /// <summary>
    /// Returns the current text from a TMP input field, or <c>null</c> if the field is missing.
    /// </summary>
    public static string GetText(TMP_InputField field) => field ? field.text : null;

    /// <summary>
    /// Validates and normalizes an object name. Falls back to fallbackPrefix when input is blank.
    /// </summary>
    /// <param name="text">User-entered name.</param>
    /// <param name="fallbackPrefix">Prefix used when generating a fallback name.</param>
    /// <param name="index">Index appended to the fallback prefix.</param>
    /// <param name="maxLen">Maximum allowed length (inclusive).</param>
    /// <param name="name">Validated or fallback name.</param>
    /// <param name="error">Error message when validation fails; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if the name is valid; otherwise <c>false</c>.</returns>
    public static bool TryGetName(string text, string fallbackPrefix, int index, int maxLen,
                                  out string name, out string error)
    {
        name = text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{fallbackPrefix} {index}";
        if (name.Length > maxLen)
        {
            error = $"Name too long. Max {maxLen} characters.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Field-based overload of TryGetName(string,string,int,int,out string,out string).
    /// </summary>
    public static bool TryGetName(TMP_InputField field, string fallbackPrefix, int index, int maxLen,
                                  out string name, out string error)
        => TryGetName(GetText(field), fallbackPrefix, index, maxLen, out name, out error);

    /// <summary>
    /// Parses and validates mass against an allowed range.
    /// </summary>
    /// <param name="text">User-entered mass (supports units/formatting handled by <c>ParsingUtils.TryParseMass</c>).</param>
    /// <param name="allowed">Inclusive allowed mass range.</param>
    /// <param name="mass">Parsed mass in kilograms.</param>
    /// <param name="error">Error message when validation fails; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if parsed and within range; otherwise <c>false</c>.</returns>
    public static bool TryGetMass(string text, RangeF allowed, out float mass, out string error)
    {
        mass = 0f;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"Please enter a numeric mass between {allowed.Min:N0} and {allowed.Max:N0} kg.";
            return false;
        }
        if (!ParsingUtils.TryParseMass(text, out mass) || !allowed.Contains(mass))
        {
            error = $"Invalid mass. Enter a number between {allowed.Min:N0} and {allowed.Max:N0}.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Field-based overload of TryGetMass(string,RangeF,out float,out string).
    /// </summary>
    public static bool TryGetMass(TMP_InputField field, RangeF allowed, out float mass, out string error)
        => TryGetMass(GetText(field), allowed, out mass, out error);

    /// <summary>
    /// Parses a Vector3 from text in "x,y,z" numeric format.
    /// </summary>
    /// <param name="text">User-entered vector string.</param>
    /// <param name="v">Parsed vector.</param>
    /// <param name="error">Error message when parsing fails; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if parsing succeeds; otherwise <c>false</c>.</returns>
    public static bool TryGetVector3(string text, out Vector3 v, out string error)
    {
        if (!ParsingUtils.TryParseVector3(text, out v))
        {
            error = "Invalid format. Use numeric x,y,z.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// Field-based overload of TryGetVector3(string,out Vector3,out string).
    /// </summary>
    public static bool TryGetVector3(TMP_InputField field, out Vector3 v, out string error)
        => TryGetVector3(GetText(field), out v, out error);

    /// <summary>
    /// Parses a radius vector and clamps each axis to the provided range.
    /// </summary>
    /// <param name="text">User-entered radius in "x,y,z" format.</param>
    /// <param name="perAxisClamp">Inclusive clamp applied independently per component.</param>
    /// <param name="radius">Parsed and clamped radius vector.</param>
    /// <param name="error">Error message when parsing fails; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if parsing succeeds; otherwise <c>false</c>.</returns>
    public static bool TryGetRadius(string text, RangeF perAxisClamp, out Vector3 radius, out string error)
    {
        if (!TryGetVector3(text, out radius, out error)) return false;

        radius = new Vector3(
            Mathf.Clamp(radius.x, perAxisClamp.Min, perAxisClamp.Max),
            Mathf.Clamp(radius.y, perAxisClamp.Min, perAxisClamp.Max),
            Mathf.Clamp(radius.z, perAxisClamp.Min, perAxisClamp.Max)
        );
        return true;
    }

    /// <summary>
    /// Field-based overload of TryGetRadius(string,RangeF,out Vector3,out string).
    /// </summary>
    public static bool TryGetRadius(TMP_InputField field, RangeF perAxisClamp, out Vector3 radius, out string error)
        => TryGetRadius(GetText(field), perAxisClamp, out radius, out error);

    /// <summary>
    /// Parses a position, or when empty places an object in front of the provided camera transform.
    /// Validates the final position by its distance from world origin against provided bounds.
    /// </summary>
    /// <param name="text">User-entered position in "x,y,z" format, or blank to use a default.</param>
    /// <param name="camera">Transform providing position and forward direction for the default placement.</param>
    /// <param name="defaultForwardDistance">Distance in front of camera used when input is blank.</param>
    /// <param name="bounds">Inclusive allowed distance from world origin.</param>
    /// <param name="pos">Resolved position.</param>
    /// <param name="error">Error message when parsing/validation fails; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a valid position is produced; otherwise <c>false</c>.</returns>
    public static bool TryGetPositionOrDefault(
        string text,
        Transform camera,
        float defaultForwardDistance,
        DistanceBoundsF bounds,
        out Vector3 pos,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (camera == null)
            {
                pos = default;
                error = "Camera not set for default placement.";
                return false;
            }
            pos = camera.position + camera.forward * defaultForwardDistance;
            error = null;
            return true;
        }

        if (!ParsingUtils.TryParseVector3(text, out pos))
        {
            error = "Invalid position input. Please use numeric x,y,z format.";
            return false;
        }

        float d = Vector3.Distance(Vector3.zero, pos);
        if (!bounds.Contains(d))
        {
            error = $"Invalid position: must be between {bounds.Min:N0} and {bounds.Max:N0} units from the center.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Field-based overload of TryGetPositionOrDefault(string,Transform,float,DistanceBoundsF,out Vector3,out string).
    /// </summary>
    public static bool TryGetPositionOrDefault(
        TMP_InputField field,
        Transform camera,
        float defaultForwardDistance,
        DistanceBoundsF bounds,
        out Vector3 pos,
        out string error)
        => TryGetPositionOrDefault(GetText(field), camera, defaultForwardDistance, bounds, out pos, out error);

    /// <summary>
    /// Parses a double using invariant culture (useful for orbital elements and data files).
    /// </summary>
    /// <param name="text">User-entered numeric string.</param>
    /// <param name="value">Parsed value when successful; otherwise <c>0</c>.</param>
    /// <returns><c>true</c> if parsing succeeds; otherwise <c>false</c>.</returns>
    public static bool TryGetDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Field-based overload of <see TryGetDouble(string,out double).
    /// </summary>
    public static bool TryGetDouble(TMP_InputField field, out double value)
        => TryGetDouble(GetText(field), out value);
}
