using UnityEngine;

/**
* 
**/
public class CameraCalculations : MonoBehaviour
{

    public static CameraCalculations Instance { get; private set; }

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

    /**
    * Clamps an angle between a minimum and maximum value.
    * @param angle The angle to clamp.
    * @param min The minimum value.
    * @param max The maximum value.
    * @return The clamped angle.
    **/
    public float ClampAngle(float angle, float min, float max)
    {
        angle = NormalizeAngle(angle);
        return Mathf.Clamp(angle, min, max);
    }

    /**
    * Normalizes an angle to be within -180 to 180 degrees.
    * @param angle The angle to normalize.
    * @return The normalized angle.
    **/
    public float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /**
    * Calculates the minimumcameraDistance based on the radius of the object being tracked.
    * @param radius - Radius of object being tracked by camera
    **/
    public float CalculateMinDistance(float radius)
    {
        if (radius <= 0.5f)
        {
            return Mathf.Max(0.4f, radius * 10f);
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return radius * 4f;
        }
        else
        {
            return radius + 400f;
        }
    }

    /**
    * Calculates the maximumcameraDistance based on the radius of the object being tracked.
    * @param radius - Radius of object being tracked by camera
    **/
    public float CalculateMaxDistance(float radius)
    {
        float minimumMaxDistance = 2000f;

        if (radius <= 0.5f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 500f);  // Small objects
        }
        else if (radius > 0.5f && radius <= 100f)
        {
            return Mathf.Max(minimumMaxDistance, radius * 100f);  // Medium objects
        }
        else
        {
            return Mathf.Max(minimumMaxDistance, radius + 2000f);  // Large onjects
        }
    }
}