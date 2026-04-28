using TMPro;
using UnityEngine;

/// <summary>
/// Updates tracked-body readouts from camera tracking state.
/// </summary>
public class CameraInfoUIController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI velocityText;
    [SerializeField] private TextMeshProUGUI altitudeText;
    [SerializeField] private TextMeshProUGUI trackingObjectNameText;

    private ICameraTracker cameraTracker;

    public void Initialize(SimContext ctx)
    {
        cameraTracker = ctx?.CameraTracker;
        Refresh();
    }

    public void SetTextReferences(
        TextMeshProUGUI velocity,
        TextMeshProUGUI altitude,
        TextMeshProUGUI trackingObjectName)
    {
        velocityText = velocity;
        altitudeText = altitude;
        trackingObjectNameText = trackingObjectName;
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void Refresh()
    {
        NBody body = cameraTracker?.CurrentBody;
        if (body == null)
            return;

        if (velocityText != null)
        {
            float velocityMagnitude = body.velocity.magnitude;
            float velocityInMetersPerSecond = velocityMagnitude * 10000f;
            velocityText.text = $"Velocity: {velocityInMetersPerSecond:F2} m/s";
        }

        if (altitudeText != null)
        {
            float altitude = (float)body.altitude;
            altitudeText.text = $"Altitude: {altitude * 10:F3} km";
        }

        if (trackingObjectNameText != null)
        {
            trackingObjectNameText.text = body.name;
        }
    }
}
