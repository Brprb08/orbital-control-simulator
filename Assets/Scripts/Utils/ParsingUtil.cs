// using UnityEngine;

// /// <summary>
// /// A singleton helper for turning string inputs into Unity types.
// /// Mainly used to convert comma-separated numbers into Vector3s or validate mass values.
// /// Attach this to a GameObject in your scene and access it via <c>ParsingUtils.Instance</c>.
// /// </summary>
// public class ParsingUtils : MonoBehaviour
// {
//     public static ParsingUtils Instance { get; private set; }

//     void Awake()
//     {
//         if (Instance != null && Instance != this)
//         {
//             Destroy(gameObject);
//             return;
//         }
//         Instance = this;
//     }

//     /// <summary>
//     /// Attempts to parse a string like "1.0, 2.0, 3.0" into a <see cref="Vector3"/>.
//     /// </summary>
//     /// <param name="input">The comma-separated string to parse.</param>
//     /// <param name="result">Output parameter receiving the parsed Vector3.</param>
//     /// <returns>
//     /// True if parsing succeeded and three float values were found; otherwise false.
//     /// </returns>
//     public bool TryParseVector3(string input, out Vector3 result)
//     {
//         result = Vector3.zero;
//         string[] parts = input.Split(',');

//         if (parts.Length != 3)
//             return false;

//         float x, y, z;
//         if (float.TryParse(parts[0].Trim(), out x) &&
//             float.TryParse(parts[1].Trim(), out y) &&
//             float.TryParse(parts[2].Trim(), out z))
//         {
//             result = new Vector3(x, y, z);
//             return true;
//         }

//         return false;
//     }


//     /// <summary>
//     /// Validates and parses a mass value from a string.
//     /// Only allows numeric values between 500 and 5.972 × 10¹¹ kg.
//     /// </summary>
//     /// <param name="input">The string representing mass.</param>
//     /// <param name="mass">Output parameter receiving the parsed mass if valid.</param>
//     /// <returns>
//     /// True if input is a number within the valid range; false otherwise.
//     /// </returns>
//     public bool TryParseMass(string input, out float mass)
//     {
//         mass = 0f;

//         if (string.IsNullOrWhiteSpace(input))
//             return false;

//         if (!float.TryParse(input, out float parsedMass))
//             return false;

//         if (parsedMass < 500 || parsedMass > 5.972e+11)
//             return false;

//         mass = parsedMass;
//         return true;
//     }
// }

using UnityEngine;

/// <summary>
/// A singleton helper for turning string inputs into Unity types.
/// Mainly used to convert comma-separated numbers into Vector3s or validate mass values.
/// Attach this to a GameObject in your scene and access it via <c>ParsingUtils.Instance</c>.
/// </summary>
public static class ParsingUtils
{
    // public static ParsingUtils Instance { get; private set; }

    // void Awake()
    // {
    //     if (Instance != null && Instance != this)
    //     {
    //         Destroy(gameObject);
    //         return;
    //     }
    //     Instance = this;
    // }

    /// <summary>
    /// Attempts to parse a string like "1.0, 2.0, 3.0" into a <see cref="Vector3"/>.
    /// </summary>
    /// <param name="input">The comma-separated string to parse.</param>
    /// <param name="result">Output parameter receiving the parsed Vector3.</param>
    /// <returns>
    /// True if parsing succeeded and three float values were found; otherwise false.
    /// </returns>
    public static bool TryParseVector3(string input, out Vector3 result)
    {
        result = Vector3.zero;
        string[] parts = input.Split(',');

        if (parts.Length != 3)
            return false;

        float x, y, z;
        if (float.TryParse(parts[0].Trim(), out x) &&
            float.TryParse(parts[1].Trim(), out y) &&
            float.TryParse(parts[2].Trim(), out z))
        {
            result = new Vector3(x, y, z);
            return true;
        }

        return false;
    }


    /// <summary>
    /// Validates and parses a mass value from a string.
    /// Only allows numeric values between 500 and 5.972 × 10¹¹ kg.
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

        if (!float.TryParse(input, out float parsedMass))
            return false;

        if (parsedMass < 500 || parsedMass > 5.972e+11)
            return false;

        mass = parsedMass;
        return true;
    }
}