public class VectorOverlayUIController
{
    private readonly UIReferences refs;
    private readonly NBodyVectorOverlayController vectorOverlayController;

    private bool vectorsVisible = true;

    public bool VectorsVisible => vectorsVisible;

    public VectorOverlayUIController(UIReferences refs, NBodyVectorOverlayController vectorOverlayController)
    {
        this.refs = refs;
        this.vectorOverlayController = vectorOverlayController;
    }

    public void Initialize()
    {
        if (vectorOverlayController != null)
            vectorsVisible = vectorOverlayController.showVectors;

        RefreshLabel();
    }

    public void Toggle()
    {
        if (vectorOverlayController == null)
            return;

        vectorOverlayController.ToggleFromUI();
        vectorsVisible = vectorOverlayController.showVectors;
        RefreshLabel();
    }

    public void Apply(CameraMode mode)
    {
        bool isFreeCam = mode == CameraMode.Free;

        if (refs.vectorToggleButton != null)
            refs.vectorToggleButton.gameObject.SetActive(!isFreeCam);

        RefreshLabel();
    }

    public void RefreshLabel()
    {
        if (refs.vectorToggleButtonText == null)
            return;

        refs.vectorToggleButtonText.text = vectorsVisible ? "Hide Vectors" : "Show Vectors";
    }
}