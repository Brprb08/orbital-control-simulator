using UnityEngine;
public class CloudRotation : MonoBehaviour
{
    // 360f / 86400f;  Earth's base speed (deg/sec)
    // 1.05f;          Clouds rotate slightly faster
    float CloudRotationRate = (360f / 86400f) * (1.4f);
    void Update()
    {
        transform.Rotate(Vector3.up, -CloudRotationRate * Time.fixedDeltaTime);
    }

}