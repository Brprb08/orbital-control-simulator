using TMPro;

public class TutorialUIController
{
    private readonly UIReferences refs;
    private readonly TutorialController tutorialController;

    public TutorialUIController(UIReferences refs, TutorialController tutorialController)
    {
        this.refs = refs;
        this.tutorialController = tutorialController;
    }

    public void Initialize()
    {
        HideLegacyInstructions();
        RefreshButtonLabels();
    }

    public void Apply()
    {
        HideLegacyInstructions();
        RefreshButtonLabels();
    }

    public void ToggleTutorial()
    {
        if (tutorialController != null)
            tutorialController.ToggleTutorial();
        else if (refs.tutorialPanel != null)
            refs.tutorialPanel.SetActive(!refs.tutorialPanel.activeSelf);

        HideLegacyInstructions();
        RefreshButtonLabels();
    }

    public void OpenTutorial()
    {
        if (tutorialController != null)
            tutorialController.OpenTutorial();
        else if (refs.tutorialPanel != null)
            refs.tutorialPanel.SetActive(true);

        HideLegacyInstructions();
        RefreshButtonLabels();
    }

    public void MinimizeTutorial()
    {
        if (tutorialController != null)
            tutorialController.MinimizeTutorial();
        else if (refs.tutorialPanel != null)
            refs.tutorialPanel.SetActive(false);

        HideLegacyInstructions();
        RefreshButtonLabels();
    }

    public void SkipTutorial()
    {
        if (tutorialController != null)
        {
            tutorialController.SkipTutorial();
        }
        else if (refs.tutorialPanel != null)
        {
            refs.tutorialPanel.SetActive(false);
        }

        HideLegacyInstructions();
        RefreshButtonLabels();
    }

    private void HideLegacyInstructions()
    {
        if (refs.instructionsPanel != null)
            refs.instructionsPanel.SetActive(false);
    }

    private void RefreshButtonLabels()
    {
        TMP_Text instructionsButtonText = refs.instructionsButton != null
            ? refs.instructionsButton.GetComponentInChildren<TMP_Text>(true)
            : null;

        if (instructionsButtonText != null)
            instructionsButtonText.text = IsTutorialOpen() ? "Minimize Tutorial" : "Open Tutorial";

        TMP_Text skipButtonText = refs.skipButton != null
            ? refs.skipButton.GetComponentInChildren<TMP_Text>(true)
            : null;

        if (skipButtonText != null)
            skipButtonText.text = "Minimize";
    }

    private bool IsTutorialOpen()
    {
        if (tutorialController != null)
            return tutorialController.IsOpen;

        return refs.tutorialPanel != null && refs.tutorialPanel.activeSelf;
    }
}
