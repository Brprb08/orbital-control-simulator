using UnityEngine;

public class CameraButtonProxy : MonoBehaviour
{
    private GravityManager gravityManager;
    private ICameraTracker cameraTracker;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.gravityManager = ctx.GravityManager;
        this.cameraTracker = ctx.CameraTracker;
    }

    // These are callable from Unity UI Button OnClick
    public void EarthCam()
    {
        cameraTracker.SwitchToEarthCam();
    }

    public void FreeCam()
    {
        cameraTracker.BreakToFreeCam();
    }

    public void ReturnToTracking()
    {
        cameraTracker.ReturnToTracking();
    }
}
