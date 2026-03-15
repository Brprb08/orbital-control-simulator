using TMPro;

public class PlacementUIController
{
    private readonly UIReferences refs;
    private readonly ObjectPlacementManager objectPlacementManager;
    private readonly PlacementFieldsUI placementFieldsUI;

    private enum PlacementMode
    {
        Manual,
        TLE,
        Kepler
    }

    private PlacementMode placementMode = PlacementMode.Manual;

    public PlacementUIController(UIReferences refs, ObjectPlacementManager objectPlacementManager)
    {
        this.refs = refs;
        this.objectPlacementManager = objectPlacementManager;
    }

    public void Initialize()
    {
        RefreshButtonLabel();
    }

    public void CyclePlacementMode()
    {
        placementMode = (PlacementMode)(((int)placementMode + 1) % 3);
        RefreshButtonLabel();
    }

    public void Apply(CameraMode cameraMode)
    {
        bool isFreeCam = cameraMode == CameraMode.Free;

        if (refs.placementModeButton != null)
            refs.placementModeButton.interactable = isFreeCam;

        if (refs.randomSatelliteButton != null)
            refs.randomSatelliteButton.interactable = isFreeCam;

        ShowPlacementSelect(isFreeCam);
        ShowPlacePanels(isFreeCam);

        if (!isFreeCam)
            objectPlacementManager?.ClearAllFields();
    }

    private void RefreshButtonLabel()
    {
        TMP_Text txt = refs.placementModeButton != null
            ? refs.placementModeButton.GetComponentInChildren<TMP_Text>()
            : null;

        if (txt == null) return;

        txt.text = placementMode switch
        {
            PlacementMode.Manual => "Mode: Cartesian  (next: TLE)",
            PlacementMode.TLE => "Mode: TLE       (next: Kepler)",
            PlacementMode.Kepler => "Mode: Kepler      (next: Cartesian)",
            _ => txt.text
        };
    }

    private void ShowPlacementSelect(bool show)
    {
        if (refs.placementSelectPanel != null)
            refs.placementSelectPanel.SetActive(show);

        if (refs.randomPlacementPanel != null)
            refs.randomPlacementPanel.SetActive(show);

        bool manual = placementMode == PlacementMode.Manual;

        if (refs.velocityInputField != null)
            refs.velocityInputField.interactable = false;

        if (show)
        {
            if (refs.nameInputField != null) refs.nameInputField.interactable = manual;
            if (refs.positionInputField != null) refs.positionInputField.interactable = manual;
            if (refs.massInputField != null) refs.massInputField.interactable = manual;
            if (refs.radiusInputField != null) refs.radiusInputField.interactable = manual;
            if (refs.placeObjectButton != null) refs.placeObjectButton.interactable = true;
        }
        else
        {
            if (refs.nameInputField != null)
            {
                refs.nameInputField.text = string.Empty;
                refs.nameInputField.interactable = false;
            }

            if (refs.positionInputField != null)
            {
                refs.positionInputField.text = string.Empty;
                refs.positionInputField.interactable = false;
            }

            if (refs.massInputField != null)
            {
                refs.massInputField.text = string.Empty;
                refs.massInputField.interactable = false;
            }

            if (refs.radiusInputField != null)
            {
                refs.radiusInputField.text = string.Empty;
                refs.radiusInputField.interactable = false;
            }

            if (refs.placeObjectButton != null)
                refs.placeObjectButton.interactable = false;
        }
    }

    private void ShowPlacePanels(bool show)
    {
        if (refs.placeTLEPanel == null || refs.objectPlacementPanel == null || refs.placeKeplerPanel == null)
            return;

        if (!show)
        {
            refs.placeTLEPanel.SetActive(false);
            refs.objectPlacementPanel.SetActive(false);
            refs.placeKeplerPanel.SetActive(false);
            return;
        }

        refs.placeTLEPanel.SetActive(placementMode == PlacementMode.TLE);
        refs.objectPlacementPanel.SetActive(placementMode == PlacementMode.Manual);
        refs.placeKeplerPanel.SetActive(placementMode == PlacementMode.Kepler);
    }
}