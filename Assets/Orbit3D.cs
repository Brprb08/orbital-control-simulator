using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Orbit3D : MonoBehaviour
{
    public Transform centerObject; // The object to orbit around
    public float orbitRadius = 5f; // Orbit distance from the center object
    public float orbitSpeed = 30f; // Orbit speed in degrees per second (reduce if necessary)
    public float inclination = 30f; // Orbit inclination in degrees
    public int pathSegments = 50; // Number of points in the orbit path
    public float pathLengthDegrees = 90f; // Length of the orbit path in degrees

    private float currentAngle = 0f; // Current angle in degrees
    private LineRenderer lineRenderer; // LineRenderer for the orbit path

    void Start()
    {
        // Initialize the LineRenderer
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = pathSegments;
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.useWorldSpace = true;

        // Draw the initial orbit path
        DrawDynamicOrbitPath();
    }

    void Update()
    {
        if (centerObject == null)
        {
            Debug.LogError("Center object not assigned!");
            return;
        }

        // Update the current angle based on orbit speed
        currentAngle += orbitSpeed * Time.deltaTime;

        // Keep the angle within 0-360 degrees
        if (currentAngle >= 360f) currentAngle -= 360f;

        Debug.Log($"Current Angle: {currentAngle}");

        // Calculate the current position of the sphere in its orbit
        transform.position = CalculateOrbitPosition(currentAngle);

        // Dynamically update the orbit path
        DrawDynamicOrbitPath();
        Debug.Log("Orbit path updated.");
    }

    void DrawDynamicOrbitPath()
    {
        if (lineRenderer == null || centerObject == null) return;

        // Draw a path starting at the current angle and extending pathLengthDegrees ahead
        for (int i = 0; i < pathSegments; i++)
        {
            float segmentAngle = currentAngle + (i * (pathLengthDegrees / pathSegments));
            Vector3 segmentPosition = CalculateOrbitPosition(segmentAngle);
            lineRenderer.SetPosition(i, segmentPosition);
        }
    }

    Vector3 CalculateOrbitPosition(float angle)
    {
        // Convert the angle to radians
        float angleRad = angle * Mathf.Deg2Rad;

        // Calculate the X and Z positions for circular motion
        float x = Mathf.Cos(angleRad) * orbitRadius;
        float z = Mathf.Sin(angleRad) * orbitRadius;

        // Apply inclination to the Y position
        float y = Mathf.Sin(angleRad) * Mathf.Sin(inclination * Mathf.Deg2Rad) * orbitRadius;

        // Return the calculated position relative to the center object
        return new Vector3(
            centerObject.position.x + x,
            centerObject.position.y + y,
            centerObject.position.z + z
        );
    }
}