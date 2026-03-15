public class TutorialUIController
{
    private readonly UIReferences refs;
    private readonly TutorialController tutorialController;

    public TutorialUIController(UIReferences refs, TutorialController tutorialController)
    {
        this.refs = refs;
        this.tutorialController = tutorialController;
    }

    public void SkipTutorial()
    {
        if (tutorialController != null)
            tutorialController.inTutorialMode = false;

        if (refs.tutorialPanel != null)
            refs.tutorialPanel.SetActive(false);
    }
}