using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    // External flags updated by other systems
    public bool hasSwitchedSatellites = false;
    public bool hasSwitchedToEarthCam = false;
    public bool hasNameBeenEnteredForSatellite = false;  // currently unused in steps, keep if you’ll use later
    public bool hasPositionBeenEnteredForSatellite = false; // currently unused in steps
    public bool hasMassBeenEnteredForSatellite = false;
    public bool hasRadiusBeenEnteredForSatellite = false;
    public bool hasSatelliteBeenPlaced = false;
    public bool hasClickAndDrag = false;
    public bool hasAddVelocity = false;
    public bool hasSetVelocity = false;
    public bool hasChangedTimeScale = false;
    public bool hasAppliedThrust = false;
    public bool hasSetupNode = false;
    public bool hasPlacedNode = false;

    // One-time interstitial flow tracking
    private bool[] interstitialShown;        // true after we show the interstitial for a step
    private bool[] preInterstitialConsumed;  // true once the 2s pre-interstitial timer has started for a step

    // One-time pre-interstitial countdown (while still on the checklist page)
    [SerializeField] private float preInterstitialDelay = 2f;

    private bool interstitialCountdownActive = false;
    private float interstitialCountdown = 0f;
    private float mainCountdown = 0f;

    private enum StepPhase { Main, Interstitial }
    private StepPhase phase = StepPhase.Main;
    private float interstitialTimer = 0f; // timer used while on interstitial if that step wants auto-advance

    private readonly TutorialProgress progress = new();

    [Header("Input thresholds")]
    [SerializeField] private float rotatePixelsThreshold = 30f;
    [SerializeField] private float scrollThreshold = 2f;

    private bool rmbHolding = false;
    private Vector2 rmbStartPos;
    private float zoomAccumAbs = 0f;

    private TutorialStep[] steps;

    // --------------- Internal state ---------------
    private int stepIndex = 0;
    private readonly List<Toggle> activeReqToggles = new();

    // ------------------ Lifecycle ------------------
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        this.cameraMovement = ctx.CameraMovement;
    }

    private void Awake()
    {
        if (nextButton) nextButton.onClick.AddListener(OnNextClicked);
        if (backButton) backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnEnable()
    {
        stepIndex = 0;
        phase = StepPhase.Main;

        steps = TutorialSequence.Default();

        interstitialShown = new bool[steps.Length];
        preInterstitialConsumed = new bool[steps.Length];

        ResetTransients();
        RebuildUIForStep();
        UpdateButtons();
    }

    private void Update()
    {
        var step = steps[stepIndex];

        if (phase == StepPhase.Main)
        {
            // Poll only what’s needed for the current step
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

                    case RequirementType.ClickSatelliteAndDrag:
                        if (!progress.IsComplete(RequirementType.ClickSatelliteAndDrag) && hasClickAndDrag)
                            progress.SetComplete(RequirementType.ClickSatelliteAndDrag);
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

            // One-time pre-interstitial countdown (stay on checklist first time requirements are met)
            if (step.showInterstitialAfterComplete && !interstitialShown[stepIndex])
            {
                if (allMet)
                {
                    if (!preInterstitialConsumed[stepIndex])
                    {
                        preInterstitialConsumed[stepIndex] = true; // consume so it won’t restart later
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
                    // Cancel a pending countdown if user falls below requirements; keep consumed=true so it won't restart
                    interstitialCountdownActive = false;
                    interstitialCountdown = 0f;
                }
            }
            // NEW: only allow checklist auto-advance on steps that do NOT use an interstitial
            else if (!step.showInterstitialAfterComplete)
            {
                if (allMet && steps[stepIndex].autoAdvanceFromInterstitial)
                {
                    if (mainCountdown <= 0f)
                        mainCountdown = preInterstitialDelay; // start once when first satisfied

                    mainCountdown -= Time.unscaledDeltaTime;
                    if (mainCountdown <= 0f)
                    {
                        AdvanceStep();
                    }
                }
                else
                {
                    mainCountdown = 0f; // no countdown if not allMet or autoAdvance disabled
                }
            }

        }
        else if (phase == StepPhase.Interstitial)
        {
            // Optional auto-advance while on the interstitial page
            if (steps[stepIndex].autoAdvanceFromInterstitial && interstitialTimer > 0f)
            {
                interstitialTimer -= Time.unscaledDeltaTime;
                if (interstitialTimer <= 0f)
                {
                    AdvanceStep(); // same as clicking Continue
                }
            }
        }
    }

    // ------------------ Interstitial ------------------
    private void EnterInterstitial(TutorialStep step)
    {
        interstitialShown[stepIndex] = true;
        phase = StepPhase.Interstitial;

        if (bodyText) bodyText.text = string.IsNullOrEmpty(step.interstitialBody)
            ? "Nice! Next we will go into blah blah blah…"
            : step.interstitialBody;

        // Clear checklist UI
        foreach (var t in activeReqToggles) if (t) Destroy(t.gameObject);
        activeReqToggles.Clear();

        // Button label & interactivity
        if (nextButton)
        {
            nextButton.interactable = true;
            var lbl = nextButton.GetComponentInChildren<TMP_Text>(true);
            if (lbl) lbl.text = "Continue";
        }

        // Interstitial auto-advance (no extra 2s here; that delay happened before entering)
        interstitialTimer = (step.autoAdvanceFromInterstitial && step.autoAdvanceDelay > 0f)
            ? step.autoAdvanceDelay
            : 0f;

        ReflowLayout();
    }

    // ------------------ Public notifiers ------------------
    // public void NotifyRotatedCamera()
    // {
    //     progress.SetComplete(RequirementType.RotateViewRMB);
    // }

    // public void NotifyZoomedAbs(float amountAbs)
    // {
    //     zoomAccumAbs += Mathf.Abs(amountAbs);
    //     if (zoomAccumAbs >= scrollThreshold)
    //         progress.SetComplete(RequirementType.ZoomScroll);
    // }

    public void ResetAllProgress()
    {
        System.Array.Clear(interstitialShown, 0, interstitialShown.Length);
        System.Array.Clear(preInterstitialConsumed, 0, preInterstitialConsumed.Length);

        progress.SetComplete(RequirementType.RotateViewRMB, false);
        progress.SetComplete(RequirementType.ZoomScroll, false);
        progress.SetComplete(RequirementType.SwitchSatelliteTrack, false);
        progress.SetComplete(RequirementType.SwitchToEarthCam, false);
        progress.SetComplete(RequirementType.SwitchToFreeCam, false);
        progress.SetComplete(RequirementType.PressW, false);
        progress.SetComplete(RequirementType.PressA, false);
        progress.SetComplete(RequirementType.PressS, false);
        progress.SetComplete(RequirementType.PressD, false);
        progress.SetComplete(RequirementType.RotateViewRMBFree, false);
        progress.SetComplete(RequirementType.EnterMass, false);
        progress.SetComplete(RequirementType.EnterRadius, false);
        progress.SetComplete(RequirementType.PlaceSatellite, false);
        progress.SetComplete(RequirementType.ClickSatelliteAndDrag, false);
        progress.SetComplete(RequirementType.AddVelocity, false);
        progress.SetComplete(RequirementType.SetVelocity, false);

        ResetTransients();
        stepIndex = 0;
        phase = StepPhase.Main;
        RebuildUIForStep();
        UpdateButtons();
    }

    // ------------------ Buttons ------------------
    public void OnNextClicked()
    {
        if (phase == StepPhase.Interstitial)
        {
            AdvanceStep();
            return;
        }

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

    private void OnBackClicked()
    {
        // From an interstitial, return to the SAME step’s checklist
        if (phase == StepPhase.Interstitial)
        {
            phase = StepPhase.Main;

            // kill any timers/counters so no forward jump happens
            interstitialTimer = 0f;
            interstitialCountdownActive = false;
            interstitialCountdown = 0f;
            mainCountdown = 0f;

            RebuildUIForStep();
            UpdateButtons();
            return;
        }

        // From checklist, go to previous step
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

    // ------------------ UI Helpers ------------------
    private void RebuildUIForStep()
    {
        var step = steps[stepIndex];
        if (bodyText) bodyText.text = step.body;

        // rebuild checklist only in Main phase
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

    private void UpdateButtons()
    {
        if (backButton) backButton.interactable = (stepIndex > 0);
        if (nextButton)
        {
            bool allMet = AreAllRequirementsMet(steps[stepIndex].requirements);
            nextButton.interactable = allMet || phase == StepPhase.Interstitial;
        }
    }

    private void ResetTransients()
    {
        rmbHolding = false;
        rmbStartPos = Vector2.zero;
        zoomAccumAbs = 0f;

        interstitialCountdownActive = false;
        interstitialCountdown = 0f;
    }

    // ------------------ Requirement Logic ------------------
    private bool AreAllRequirementsMet(RequirementDef[] reqs)
    {
        for (int i = 0; i < reqs.Length; i++)
            if (!progress.IsComplete(reqs[i].type)) return false;
        return true;
    }

    // ------------------ Input Polling ------------------
    public void PressedW()
    {
        if (progress.IsComplete(RequirementType.PressW) && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.W))
            progress.SetComplete(RequirementType.PressW);
    }

    public void PressedA()
    {
        if (progress.IsComplete(RequirementType.PressA) && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.A))
            progress.SetComplete(RequirementType.PressA);
    }

    public void PressedS()
    {
        if (progress.IsComplete(RequirementType.PressS) && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.S))
            progress.SetComplete(RequirementType.PressS);
    }

    public void PressedD()
    {
        if (progress.IsComplete(RequirementType.PressD) && cameraMovement.IsFreeCamMode) return;
        if (Input.GetKeyDown(KeyCode.D))
            progress.SetComplete(RequirementType.PressD);
    }

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

                rmbHolding = false; // mark done
            }
        }
    }

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

    private void ReflowLayout()
    {
        if (bodyText != null)
            bodyText.ForceMeshUpdate();

        Canvas.ForceUpdateCanvases();

        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }
}
