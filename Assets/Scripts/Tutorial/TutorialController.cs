using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the step-by-step tutorial flow: checklist requirements, interstitial pages,
/// input polling for tasks (rotate, zoom, WASD, etc.), and UI updates.
/// External systems flip boolean flags to satisfy certain steps.
/// </summary>
public class TutorialController : MonoBehaviour
{
    private CameraMovement cameraMovement;
    private SimContext ctx;

    [Header("UI References (assign in Inspector)")]
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button backButton;

    [Tooltip("Parent transform where requirement items (toggles) will be spawned.")]
    [SerializeField] private Transform requirementListRoot;

    [Tooltip("Prefab for a requirement item (Toggle with a label).")]
    [SerializeField] private Toggle requirementItemPrefab;

    [Header("Behavior")]
    [Tooltip("Close (hide) the panel when the last step is confirmed (Next on final).")]
    [SerializeField] private bool closePanelWhenDone = true;

    public bool inTutorialMode = true;

    // Progress flags set by outside systems or input polling
    public bool hasSwitchedSatellites = false;
    public bool hasSwitchedToEarthCam = false;
    public bool hasNameBeenEnteredForSatellite = false;     // reserved
    public bool hasPositionBeenEnteredForSatellite = false; // reserved
    public bool hasMassBeenEnteredForSatellite = false;
    public bool hasRadiusBeenEnteredForSatellite = false;
    public bool hasSatelliteBeenPlaced = false;
    public bool hasAddVelocity = false;
    public bool hasSetVelocity = false;
    public bool hasChangedTimeScale = false;
    public bool hasAppliedThrust = false;
    public bool hasSetupNode = false;
    public bool hasPlacedNode = false;

    // Interstitial state
    private bool[] interstitialShown;
    private bool[] preInterstitialConsumed;

    [SerializeField] private float preInterstitialDelay = 2f;

    private bool interstitialCountdownActive = false;
    private float interstitialCountdown = 0f;
    private float mainCountdown = 0f;

    private enum StepPhase { Main, Interstitial }
    private StepPhase phase = StepPhase.Main;
    private float interstitialTimer = 0f;

    private readonly TutorialProgress progress = new();

    [Header("Input thresholds")]
    [SerializeField] private float rotatePixelsThreshold = 30f;
    [SerializeField] private float scrollThreshold = 2f;

    private bool rmbHolding = false;
    private Vector2 rmbStartPos;
    private float zoomAccumAbs = 0f;

    private TutorialStep[] steps;
    private bool sequenceInitialized;

    // Step/UI state
    private int stepIndex = 0;
    private readonly List<Toggle> activeReqToggles = new();

    public bool IsOpen => tutorialPanel != null ? tutorialPanel.activeSelf : gameObject.activeSelf;

    /// <summary>
    /// Injects the simulation context and caches camera movement.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraMovement = ctx.CameraMovement;
    }

    /// <summary>
    /// Wires button events.
    /// </summary>
    private void Awake()
    {
        if (nextButton) nextButton.onClick.AddListener(OnNextClicked);
        if (backButton) backButton.onClick.AddListener(OnBackClicked);
    }

    /// <summary>
    /// Opens the tutorial without resetting the current page.
    /// </summary>
    private void OnEnable()
    {
        EnsureSequenceInitialized();
        inTutorialMode = true;
        BringToFront();
        RebuildUIForStep();
        UpdateButtons();
    }

    /// <summary>
    /// Per-frame step logic: polls requirements, handles timers, and manages interstitial transitions.
    /// </summary>
    private void Update()
    {
        if (steps == null || steps.Length == 0)
            return;

        var step = steps[stepIndex];

        if (phase == StepPhase.Main)
        {
            var reqs = step.requirements;
            for (int i = 0; i < reqs.Length; i++)
            {
                switch (reqs[i].type)
                {
                    case RequirementType.RotateViewRMB:
                        TrackRotateRMB();
                        break;

                    case RequirementType.ZoomScroll:
                        TrackZoomScroll();
                        break;

                    case RequirementType.SwitchSatelliteTrack:
                        if (!progress.IsComplete(RequirementType.SwitchSatelliteTrack) && hasSwitchedSatellites)
                            progress.SetComplete(RequirementType.SwitchSatelliteTrack);
                        break;

                    case RequirementType.SwitchToEarthCam:
                        if (!progress.IsComplete(RequirementType.SwitchToEarthCam) && hasSwitchedToEarthCam)
                            progress.SetComplete(RequirementType.SwitchToEarthCam);
                        break;

                    case RequirementType.SwitchToFreeCam:
                        if (!progress.IsComplete(RequirementType.SwitchToFreeCam) && cameraMovement != null && cameraMovement.IsFreeCamMode)
                            progress.SetComplete(RequirementType.SwitchToFreeCam);
                        break;

                    case RequirementType.PressW:
                        PressedW();
                        break;

                    case RequirementType.PressA:
                        PressedA();
                        break;

                    case RequirementType.PressS:
                        PressedS();
                        break;

                    case RequirementType.PressD:
                        PressedD();
                        break;

                    case RequirementType.RotateViewRMBFree:
                        TrackRotateRMB();
                        break;

                    case RequirementType.EnterPosition:
                        if (!progress.IsComplete(RequirementType.EnterPosition) && hasPositionBeenEnteredForSatellite)
                            progress.SetComplete(RequirementType.EnterPosition);
                        break;

                    case RequirementType.EnterMass:
                        if (!progress.IsComplete(RequirementType.EnterMass) && hasMassBeenEnteredForSatellite)
                            progress.SetComplete(RequirementType.EnterMass);
                        break;

                    case RequirementType.EnterRadius:
                        if (!progress.IsComplete(RequirementType.EnterRadius) && hasRadiusBeenEnteredForSatellite)
                            progress.SetComplete(RequirementType.EnterRadius);
                        break;

                    case RequirementType.PlaceSatellite:
                        if (!progress.IsComplete(RequirementType.PlaceSatellite) && hasSatelliteBeenPlaced)
                            progress.SetComplete(RequirementType.PlaceSatellite);
                        break;

                    case RequirementType.AddVelocity:
                        if (!progress.IsComplete(RequirementType.AddVelocity) && hasAddVelocity)
                            progress.SetComplete(RequirementType.AddVelocity);
                        break;

                    case RequirementType.SetVelocity:
                        if (!progress.IsComplete(RequirementType.SetVelocity) && hasSetVelocity)
                            progress.SetComplete(RequirementType.SetVelocity);
                        break;

                    case RequirementType.ChangedTimeScale:
                        if (!progress.IsComplete(RequirementType.ChangedTimeScale) && hasChangedTimeScale)
                            progress.SetComplete(RequirementType.ChangedTimeScale);
                        break;

                    case RequirementType.ApplyThrust:
                        if (!progress.IsComplete(RequirementType.ApplyThrust) && hasAppliedThrust)
                            progress.SetComplete(RequirementType.ApplyThrust);
                        break;

                    case RequirementType.ClickSetupForNode:
                        if (!progress.IsComplete(RequirementType.ClickSetupForNode) && hasSetupNode)
                            progress.SetComplete(RequirementType.ClickSetupForNode);
                        break;

                    case RequirementType.PlaceManeuverNode:
                        if (!progress.IsComplete(RequirementType.PlaceManeuverNode) && hasPlacedNode)
                            progress.SetComplete(RequirementType.PlaceManeuverNode);
                        break;

                    case RequirementType.None:
                    default:
                        break;
                }
            }

            // Update checklist UI & Next button
            bool allMet = AreAllRequirementsMet(step.requirements);

            for (int i = 0; i < activeReqToggles.Count && i < step.requirements.Length; i++)
                if (activeReqToggles[i]) activeReqToggles[i].isOn = progress.IsComplete(step.requirements[i].type);

            if (nextButton) nextButton.interactable = allMet;

            // Pre-interstitial countdown, if the step uses an interstitial
            if (step.showInterstitialAfterComplete && !interstitialShown[stepIndex])
            {
                if (allMet)
                {
                    if (!preInterstitialConsumed[stepIndex])
                    {
                        preInterstitialConsumed[stepIndex] = true;
                        interstitialCountdownActive = true;
                        interstitialCountdown = preInterstitialDelay;
                    }
                    else if (interstitialCountdownActive)
                    {
                        interstitialCountdown -= Time.unscaledDeltaTime;
                        if (interstitialCountdown <= 0f)
                        {
                            interstitialCountdownActive = false;
                            EnterInterstitial(step);
                        }
                    }
                }
                else
                {
                    // Cancel pending countdown if requirements become unmet
                    interstitialCountdownActive = false;
                    interstitialCountdown = 0f;
                }
            }
            // Auto-advance directly from the checklist for steps without interstitials (opt-in)
            else if (!step.showInterstitialAfterComplete)
            {
                if (allMet && steps[stepIndex].autoAdvanceFromInterstitial)
                {
                    if (mainCountdown <= 0f)
                        mainCountdown = preInterstitialDelay;

                    mainCountdown -= Time.unscaledDeltaTime;
                    if (mainCountdown <= 0f)
                    {
                        AdvanceStep();
                    }
                }
                else
                {
                    mainCountdown = 0f;
                }
            }
        }
        else if (phase == StepPhase.Interstitial)
        {
            // Optional auto-advance while on the interstitial
            if (steps[stepIndex].autoAdvanceFromInterstitial && interstitialTimer > 0f)
            {
                interstitialTimer -= Time.unscaledDeltaTime;
                if (interstitialTimer <= 0f)
                {
                    AdvanceStep();
                }
            }
        }
    }

    /// <summary>
    /// Switches from checklist to interstitial view for the current step.
    /// </summary>
    private void EnterInterstitial(TutorialStep step)
    {
        interstitialShown[stepIndex] = true;
        phase = StepPhase.Interstitial;

        if (bodyText) bodyText.text = string.IsNullOrEmpty(step.interstitialBody)
            ? "Nice. Continue to the next step."
            : step.interstitialBody;

        foreach (var t in activeReqToggles) if (t) Destroy(t.gameObject);
        activeReqToggles.Clear();

        if (nextButton)
        {
            nextButton.interactable = true;
            var lbl = nextButton.GetComponentInChildren<TMP_Text>(true);
            if (lbl) lbl.text = "Continue";
        }

        interstitialTimer = (step.autoAdvanceFromInterstitial && step.autoAdvanceDelay > 0f)
            ? step.autoAdvanceDelay
            : 0f;

        ReflowLayout();
    }

    /// <summary>
    /// Clears all progress and restarts the tutorial from the first step.
    /// </summary>
    public void ResetAllProgress()
    {
        EnsureSequenceInitialized();
        inTutorialMode = true;
        ResetExternalFlags();
        progress.ResetAll();

        if (interstitialShown != null)
            System.Array.Clear(interstitialShown, 0, interstitialShown.Length);
        if (preInterstitialConsumed != null)
            System.Array.Clear(preInterstitialConsumed, 0, preInterstitialConsumed.Length);

        ResetTransients();
        stepIndex = 0;
        phase = StepPhase.Main;
        RebuildUIForStep();
        UpdateButtons();
    }

    public void OpenTutorial()
    {
        EnsureSequenceInitialized();
        inTutorialMode = true;

        if (tutorialPanel != null && !tutorialPanel.activeSelf)
        {
            tutorialPanel.SetActive(true);
            return;
        }

        BringToFront();
        RebuildUIForStep();
        UpdateButtons();
    }

    public void MinimizeTutorial()
    {
        if (tutorialPanel != null)
        {
            tutorialPanel.SetActive(false);
            return;
        }

        gameObject.SetActive(false);
    }

    public void ToggleTutorial()
    {
        if (IsOpen)
            MinimizeTutorial();
        else
            OpenTutorial();
    }

    /// <summary>
    /// Next/Continue button handler: advances through interstitial or moves to the next step.
    /// Closes the panel at the end if configured to do so.
    /// </summary>
    public void OnNextClicked()
    {
        if (phase == StepPhase.Interstitial)
        {
            AdvanceStep();
            return;
        }

        if (!AreAllRequirementsMet(steps[stepIndex].requirements))
            return;

        if (stepIndex < steps.Length - 1)
        {
            stepIndex++;
            phase = StepPhase.Main;
            ResetTransients();
            RebuildUIForStep();
            UpdateButtons();
        }
        else
        {
            if (closePanelWhenDone && tutorialPanel)
            {
                tutorialPanel.SetActive(false);
                inTutorialMode = false;
            }
            else if (nextButton) nextButton.interactable = false;
        }
    }

    /// <summary>
    /// Advances to the next step and rebuilds the UI; closes the panel at the end if configured.
    /// </summary>
    private void AdvanceStep()
    {
        if (stepIndex < steps.Length - 1)
        {
            stepIndex++;
            phase = StepPhase.Main;
            ResetTransients();
            RebuildUIForStep();
            UpdateButtons();
        }
        else
        {
            if (closePanelWhenDone && tutorialPanel)
            {
                tutorialPanel.SetActive(false);
                inTutorialMode = false;
            }
            else if (nextButton) nextButton.interactable = false;
        }
    }

    /// <summary>
    /// Back button handler. From interstitial, returns to the same step checklist; otherwise goes to the previous step.
    /// </summary>
    private void OnBackClicked()
    {
        if (phase == StepPhase.Interstitial)
        {
            phase = StepPhase.Main;

            interstitialTimer = 0f;
            interstitialCountdownActive = false;
            interstitialCountdown = 0f;
            mainCountdown = 0f;

            RebuildUIForStep();
            UpdateButtons();
            return;
        }

        if (stepIndex > 0)
        {
            stepIndex--;
            phase = StepPhase.Main;

            interstitialCountdownActive = false;
            interstitialCountdown = 0f;
            mainCountdown = 0f;

            ResetTransients();
            RebuildUIForStep();
            UpdateButtons();
        }
    }

    /// <summary>
    /// Rebuilds text, checklist items, and button labels for the current step and phase.
    /// </summary>
    private void RebuildUIForStep()
    {
        if (steps == null || steps.Length == 0)
            return;

        BringToFront();
        var step = steps[stepIndex];
        if (bodyText) bodyText.text = step.body;

        foreach (var t in activeReqToggles) if (t) Destroy(t.gameObject);
        activeReqToggles.Clear();

        if (phase == StepPhase.Main)
        {
            var reqs = step.requirements;
            if (requirementListRoot && requirementItemPrefab)
            {
                for (int i = 0; i < reqs.Length; i++)
                {
                    var item = Instantiate(requirementItemPrefab, requirementListRoot);
                    item.isOn = progress.IsComplete(reqs[i].type);
                    item.interactable = false;
                    var label = item.GetComponentInChildren<Text>(true);
                    if (label) label.text = reqs[i].label;
                    var tmpLabel = item.GetComponentInChildren<TMP_Text>(true);
                    if (tmpLabel) tmpLabel.text = reqs[i].label;
                    activeReqToggles.Add(item);
                }
            }
        }

        if (nextButton)
        {
            var lbl = nextButton.GetComponentInChildren<TMP_Text>(true);
            if (lbl)
            {
                bool last = (stepIndex == steps.Length - 1);
                lbl.text = (phase == StepPhase.Interstitial) ? "Continue" : (last ? "Done" : "Next");
            }
        }

        ReflowLayout();
    }

    /// <summary>
    /// Enables/disables navigation buttons based on progress and phase.
    /// </summary>
    private void UpdateButtons()
    {
        if (steps == null || steps.Length == 0)
            return;

        if (backButton) backButton.interactable = (stepIndex > 0);
        if (nextButton)
        {
            bool allMet = AreAllRequirementsMet(steps[stepIndex].requirements);
            nextButton.interactable = allMet || phase == StepPhase.Interstitial;
        }
    }

    /// <summary>
    /// Clears transient input and timer state used within a step.
    /// </summary>
    private void ResetTransients()
    {
        rmbHolding = false;
        rmbStartPos = Vector2.zero;
        zoomAccumAbs = 0f;

        interstitialCountdownActive = false;
        interstitialCountdown = 0f;
    }

    /// <summary>
    /// Returns whether all requirements for the provided list are satisfied.
    /// </summary>
    private bool AreAllRequirementsMet(RequirementDef[] reqs)
    {
        for (int i = 0; i < reqs.Length; i++)
            if (!progress.IsComplete(reqs[i].type)) return false;
        return true;
    }

    // ---- Input polling that marks requirements complete ----

    /// <summary>
    /// Marks the W key requirement when pressed.
    /// </summary>
    public void PressedW()
    {
        if (progress.IsComplete(RequirementType.PressW) && cameraMovement != null && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.W))
            progress.SetComplete(RequirementType.PressW);
    }

    /// <summary>
    /// Marks the A key requirement when pressed.
    /// </summary>
    public void PressedA()
    {
        if (progress.IsComplete(RequirementType.PressA) && cameraMovement != null && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.A))
            progress.SetComplete(RequirementType.PressA);
    }

    /// <summary>
    /// Marks the S key requirement when pressed.
    /// </summary>
    public void PressedS()
    {
        if (progress.IsComplete(RequirementType.PressS) && cameraMovement != null && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.S))
            progress.SetComplete(RequirementType.PressS);
    }

    /// <summary>
    /// Marks the D key requirement when pressed.
    /// </summary>
    public void PressedD()
    {
        if (progress.IsComplete(RequirementType.PressD) && cameraMovement != null && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.D))
            progress.SetComplete(RequirementType.PressD);
    }

    /// <summary>
    /// Tracks right-mouse dragging to satisfy rotate requirements (separate flags for free cam vs tracked cam).
    /// </summary>
    private void TrackRotateRMB()
    {
        if (cameraMovement != null && cameraMovement.IsFreeCamMode)
        {
            if (progress.IsComplete(RequirementType.RotateViewRMBFree)) return;
        }
        else
        {
            if (progress.IsComplete(RequirementType.RotateViewRMB)) return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            rmbHolding = true;
            rmbStartPos = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(1))
        {
            rmbHolding = false;
        }
        if (rmbHolding)
        {
            var delta = (Vector2)Input.mousePosition - rmbStartPos;
            if (delta.magnitude >= rotatePixelsThreshold)
            {
                if (cameraMovement != null && cameraMovement.IsFreeCamMode)
                    progress.SetComplete(RequirementType.RotateViewRMBFree);
                else
                    progress.SetComplete(RequirementType.RotateViewRMB);

                rmbHolding = false;
            }
        }
    }

    /// <summary>
    /// Accumulates scroll input until the zoom requirement threshold is met.
    /// </summary>
    private void TrackZoomScroll()
    {
        if (progress.IsComplete(RequirementType.ZoomScroll)) return;

        float s = Input.mouseScrollDelta.y;
        if (Mathf.Abs(s) > 0f)
        {
            zoomAccumAbs += Mathf.Abs(s);
            if (zoomAccumAbs >= scrollThreshold)
                progress.SetComplete(RequirementType.ZoomScroll);
        }
    }

    /// <summary>
    /// Forces a layout refresh after text or list changes.
    /// </summary>
    private void ReflowLayout()
    {
        if (bodyText != null)
            bodyText.ForceMeshUpdate();

        Canvas.ForceUpdateCanvases();

        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }

    public void SkipTutorial()
    {
        inTutorialMode = false;
        if (tutorialPanel)
            tutorialPanel.SetActive(false);
    }

    private void EnsureSequenceInitialized()
    {
        if (sequenceInitialized && steps != null && steps.Length > 0)
            return;

        steps = TutorialSequence.Default();
        interstitialShown = new bool[steps.Length];
        preInterstitialConsumed = new bool[steps.Length];
        sequenceInitialized = true;

        stepIndex = 0;
        phase = StepPhase.Main;
        ResetExternalFlags();
        progress.ResetAll();
        ResetTransients();
    }

    private void BringToFront()
    {
        if (tutorialPanel != null)
            tutorialPanel.transform.SetAsLastSibling();
    }

    private void ResetExternalFlags()
    {
        hasSwitchedSatellites = false;
        hasSwitchedToEarthCam = false;
        hasNameBeenEnteredForSatellite = false;
        hasPositionBeenEnteredForSatellite = false;
        hasMassBeenEnteredForSatellite = false;
        hasRadiusBeenEnteredForSatellite = false;
        hasSatelliteBeenPlaced = false;
        hasAddVelocity = false;
        hasSetVelocity = false;
        hasChangedTimeScale = false;
        hasAppliedThrust = false;
        hasSetupNode = false;
        hasPlacedNode = false;
    }
}
