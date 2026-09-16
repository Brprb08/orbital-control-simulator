using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Populates and maintains a body-selection dropdown backed by BodyService,
/// and keeps the selection synchronized with the camera’s tracked body.
/// Listens to camera and body list changes, and routes user selections to tracking.
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
    private ConstellationRegistry constellationRegistry;
    private SimContext ctx;

    private enum DropdownEntryKind
    {
        Body,
        ConstellationPlane
    }

    private readonly struct DropdownEntry
    {
        public readonly DropdownEntryKind Kind;
        public readonly NBody Body;
        public readonly ConstellationRecord Constellation;
        public readonly ConstellationPlaneRecord Plane;
        public readonly string Label;

        public DropdownEntry(NBody body)
        {
            Kind = DropdownEntryKind.Body;
            Body = body;
            Constellation = null;
            Plane = null;
            Label = body != null ? body.name : "--";
        }

        public DropdownEntry(ConstellationRecord constellation, ConstellationPlaneRecord plane)
        {
            Kind = DropdownEntryKind.ConstellationPlane;
            Body = null;
            Constellation = constellation;
            Plane = plane;
            Label = plane != null ? plane.DisplayName : "--";
        }

        public NBody ResolveTrackingBody()
        {
            return Kind == DropdownEntryKind.ConstellationPlane
                ? Plane?.RepresentativeBody
                : Body;
        }
    }

    // index -> typed selection map (avoids name lookups)
    private readonly List<DropdownEntry> _optionsMap = new List<DropdownEntry>();

    // Event handler refs 
    private System.Action<NBody> _onBodyAddedHandler;
    private System.Action<NBody> _onBodyRemovedHandler;

    private System.Action<NBody> _onTrackedBodyChangedHandler;
    private System.Action<Transform> _onTrackedPlaceholderHandler;
    private System.Action<CameraMode> _onModeChangedHandler;

    // Guard to avoid duplicate listener registration
    private bool _valueListenerAdded;
    private bool _registryListenerAdded;
    private bool _rebuildQueued;

    /// <summary>
    /// Injects context references and builds initial options.
    /// </summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
        cameraTracker = ctx.CameraTracker;
        bodyService = ctx.BodyService;
        constellationRegistry = ctx.ConstellationRegistry;
        tutorialController = ctx.TutorialController;

        if (bodyDropdown == null)
            Debug.LogError("[BodyDropdown] Missing TMP_Dropdown.");
        if (bodyService == null)
            Debug.LogError("[BodyDropdown] BodyService missing from context.");

        BindRegistryListener();
        RebuildOptionsAndSelection();
    }

    private void OnEnable()
    {
        if (bodyService != null)
        {
            _onBodyAddedHandler = OnBodyAdded;
            _onBodyRemovedHandler = OnBodyRemoved;

            bodyService.BodyAdded += _onBodyAddedHandler;
            bodyService.BodyRemoved += _onBodyRemovedHandler;
        }

        if (cameraTracker is CameraController controller)
        {
            _onTrackedBodyChangedHandler = OnTrackedBodyChanged;
            _onTrackedPlaceholderHandler = OnTrackedPlaceholderChanged;
            _onModeChangedHandler = OnModeChanged;

            controller.OnTrackedBodyChanged += _onTrackedBodyChangedHandler;
            controller.OnTrackedPlaceholderChanged += _onTrackedPlaceholderHandler;
            controller.OnModeChanged += _onModeChangedHandler;
        }

        if (dropdownObserver != null)
        {
            dropdownObserver.OnDropdownShown += HandleDropdownShown;
            dropdownObserver.OnDropdownHidden += HandleDropdownHidden;
        }

        if (bodyDropdown != null && !_valueListenerAdded)
        {
            bodyDropdown.onValueChanged.AddListener(HandleDropdownValueChanged);
            _valueListenerAdded = true;
        }

        BindRegistryListener();
        RebuildOptionsAndSelection();
    }

    private void OnDisable()
    {
        if (bodyService != null)
        {
            if (_onBodyAddedHandler != null) bodyService.BodyAdded -= _onBodyAddedHandler;
            if (_onBodyRemovedHandler != null) bodyService.BodyRemoved -= _onBodyRemovedHandler;
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

        UnbindRegistryListener();
        _openListRt = null;
    }

    private void OnDestroy()
    {
        if (bodyDropdown != null && _valueListenerAdded)
            bodyDropdown.onValueChanged.RemoveListener(HandleDropdownValueChanged);

        UnbindRegistryListener();
    }

    private void LateUpdate()
    {
        if (!_rebuildQueued)
            return;

        _rebuildQueued = false;
        RebuildOptionsAndSelection();
    }

    /// <summary>
    /// Tracks the selected body when the user chooses an option.
    /// </summary>
    public void HandleDropdownValueChanged(int index)
    {
        if (bodyDropdown == null) return;
        if (ctx != null && ctx.ThrustController != null && ctx.ThrustController.IsNodeBurnActive)
        {
            UpdateDropdownSelection();
            return;
        }
        if (index < 0 || index >= _optionsMap.Count) return;

        var entry = _optionsMap[index];
        var target = entry.ResolveTrackingBody();
        if (target == null) return;

        cameraTracker?.TrackBody(target);

        if (tutorialController != null && tutorialController.inTutorialMode)
            tutorialController.hasSwitchedSatellites = true;

        Debug.Log($"[BodyDropdown] Tracking switched to: {entry.Label}");
    }

    public void SetInteractable(bool interactable)
    {
        if (bodyDropdown != null)
            bodyDropdown.interactable = interactable;
    }

    private void OnTrackedBodyChanged(NBody _) => UpdateDropdownSelection();
    private void OnTrackedPlaceholderChanged(Transform _) => SetDropdownNoSelection();

    private void OnModeChanged(CameraMode mode)
    {
        if (mode == CameraMode.Free) SetDropdownNoSelection();
        else if (mode == CameraMode.Track) UpdateDropdownSelection();
    }

    private void OnBodyAdded(NBody _) => QueueRebuildOptionsAndSelection();
    private void OnBodyRemoved(NBody _) => QueueRebuildOptionsAndSelection();
    private void OnCentralBodyChanged(NBody _) => QueueRebuildOptionsAndSelection();
    private void OnConstellationRegistryChanged() => QueueRebuildOptionsAndSelection();

    private void HandleDropdownShown(RectTransform listRt) => _openListRt = listRt;
    private void HandleDropdownHidden() => _openListRt = null;

    /// <summary>
    /// Rebuilds the option list and syncs selection to the tracked body.
    /// </summary>
    public void RebuildOptionsAndSelection()
    {
        _rebuildQueued = false;
        RebuildOptions();
        UpdateDropdownSelection();
    }

    private void QueueRebuildOptionsAndSelection()
    {
        _rebuildQueued = true;
    }

    /// <summary>
    /// Rebuilds options from current bodies and refreshes the index map.
    /// </summary>
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

                if (!b.CompareTag("Planet") && !b.CompareTag("Satellite")) continue;

                if (constellationRegistry != null && constellationRegistry.IsConstellationMember(b))
                    continue;

                _optionsMap.Add(new DropdownEntry(b));
                opts.Add(new TMP_Dropdown.OptionData(b.name));
            }
        }

        if (constellationRegistry != null)
        {
            var constellations = constellationRegistry.Constellations;
            for (int c = 0; c < constellations.Count; c++)
            {
                ConstellationRecord constellation = constellations[c];
                if (constellation == null)
                    continue;

                var planes = constellation.Planes;
                for (int p = 0; p < planes.Count; p++)
                {
                    ConstellationPlaneRecord plane = planes[p];
                    if (plane == null || plane.RepresentativeBody == null)
                        continue;

                    _optionsMap.Add(new DropdownEntry(constellation, plane));
                    opts.Add(new TMP_Dropdown.OptionData(plane.DisplayName));
                }
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

    /// <summary>
    /// Updates the dropdown selection to reflect the currently tracked body.
    /// </summary>
    public void UpdateDropdownSelection()
    {
        if (bodyDropdown == null) return;

        var tracked = cameraTracker != null ? cameraTracker.CurrentBody : null;
        int idx = -1;

        if (tracked != null)
        {
            for (int i = 0; i < _optionsMap.Count; i++)
            {
                DropdownEntry entry = _optionsMap[i];

                if (entry.Kind == DropdownEntryKind.Body && entry.Body == tracked)
                {
                    idx = i;
                    break;
                }

                if (entry.Kind == DropdownEntryKind.ConstellationPlane &&
                    constellationRegistry != null &&
                    constellationRegistry.TryGetPlaneForBody(tracked, out var constellation, out var plane) &&
                    entry.Constellation == constellation &&
                    entry.Plane == plane)
                {
                    idx = i;
                    break;
                }
            }
        }

        if (idx >= 0)
            bodyDropdown.SetValueWithoutNotify(idx);
        else
            SetDropdownNoSelection();

        bodyDropdown.RefreshShownValue();
    }

    /// <summary>
    /// Clears the visual selection (no explicit placeholder option by default).
    /// </summary>
    private void SetDropdownNoSelection()
    {
        bodyDropdown.RefreshShownValue();
    }

    private void BindRegistryListener()
    {
        if (constellationRegistry == null || _registryListenerAdded)
            return;

        constellationRegistry.Changed += OnConstellationRegistryChanged;
        _registryListenerAdded = true;
    }

    private void UnbindRegistryListener()
    {
        if (constellationRegistry == null || !_registryListenerAdded)
            return;

        constellationRegistry.Changed -= OnConstellationRegistryChanged;
        _registryListenerAdded = false;
    }
}
