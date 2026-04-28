using UnityEngine;

/// <summary>
/// Provides UI button event hooks that control camera modes by proxy.
/// Bridges Unity UI buttons to the camera tracking system, enabling switching
/// between Earth, Free, and Tracking camera modes without direct scene references.
/// </summary>
public class CameraButtonProxy : MonoBehaviour
{
    private ICameraTracker cameraTracker;
    private SimContext ctx;

    /// <summary>
    /// Initializes the proxy with the active simulation context and retrieves camera tracker reference.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraTracker = ctx.CameraTracker;
    }

    /// <summary>
    /// Switches the camera to Earth-centered mode.
    /// </summary>
    public void EarthCam()
    {
        cameraTracker.SwitchToEarthCam();
        ctx?.UIRoot?.RefreshAllUi();
    }

    /// <summary>
    /// Switches the camera to free-fly mode.
    /// </summary>
    public void FreeCam()
    {
        cameraTracker.BreakToFreeCam();
    }

    /// <summary>
    /// Returns the camera to tracking its current target body.
    /// </summary>
    public void ReturnToTracking()
    {
        cameraTracker.ReturnToTracking();
    }
}

