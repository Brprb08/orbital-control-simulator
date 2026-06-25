using TMPro;

public class InstructionsUIController
{
    private readonly UIReferences refs;

    private bool showInstructionText;

    public bool IsVisible => showInstructionText;

    public InstructionsUIController(UIReferences refs, bool initialVisible = false)
    {
        this.refs = refs;
        showInstructionText = initialVisible;
    }

    public void Initialize()
    {
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void Toggle()
    {
        showInstructionText = false;
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void SetVisible(bool visible)
    {
        showInstructionText = false;
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void Apply(CameraMode mode)
    {
        showInstructionText = false;
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void RefreshButtonLabel()
    {
        TMP_Text buttonText = refs.instructionsButton != null
            ? refs.instructionsButton.GetComponentInChildren<TMP_Text>()
            : null;

        if (buttonText != null)
            buttonText.text = "Open Tutorial";
    }

    private void ApplyVisibility()
    {
        if (refs.instructionsPanel != null)
            refs.instructionsPanel.SetActive(showInstructionText);
    }
}
