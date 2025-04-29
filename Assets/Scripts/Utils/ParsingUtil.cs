using UnityEngine;

/**
 * A singleton helper for turning string inputs into Unity types.
 * Mainly used to convert comma-separated numbers into Vector3s or validate mass values.
 * Stick this on a GameObject in your scene, and call its static Instance from anywhere.
 */
public class ParsingUtils : MonoBehaviour
{
    public static ParsingUtils Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /**
     * Attempts to parse a string like "1.0, 2.0, 3.0" into a Vector3.
     * Returns true and sets result if the input has exactly three float values
     * otherwise returns false and leaves result at Vector3.zero.
     *
     * @param input   The comma-separated string to parse.
     * @param result  Output parameter that receives the parsed Vector3.
     * @return        True if parse succeeded, false if format was wrong or parse failed.
     */
    public bool TryParseVector3(string input, out Vector3 result)
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


    /**
     * Validates and parses a mass value from a string.
     * Rejects empty input, non-numeric strings, or values outside the range:
     * at least 500 (kg) up to about 5.972×10^11 (approximate mass of the universe in kg).
     *
     * @param input  The string representing mass.
     * @param mass   Output parameter that receives the parsed mass if valid.
     * @return       True if input was a number within the valid mass range, false otherwise.
     */
    public bool TryParseMass(string input, out float mass)
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