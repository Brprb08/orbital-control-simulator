using TMPro;

public class FlightUIController
{
    public enum ThrustUiMode
    {
        FreeThrust,
        ManeuverNodes
    }

    private readonly UIReferences refs;

    private ThrustUiMode thrustUiMode = ThrustUiMode.FreeThrust;

    public ThrustUiMode CurrentMode => thrustUiMode;
    public bool IsFreeThrustMode => thrustUiMode == ThrustUiMode.FreeThrust;

    public FlightUIController(UIReferences refs)
    {
        this.refs = refs;
    }

    public void Initialize()
    {
        RefreshButtonLabel();
    }

    public void ToggleBurnMode()
    {
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
}