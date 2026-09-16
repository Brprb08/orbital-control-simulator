using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class ConstellationTrajectoryOverlay : MonoBehaviour
{
    [Header("Option A Definition Overlay")]
    [SerializeField, Min(24)] private int samplesPerPlane = 192;
    [SerializeField, Min(1)] private int maxVisiblePlaneLines = 12;
    [SerializeField] private Color planeColor = new(0.16f, 0.72f, 1f, 1f);
    [SerializeField, Min(0.0001f)] private float lineWidth = 0.08f;

    private readonly List<ProceduralLineRenderer> planeLines = new();
    private readonly Dictionary<ConstellationPlaneRecord, Vector3[]> idealOrbitCache = new();

    private ConstellationRegistry registry;
    private CameraController cameraController;
    private TrajectoryRenderer trajectoryRenderer;
    private Camera mainCamera;
    private ConstellationRecord activeConstellation;
    private int activePlaneIndex = -1;
    private Transform lineRoot;
    private double mu = PhysicsConstants.EarthMuMeters;
    private double metersPerUnit = SimulationUnits.MetersPerUnit;
    private bool overlayDirty;
    private bool membershipDirty;
    private readonly List<int> occupiedPlaneIndices = new();
    private string renderedConstellationId;
    private int renderedActivePlaneIndex = -1;
    private int renderedPlaneCount = -1;
    private bool renderedVisible;
    private int coloredPlaneIndex = -2;
    private Color appliedTrackedColor;
    private Color appliedPlaneColor;

    public void Initialize(SimContext ctx)
    {
        registry = ctx.ConstellationRegistry;
        cameraController = ctx.CameraController;
        trajectoryRenderer = ctx.TrajectoryRenderer;
        mainCamera = Camera.main;

        if (ctx.ObjectPlacementManager != null)
        {
            mu = ctx.ObjectPlacementManager.Mu;
            metersPerUnit = ctx.ObjectPlacementManager.MetersPerUnit;
        }

        EnsureLineRoot();

        if (registry != null)
            registry.Changed += HandleConstellationsChanged;

        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged += HandleTrackedBodyChanged;
            cameraController.OnModeChanged += HandleCameraModeChanged;
        }

        RefreshActiveSelection();
        RebuildOverlay();
    }

    private void LateUpdate()
    {
        if (overlayDirty)
        {
            overlayDirty = false;
            RefreshActiveSelection();
            if (membershipDirty || HasOverlaySelectionChanged())
                RebuildOverlay();
            membershipDirty = false;
        }

        ApplyPlaneColors();

        // Zoom changes do not dirty the orbital geometry, but visibility must
        // follow the same close-up cutoff as a normal tracked satellite.
        bool visible = activeConstellation != null && trajectoryRenderer != null &&
                       CameraVisibilityPolicy.IsTrackedOrbitVisible(cameraController, mainCamera,
                           CameraVisibilityPolicy.SelectedBody(cameraController), trajectoryRenderer.lineDisableDistance);
        SetAllVisible(visible);
    }

    private void OnDestroy()
    {
        if (registry != null)
            registry.Changed -= HandleConstellationsChanged;

        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged -= HandleTrackedBodyChanged;
            cameraController.OnModeChanged -= HandleCameraModeChanged;
        }

        ClearLines();
    }

    private void HandleConstellationsChanged()
    {
        membershipDirty = true;
        MarkOverlayDirty();
    }

    private void HandleTrackedBodyChanged(NBody body)
    {
        MarkOverlayDirty();
    }

    private void HandleCameraModeChanged(CameraMode mode)
    {
        if (mode == CameraMode.Free)
        {
            // Match normal tracked trajectories: leaving the tracked-camera
            // view hides constellation paths immediately.
            SetAllVisible(false);
            renderedVisible = false;
        }

        MarkOverlayDirty();
    }

    private void MarkOverlayDirty()
    {
        overlayDirty = true;
    }

    private void RefreshActiveSelection()
    {
        activeConstellation = null;
        activePlaneIndex = -1;
        if (CameraVisibilityPolicy.TryGetSelectedConstellation(cameraController, registry, out var constellation, out var plane))
        {
            activeConstellation = constellation;
            activePlaneIndex = plane.PlaneIndex;
        }
    }

    private void RebuildOverlay()
    {
        if (activeConstellation == null)
        {
            SetAllVisible(false);
            renderedConstellationId = null;
            renderedActivePlaneIndex = -1;
            renderedPlaneCount = -1;
            renderedVisible = false;
            return;
        }

        EnsureLineRoot();
        EnsureLineCount(activeConstellation.Definition.Planes);
        renderedConstellationId = activeConstellation.Id;
        renderedActivePlaneIndex = activePlaneIndex;
        renderedPlaneCount = activeConstellation.Definition.Planes;
        renderedVisible = true;
        occupiedPlaneIndices.Clear();
        for (int i = 0; i < activeConstellation.Planes.Count; i++)
        {
            if (activeConstellation.Planes[i].RepresentativeBody != null)
                occupiedPlaneIndices.Add(i);
        }

        for (int plane = 0; plane < planeLines.Count; plane++)
        {
            ProceduralLineRenderer line = planeLines[plane];
            if (line == null)
                continue;

            if (plane >= activeConstellation.Definition.Planes)
            {
                line.Clear();
                line.SetVisibility(false);
                continue;
            }

            bool isActivePlane = plane == activePlaneIndex;
            if (!ShouldDrawPlane(plane, isActivePlane))
            {
                line.Clear();
                line.SetVisibility(false);
                continue;
            }

            line.SetLineWidth(lineWidth);
            line.UpdateLine(GetIdealOrbit(activeConstellation, plane), smoothClosedLoop: true);
            line.SetVisibility(true);
        }

        ApplyPlaneColors(force: true);
    }

    private void ApplyPlaneColors(bool force = false)
    {
        if (activeConstellation == null)
            return;

        NBody selected = CameraVisibilityPolicy.SelectedBody(cameraController);
        int highlightedPlane = selected != null && registry != null && !registry.HasDepartedIdeal(selected)
            ? activePlaneIndex
            : -1;
        Color trackedColor = trajectoryRenderer != null
            ? trajectoryRenderer.predictionColor
            : new Color32(0x29, 0x78, 0xFF, 255);

        if (!force && coloredPlaneIndex == highlightedPlane &&
            appliedTrackedColor == trackedColor && appliedPlaneColor == planeColor)
            return;

        coloredPlaneIndex = highlightedPlane;
        appliedTrackedColor = trackedColor;
        appliedPlaneColor = planeColor;
        string trackedHex = "#" + ColorUtility.ToHtmlStringRGB(trackedColor);
        string planeHex = "#" + ColorUtility.ToHtmlStringRGB(planeColor);
        for (int i = 0; i < planeLines.Count; i++)
        {
            if (planeLines[i] != null)
            {
                planeLines[i].SetLineColor(i == highlightedPlane ? trackedHex : planeHex);

                // Transparent rings share nearly the same bounds center, so
                // distance sorting can swap their blend order as the camera moves.
                // Draw ideal planes in a fixed order, with the highlighted plane
                // last. Keep live predictions and maneuvers (order 0) above them.
                planeLines[i].GetComponent<MeshRenderer>().sortingOrder =
                    i == highlightedPlane ? -1 : -2 - i;
            }
        }
    }

    private bool HasOverlaySelectionChanged()
    {
        if (activeConstellation == null)
            return renderedVisible || renderedConstellationId != null || renderedActivePlaneIndex != -1 || renderedPlaneCount != -1;

        return !renderedVisible ||
               renderedConstellationId != activeConstellation.Id ||
               renderedActivePlaneIndex != activePlaneIndex ||
               renderedPlaneCount != activeConstellation.Definition.Planes;
    }

    private bool ShouldDrawPlane(int planeIndex, bool isActivePlane)
    {
        int occupiedIndex = occupiedPlaneIndices.IndexOf(planeIndex);
        if (occupiedIndex < 0)
            return false;
        if (isActivePlane)
            return true;

        int maxLines = Mathf.Max(1, maxVisiblePlaneLines);
        int planeCount = occupiedPlaneIndices.Count;
        if (planeCount <= maxLines)
            return true;

        // Empty planes do not consume the display budget or affect spacing.
        int selectedIndex = occupiedPlaneIndices.IndexOf(activePlaneIndex);
        int reserved = selectedIndex >= 0 ? 1 : 0;
        int unselectedBudget = maxLines - reserved;
        if (unselectedBudget <= 0)
            return false;

        int unselectedCount = planeCount - reserved;
        int rank = selectedIndex >= 0 && occupiedIndex > selectedIndex ? occupiedIndex - 1 : occupiedIndex;
        int previousBucket = rank > 0
            ? Mathf.FloorToInt((rank - 1) * unselectedBudget / (float)unselectedCount)
            : -1;
        int currentBucket = Mathf.FloorToInt(rank * unselectedBudget / (float)unselectedCount);
        return currentBucket != previousBucket;
    }

    private Vector3[] GetIdealOrbit(ConstellationRecord constellation, int planeIndex)
    {
        ConstellationPlaneRecord plane = constellation.GetPlane(planeIndex);
        if (plane == null)
            return BuildPlaneOrbit(constellation.Definition, planeIndex);

        if (!idealOrbitCache.TryGetValue(plane, out var points) || points == null || points.Length < 2)
        {
            points = BuildPlaneOrbit(constellation.Definition, planeIndex);
            idealOrbitCache[plane] = points;
        }

        return points;
    }

    private Vector3[] BuildPlaneOrbit(ConstellationDefinition definition, int planeIndex)
    {
        int sampleCount = Mathf.Max(24, samplesPerPlane);
        var points = new Vector3[sampleCount + 1];

        double raanDeg = (definition.RaanStartDeg % 360.0) +
                         (definition.RaanSpreadDeg / definition.Planes) * planeIndex;

        for (int i = 0; i <= sampleCount; i++)
        {
            double trueAnomalyDeg = 360.0 * i / sampleCount;
            var (rEci, _) = KeplerUtils.FromElements(
                definition.SemiMajorAxisMeters,
                definition.Eccentricity,
                definition.InclinationDeg,
                raanDeg,
                definition.ArgumentOfPerigeeDeg,
                trueAnomalyDeg,
                mu
            );

            points[i] = FrameUtils.EciToUnity(rEci, metersPerUnit);
        }

        return points;
    }

    private void EnsureLineRoot()
    {
        if (lineRoot != null)
            return;

        var root = new GameObject("ConstellationTrajectoryOverlay");
        root.layer = gameObject.layer;
        root.transform.SetParent(transform, false);
        lineRoot = root.transform;
    }

    private void EnsureLineCount(int count)
    {
        while (planeLines.Count < count)
        {
            var go = new GameObject($"ConstellationPlaneLine_{planeLines.Count + 1}");
            go.layer = gameObject.layer;
            go.transform.SetParent(lineRoot, false);

            var line = go.AddComponent<ProceduralLineRenderer>();
            line.SetLineWidth(lineWidth);
            planeLines.Add(line);
        }
    }

    private void SetAllVisible(bool visible)
    {
        for (int i = 0; i < planeLines.Count; i++)
        {
            ProceduralLineRenderer line = planeLines[i];
            if (line != null)
                line.SetVisibility(visible && line.HasPoints &&
                    activeConstellation?.GetPlane(i)?.RepresentativeBody != null);
        }
    }

    private void ClearLines()
    {
        if (lineRoot == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(lineRoot.gameObject);
        else
            Destroy(lineRoot.gameObject);
#else
        Destroy(lineRoot.gameObject);
#endif

        lineRoot = null;
        planeLines.Clear();
    }
}
