using TMPro;

public class FlightUIController
{
    public enum ThrustUiMode
    {
        FreeThrust,
        ManeuverNodes
    }

    private readonly UIReferences refs;
    private readonly ManeuverNodeManager maneuverNodeManager;
    private readonly ThrustController thrustController;

    private ThrustUiMode thrustUiMode = ThrustUiMode.FreeThrust;

    public ThrustUiMode CurrentMode => thrustUiMode;
    public bool IsFreeThrustMode => thrustUiMode == ThrustUiMode.FreeThrust;

    public FlightUIController(UIReferences refs, SimContext ctx)
    {
        this.refs = refs;
        maneuverNodeManager = ctx != null ? ctx.ManeuverNodeManager : null;
        thrustController = ctx != null ? ctx.ThrustController : null;
    }

    public void Initialize()
    {
        RefreshButtonLabel();
    }

    public void ToggleBurnMode()
    {
        if (ShouldForceManeuverMode())
        {
            thrustUiMode = ThrustUiMode.ManeuverNodes;
            RefreshButtonLabel();
            return;
        }

        thrustUiMode = thrustUiMode == ThrustUiMode.FreeThrust
            ? ThrustUiMode.ManeuverNodes
            : ThrustUiMode.FreeThrust;

        RefreshButtonLabel();
    }

    public void Apply(CameraMode cameraMode)
    {
        bool isFreeCam = cameraMode == CameraMode.Free;
        ShowThrustPanels(!isFreeCam);
    }

    public void ShowThrustPanels(bool show)
    {
        bool forceManeuverMode = ShouldForceManeuverMode();
        if (forceManeuverMode)
            thrustUiMode = ThrustUiMode.ManeuverNodes;

        if (refs.burnControlButton != null)
            refs.burnControlButton.interactable = !forceManeuverMode;

        if (refs.burnControlsPanel != null)
            refs.burnControlsPanel.SetActive(show);

        if (!show)
        {
            if (refs.thrustButtons != null) refs.thrustButtons.SetActive(false);
            if (refs.maneuverNodePanel != null) refs.maneuverNodePanel.SetActive(false);
            if (refs.attitudeControlPanel != null) refs.attitudeControlPanel.SetActive(false);
            return;
        }

        if (thrustUiMode == ThrustUiMode.FreeThrust)
        {
            if (refs.thrustButtons != null) refs.thrustButtons.SetActive(true);
            if (refs.attitudeControlPanel != null) refs.attitudeControlPanel.SetActive(true);
            if (refs.maneuverNodePanel != null) refs.maneuverNodePanel.SetActive(false);
        }
        else
        {
            if (refs.thrustButtons != null) refs.thrustButtons.SetActive(false);
            if (refs.attitudeControlPanel != null) refs.attitudeControlPanel.SetActive(false);
            if (refs.maneuverNodePanel != null) refs.maneuverNodePanel.SetActive(true);
        }
    }

    public void RefreshButtonLabel()
    {
        TMP_Text txt = refs.burnControlButton != null
            ? refs.burnControlButton.GetComponentInChildren<TMP_Text>()
            : null;

        if (txt == null) return;

        txt.text = thrustUiMode == ThrustUiMode.FreeThrust
            ? "Use Maneuver Nodes"
            : "Use Free Thrust";
    }

    private bool ShouldForceManeuverMode()
    {
        if (thrustController != null && thrustController.IsNodeBurnActive)
            return true;

        ManeuverNode node = maneuverNodeManager != null ? maneuverNodeManager.CurrentNode : null;
        return node != null && node.isFinalized;
    }
}
