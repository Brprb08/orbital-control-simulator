using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Manages the body-selection dropdown: populates options from BodyService and
/// keeps the dropdown selection in sync with the currently tracked body.
/// Uses ObservableTMPDropdown to know when the popup opens/closes.
/// </summary>
public class BodyDropdownManager : MonoBehaviour
{
    [Header("References - UI")]
    [SerializeField] private TMP_Dropdown bodyDropdown;
    [SerializeField] private ObservableTMPDropdown dropdownObserver; // popup lifecycle
    private RectTransform _openListRt; // current popup instance (null if closed)

    [Header("References - Scripts")]
    [SerializeField] private TutorialController tutorialController;

    // Services/interfaces
    private ICameraTracker cameraTracker;
    private BodyService bodyService;
    private SimContext ctx;

    // index -> NBody map (avoids name lookups)
    private readonly List<NBody> _optionsMap = new List<NBody>();

    // Event handler refs (so we can unhook cleanly)
    private System.Action<NBody> _onBodyAddedHandler;
    private System.Action<NBody> _onBodyRemovedHandler;
    private System.Action<NBody> _onCentralChangedHandler;

    private System.Action<NBody> _onTrackedBodyChangedHandler;
    private System.Action<Transform> _onTrackedPlaceholderHandler;
    private System.Action<CameraMode> _onModeChangedHandler;

    // Guard to avoid duplicate listener registration
    private bool _valueListenerAdded;

    /// <summary>
    /// Initializes with simulation context. Call from bootstrap; subscriptions happen on enable.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        cameraTracker = ctx.CameraTracker;
        bodyService = ctx.BodyService;
        tutorialController = ctx.TutorialController;

        if (bodyDropdown == null)
            Debug.LogError("[BodyDropdown] Missing TMP_Dropdown.");
        if (bodyService == null)
            Debug.LogError("[BodyDropdown] BodyService missing from context.");

        RebuildOptionsAndSelection();
    }

    private void OnEnable()
    {
        // BodyService events → rebuild on membership changes
        if (bodyService != null)
        {
            _onBodyAddedHandler = OnBodyAdded;
            _onBodyRemovedHandler = OnBodyRemoved;
            _onCentralChangedHandler = OnCentralBodyChanged;

            bodyService.BodyAdded += _onBodyAddedHandler;
            bodyService.BodyRemoved += _onBodyRemovedHandler;
            bodyService.CentralBodyChanged += _onCentralChangedHandler;
        }

        // Camera events (until ICameraTracker exposes events, use concrete controller)
        if (cameraTracker is CameraController controller)
        {
            _onTrackedBodyChangedHandler = OnTrackedBodyChanged;
            _onTrackedPlaceholderHandler = OnTrackedPlaceholderChanged;
            _onModeChangedHandler = OnModeChanged;

            controller.OnTrackedBodyChanged += _onTrackedBodyChangedHandler;
            controller.OnTrackedPlaceholderChanged += _onTrackedPlaceholderHandler;
            controller.OnModeChanged += _onModeChangedHandler;
        }

        // ObservableTMPDropdown popup lifecycle
        if (dropdownObserver != null)
        {
            dropdownObserver.OnDropdownShown += HandleDropdownShown;
            dropdownObserver.OnDropdownHidden += HandleDropdownHidden;
        }

        // UI listener (guarded so Initialize + re-enables don't duplicate)
        if (bodyDropdown != null && !_valueListenerAdded)
        {
            bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
            _valueListenerAdded = true;
        }

        RebuildOptionsAndSelection();
    }

    private void OnDisable()
    {
        if (bodyService != null)
        {
            if (_onBodyAddedHandler != null) bodyService.BodyAdded -= _onBodyAddedHandler;
            if (_onBodyRemovedHandler != null) bodyService.BodyRemoved -= _onBodyRemovedHandler;
            if (_onCentralChangedHandler != null) bodyService.CentralBodyChanged -= _onCentralChangedHandler;
        }

        if (cameraTracker is CameraController controller)
        {
            if (_onTrackedBodyChangedHandler != null) controller.OnTrackedBodyChanged -= _onTrackedBodyChangedHandler;
            if (_onTrackedPlaceholderHandler != null) controller.OnTrackedPlaceholderChanged -= _onTrackedPlaceholderHandler;
            if (_onModeChangedHandler != null) controller.OnModeChanged -= _onModeChangedHandler;
        }

        if (dropdownObserver != null)
        {
            dropdownObserver.OnDropdownShown -= HandleDropdownShown;
            dropdownObserver.OnDropdownHidden -= HandleDropdownHidden;
        }

        if (bodyDropdown != null && _valueListenerAdded)
        {
            bodyDropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);
            _valueListenerAdded = false;
        }

        _openListRt = null;
    }

    private void OnDestroy()
    {
        if (bodyDropdown != null && _valueListenerAdded)
            bodyDropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);
    }

    // ---------- UI -> Sim ----------

    /// <summary>
    /// When a dropdown option is selected, instruct the camera to track the mapped NBody.
    /// </summary>
    public void HandleDropdownValueChanged(int index)
    {
        if (bodyDropdown == null) return;
        if (index < 0 || index >= _optionsMap.Count) return;

        var target = _optionsMap[index];
        if (target == null) return;

        cameraTracker?.TrackBody(target);

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasSwitchedSatellites = true;

        Debug.Log($"[BodyDropdown] Tracking switched to: {target.name}");
    }

    // ---------- Sim -> UI (event handlers) ----------

    private void OnTrackedBodyChanged(NBody _) => UpdateDropdownSelection();
    private void OnTrackedPlaceholderChanged(Transform _) => SetDropdownNoSelection();

    private void OnModeChanged(CameraMode mode)
    {
        if (mode == CameraMode.Free) SetDropdownNoSelection();
        else if (mode == CameraMode.Track) UpdateDropdownSelection();
    }

    private void OnBodyAdded(NBody _) => RebuildOptionsAndSelection();
    private void OnBodyRemoved(NBody _) => RebuildOptionsAndSelection();
    private void OnCentralBodyChanged(NBody _) => RebuildOptionsAndSelection();

    // ---------- Dropdown popup lifecycle (from ObservableTMPDropdown) ----------

    private void HandleDropdownShown(RectTransform listRt) => _openListRt = listRt;
    private void HandleDropdownHidden() => _openListRt = null;

    // ---------- Build / Sync ----------

    /// <summary>Rebuilds options from BodyService and syncs selection to current tracked body.</summary>
    public void RebuildOptionsAndSelection()
    {
        RebuildOptions();
        UpdateDropdownSelection();
    }

    /// <summary>Rebuilds the dropdown options and refreshes index→NBody map.</summary>
    public void RebuildOptions()
    {
        if (bodyDropdown == null || bodyService == null) return;

        _optionsMap.Clear();
        bodyDropdown.ClearOptions();

        var opts = new List<TMP_Dropdown.OptionData>();
        var bodies = bodyService.Bodies;

        if (bodies != null)
        {
            foreach (var b in bodies)
            {
                if (b == null) continue;

                // Centralized inclusion rule
                if (!b.CompareTag("Planet") && !b.CompareTag("Satellite")) continue;

                _optionsMap.Add(b);
                opts.Add(new TMP_Dropdown.OptionData(b.name));
            }
        }

        if (opts.Count == 0)
        {
            // Placeholder when empty (value isn't used)
            opts.Add(new TMP_Dropdown.OptionData("--"));
        }

        bodyDropdown.AddOptions(opts);
        bodyDropdown.RefreshShownValue();
    }

    /// <summary>Reflects the currently tracked body in the dropdown (or clears if none/not present).</summary>
    public void UpdateDropdownSelection()
    {
        if (bodyDropdown == null) return;

        var tracked = cameraTracker != null ? cameraTracker.CurrentBody : null;
        int idx = -1;

        if (tracked != null)
        {
            for (int i = 0; i < _optionsMap.Count; i++)
            {
                if (_optionsMap[i] == tracked) { idx = i; break; }
            }
        }

        if (idx >= 0)
            bodyDropdown.SetValueWithoutNotify(idx);
        else
            SetDropdownNoSelection();

        bodyDropdown.RefreshShownValue();
    }

    /// <summary>
    /// Clears the dropdown selection visually. If you prefer an explicit "-- Select --" option,
    /// add it in RebuildOptions at index 0 and SetValueWithoutNotify(0) here.
    /// </summary>
    private void SetDropdownNoSelection()
    {
        // No explicit placeholder index by default; just refresh.
        bodyDropdown.RefreshShownValue();
    }
}
