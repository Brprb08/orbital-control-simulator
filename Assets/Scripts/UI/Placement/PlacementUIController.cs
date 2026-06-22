using TMPro;
using UnityEngine;

/// <summary>
/// Controls placement mode and panel visibility. Field contents, feedback text,
/// and input locking live in PlacementFieldsUI; object spawning lives in
/// ObjectPlacementManager.
/// </summary>
public class PlacementUIController
{
    private readonly UIReferences refs;
    private readonly ObjectPlacementManager objectPlacementManager;

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

    public void Apply(CameraMode cameraMode, bool showManualVelocityUi)
    {
        bool isFreeCam = cameraMode == CameraMode.Free;
        bool showPlacementUi = isFreeCam;
        bool showPlacementControls = showPlacementUi && !showManualVelocityUi;

        if (refs.placementModeButton != null)
            refs.placementModeButton.interactable = showPlacementControls;

        if (refs.randomSatelliteButton != null)
            refs.randomSatelliteButton.interactable = showPlacementControls;

        ShowPlacementSelect(showPlacementControls);
        ShowPlacePanels(showPlacementUi, showManualVelocityUi);
        SetManualObjectControlsVisible(showPlacementControls && placementMode == PlacementMode.Manual);

        if (!showPlacementUi)
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
        UIHelpers.SetActive(refs.placementSelectPanel, show);
        UIHelpers.SetActive(refs.randomPlacementPanel, show);

        bool manual = placementMode == PlacementMode.Manual;

        if (show)
        {
            UIHelpers.SetInteractable(refs.nameInputField, manual);
            UIHelpers.SetInteractable(refs.positionInputField, manual);
            UIHelpers.SetInteractable(refs.massInputField, manual);
            UIHelpers.SetInteractable(refs.radiusInputField, manual);
            UIHelpers.SetInteractable(refs.placeObjectButton, manual);
        }
        else
        {
            ClearAndDisableManualInputs();
        }
    }

    private void ShowPlacePanels(bool show, bool showManualVelocityUi)
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
        refs.objectPlacementPanel.SetActive(showManualVelocityUi || placementMode == PlacementMode.Manual);
        refs.placeKeplerPanel.SetActive(placementMode == PlacementMode.Kepler);
    }

    private void SetManualObjectControlsVisible(bool visible)
    {
        SetInputVisible(refs.nameInputField, visible);
        SetInputVisible(refs.positionInputField, visible);
        SetInputVisible(refs.massInputField, visible);
        SetInputVisible(refs.radiusInputField, visible);

        UIHelpers.SetActive(refs.placeObjectButton != null ? refs.placeObjectButton.gameObject : null, visible);

        Transform panelRoot = refs.objectPlacementPanel != null ? refs.objectPlacementPanel.transform : null;
        UIHelpers.SetChildActive(panelRoot, "Txt_Name", visible);
        UIHelpers.SetChildActive(panelRoot, "Txt_Position", visible);
        UIHelpers.SetChildActive(panelRoot, "Txt_Mass", visible);
        UIHelpers.SetChildActive(panelRoot, "Txt_Radius", visible);
    }

    private static void SetInputVisible(TMP_InputField input, bool visible)
    {
        UIHelpers.SetActive(input != null ? input.gameObject : null, visible);
    }

    private void ClearAndDisableManualInputs()
    {
        UIHelpers.ClearInputs(
            false,
            refs.nameInputField,
            refs.positionInputField,
            refs.massInputField,
            refs.radiusInputField
        );

        UIHelpers.SetInteractable(
            false,
            refs.nameInputField,
            refs.positionInputField,
            refs.massInputField,
            refs.radiusInputField,
            refs.placeObjectButton
        );
    }
}
