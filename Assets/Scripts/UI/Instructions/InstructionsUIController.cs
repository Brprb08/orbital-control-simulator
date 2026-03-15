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
        showInstructionText = !showInstructionText;
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void SetVisible(bool visible)
    {
        showInstructionText = visible;
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void Apply(CameraMode mode)
    {
        // For now, instructions content text is still owned elsewhere.
        // This controller only owns the panel visibility + button label.
        ApplyVisibility();
        RefreshButtonLabel();
    }

    public void RefreshButtonLabel()
    {
        TMP_Text buttonText = refs.instructionsButton != null
            ? refs.instructionsButton.GetComponentInChildren<TMP_Text>()
            : null;

        if (buttonText != null)
            buttonText.text = showInstructionText ? "Hide Instructions" : "Show Instructions";
    }

    private void ApplyVisibility()
    {
        if (refs.instructionsPanel != null)
            refs.instructionsPanel.SetActive(showInstructionText);
    }
}