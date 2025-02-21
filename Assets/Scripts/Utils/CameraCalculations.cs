using UnityEngine;
// using System.Collections.Generic;
// using UnityEngine.EventSystems;
// using System.Collections;
// using TMPro;

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
}