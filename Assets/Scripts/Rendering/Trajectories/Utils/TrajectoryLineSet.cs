using UnityEngine;

public sealed class TrajectoryLineSet
{
    private readonly Vector3[] originLinePoints = new Vector3[2];
    private readonly Vector3[] apogeeLinePoints = new Vector3[2];
    private readonly Vector3[] perigeeLinePoints = new Vector3[2];
    private readonly Vector3[] previewApogeeLinePoints = new Vector3[2];
    private readonly Vector3[] previewPerigeeLinePoints = new Vector3[2];

    public Transform Root { get; private set; }

    public ProceduralLineRenderer Prediction { get; private set; }
    public ProceduralLineRenderer Origin { get; private set; }
    public ProceduralLineRenderer Apogee { get; private set; }
    public ProceduralLineRenderer Perigee { get; private set; }
    public ProceduralLineRenderer PreManeuver { get; private set; }
    public ProceduralLineRenderer Preview { get; private set; }
    public ProceduralLineRenderer PreviewApogee { get; private set; }
    public ProceduralLineRenderer PreviewPerigee { get; private set; }
    public ProceduralLineRenderer PlannedManeuver { get; private set; }
    public ProceduralLineRenderer Burn { get; private set; }

    private TrajectoryLineSet()
    {
    }

    public static TrajectoryLineSet Create(
        Transform parent,
        int layer,
        Color predictionColor,
        Color originColor,
        Color apogeeColor,
        Color perigeeColor,
        string preManeuverHex,
        string previewHex,
        Color burnColor)
    {
        var set = new TrajectoryLineSet();

        GameObject root = new GameObject("TrajectoryLines");
        root.layer = layer;
        root.transform.SetParent(parent, false);

        set.Root = root.transform;
        set.Prediction = CreateLine("PredictionLine", predictionColor, set.Root, layer);
        set.Origin = CreateLine("OriginLine", originColor, set.Root, layer);
        set.Apogee = CreateLine("ApogeeLine", apogeeColor, set.Root, layer);
        set.Perigee = CreateLine("PerigeeLine", perigeeColor, set.Root, layer);
        set.PreManeuver = CreateLine("PreManeuverLine", preManeuverHex, set.Root, layer);
        set.Preview = CreateLine("PreviewLine", previewHex, set.Root, layer);
        set.PreviewApogee = CreateLine("PreviewApogeeLine", apogeeColor, set.Root, layer);
        set.PreviewPerigee = CreateLine("PreviewPerigeeLine", perigeeColor, set.Root, layer);
        set.PlannedManeuver = CreateLine("PlannedManeuverLine", previewHex, set.Root, layer);
        set.Burn = CreateLine("BurnLine", burnColor, set.Root, layer);

        return set;
    }

    public void Dispose()
    {
        if (Root == null)
            return;

        GameObject rootObject = Root.gameObject;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            Object.DestroyImmediate(rootObject);
        else
            Object.Destroy(rootObject);
#else
        Object.Destroy(rootObject);
#endif

        Root = null;
        Prediction = null;
        Origin = null;
        Apogee = null;
        Perigee = null;
        PreManeuver = null;
        Preview = null;
        PreviewApogee = null;
        PreviewPerigee = null;
        PlannedManeuver = null;
        Burn = null;
    }

    public void ClearAll()
    {
        Prediction?.Clear();
        Origin?.Clear();
        Apogee?.Clear();
        Perigee?.Clear();
        PreManeuver?.Clear();
        Preview?.Clear();
        PreviewApogee?.Clear();
        PreviewPerigee?.Clear();
        PlannedManeuver?.Clear();
        Burn?.Clear();
    }

    public void ClearApsides()
    {
        Apogee?.Clear();
        Perigee?.Clear();
    }

    public void ClearPreviewApsides()
    {
        PreviewApogee?.Clear();
        PreviewPerigee?.Clear();
    }

    public void DrawOrigin(Vector3 from, Vector3 to)
    {
        if (Origin == null)
            return;

        originLinePoints[0] = from;
        originLinePoints[1] = to;
        Origin.UpdateLine(originLinePoints);
    }

    public void DrawApsides(Vector3 apogee, Vector3 perigee, Vector3 center, bool show)
    {
        if (Apogee == null || Perigee == null)
            return;

        if (!show)
        {
            ClearApsides();
            return;
        }

        apogeeLinePoints[0] = apogee;
        apogeeLinePoints[1] = center;
        perigeeLinePoints[0] = perigee;
        perigeeLinePoints[1] = center;

        Apogee.UpdateLine(apogeeLinePoints);
        Perigee.UpdateLine(perigeeLinePoints);
    }

    public void DrawPreviewApsides(
        Vector3 apogee,
        bool showApogee,
        Vector3 perigee,
        bool showPerigee,
        Vector3 center)
    {
        if (PreviewApogee == null || PreviewPerigee == null)
            return;

        if (showApogee)
        {
            previewApogeeLinePoints[0] = apogee;
            previewApogeeLinePoints[1] = center;
            PreviewApogee.UpdateLine(previewApogeeLinePoints);
        }
        else
        {
            PreviewApogee.Clear();
        }

        if (showPerigee)
        {
            previewPerigeeLinePoints[0] = perigee;
            previewPerigeeLinePoints[1] = center;
            PreviewPerigee.UpdateLine(previewPerigeeLinePoints);
        }
        else
        {
            PreviewPerigee.Clear();
        }
    }

    public void SetAllVisible(bool visible)
    {
        Prediction?.SetVisibility(visible);
        Origin?.SetVisibility(visible);
        Apogee?.SetVisibility(visible);
        Perigee?.SetVisibility(visible);
        PreManeuver?.SetVisibility(visible);
        Preview?.SetVisibility(visible);
        PreviewApogee?.SetVisibility(visible);
        PreviewPerigee?.SetVisibility(visible);
        PlannedManeuver?.SetVisibility(visible);
        Burn?.SetVisibility(visible);
    }

    public void ApplyEffectiveVisibility(
        bool runtimeVisible,
        bool maneuverRuntimeVisible,
        bool showPrediction,
        bool showOrigin,
        bool showApogeePerigee)
    {
        Prediction?.SetVisibility(runtimeVisible && showPrediction);
        Origin?.SetVisibility(runtimeVisible && showOrigin);
        Apogee?.SetVisibility(runtimeVisible && showApogeePerigee);
        Perigee?.SetVisibility(runtimeVisible && showApogeePerigee);

        PreManeuver?.SetVisibility(runtimeVisible);
        Preview?.SetVisibility(maneuverRuntimeVisible);
        PreviewApogee?.SetVisibility(maneuverRuntimeVisible);
        PreviewPerigee?.SetVisibility(maneuverRuntimeVisible);
        PlannedManeuver?.SetVisibility(maneuverRuntimeVisible);
        Burn?.SetVisibility(runtimeVisible);
    }

    public void SetLineVisibility(bool showPrediction, bool showOrigin, bool showApogeePerigee)
    {
        Prediction?.SetVisibility(showPrediction);
        Origin?.SetVisibility(showOrigin);
        Apogee?.SetVisibility(showApogeePerigee);
        Perigee?.SetVisibility(showApogeePerigee);
    }

    public bool CopyPreviewToPlannedManeuver()
    {
        if (Preview == null || PlannedManeuver == null)
            return false;

        Vector3[] points = Preview.GetWorldPointsCopy();
        if (points.Length < 2)
            return false;

        PlannedManeuver.UpdateLine(points);
        return true;
    }

    private static ProceduralLineRenderer CreateLine(string name, Color color, Transform parent, int layer)
    {
        GameObject go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);

        ProceduralLineRenderer line = go.AddComponent<ProceduralLineRenderer>();
        string hex = ColorUtility.ToHtmlStringRGB(color);

        line.SetLineColor("#" + hex);
        line.SetLineWidth(0.1f);

        return line;
    }

    private static ProceduralLineRenderer CreateLine(string name, string hexColor, Transform parent, int layer)
    {
        if (!ColorUtility.TryParseHtmlString(hexColor, out Color color))
            color = Color.white;

        return CreateLine(name, color, parent, layer);
    }
}
