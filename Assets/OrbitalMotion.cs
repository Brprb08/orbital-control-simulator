using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class OrbitalMotion : MonoBehaviour
{
    public Transform centerObject; // The object to orbit around
    public float orbitRadius = 20f; // Radius of the orbit
    public float orbitSpeed = 20f; // Speed of orbit in degrees per second
    public int pathSegments = 50; // Number of segments for the orbit path
    public float pathLength = 90f; // Length of the path in degrees (partial path)

    private float angle = 0f; // Current angle of the orbit in degrees
    private LineRenderer lineRenderer;

    void Start()
    {
        // Initialize the LineRenderer
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = pathSegments + 1; // Path includes start and end points
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.useWorldSpace = true;

        // Ensure centerObject is assigned
        if (centerObject == null)
        {
            Debug.LogError("CenterObject is not assigned!");
        }
    }

    void Update()
    {
        // Update the orbit angle
        angle += orbitSpeed * Time.deltaTime;

        // Keep the angle within 0-360 degrees
        if (angle >= 360f) angle -= 360f;

        // Convert angle to radians for position calculation
        float angleRad = angle * Mathf.Deg2Rad;

        // Calculate the new position
        float x = Mathf.Cos(angleRad) * orbitRadius;
        float z = Mathf.Sin(angleRad) * orbitRadius;

        // Update the object's position relative to the center object
        transform.position = new Vector3(
            centerObject.position.x + x,
            centerObject.position.y,
            centerObject.position.z + z
        );

        // Dynamically update the orbit path
        DrawOrbitPath();
    }

    void DrawOrbitPath()
    {
        if (centerObject == null) return;

        // Draw a partial orbit path
        for (int i = 0; i <= pathSegments; i++)
        {
            // Calculate the angle for this segment of the path
            float segmentAngle = angle + (i * pathLength / pathSegments);
            float segmentAngleRad = segmentAngle * Mathf.Deg2Rad;

            // Calculate the position for this segment
            float x = Mathf.Cos(segmentAngleRad) * orbitRadius;
            float z = Mathf.Sin(segmentAngleRad) * orbitRadius;

            Vector3 pointPosition = new Vector3(
                centerObject.position.x + x,
                centerObject.position.y,
                centerObject.position.z + z
            );

            // Assign the position to the LineRenderer
            lineRenderer.SetPosition(i, pointPosition);
        }
    }
}