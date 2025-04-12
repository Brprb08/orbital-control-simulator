using UnityEngine;

/**
* 
**/
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
        DontDestroyOnLoad(gameObject);
    }

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

    public bool TryParseMass(string input, out float mass)
    {
        mass = 0f;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!float.TryParse(input, out float parsedMass))
            return false;

        if (parsedMass < 500 || parsedMass > 5.972e+50)
            return false;

        mass = parsedMass;
        return true;
    }
}