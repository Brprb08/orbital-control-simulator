using System.Linq;
using System.Collections.Generic;

/// <summary>Cycles through the original members of the selected constellation plane.</summary>
public sealed class ConstellationNavigationController
{
    private readonly SimContext ctx;

    public ConstellationNavigationController(SimContext ctx)
    {
        this.ctx = ctx;
    }

    public void TrackNextSatellite() => TrackAdjacentSatellite(1);
    public void TrackPreviousSatellite() => TrackAdjacentSatellite(-1);

    /// <summary>
    /// Prefers the original plane, then the original constellation, then a regular
    /// satellite. Pending removals are excluded even before Unity destroys them.
    /// Ordinary satellites retain the existing first-survivor selection behavior.
    /// </summary>
    public NBody FindRemovalReplacement(NBody removed, ISet<NBody> pendingRemovals)
    {
        if (removed == null || ctx?.BodyService == null)
            return null;

        ConstellationRegistry registry = ctx.ConstellationRegistry;
        ConstellationRecord originalConstellation = null;
        ConstellationPlaneRecord originalPlane = null;
        bool isConstellation = registry != null &&
            registry.TryGetPlaneForBody(removed, out originalConstellation, out originalPlane);

        NBody replacement = null;
        int bestRank = int.MaxValue;
        foreach (NBody candidate in ctx.BodyService.Bodies)
        {
            if (candidate == null || candidate == removed || !candidate.CompareTag("Satellite") ||
                (pendingRemovals != null && pendingRemovals.Contains(candidate)))
                continue;

            if (!isConstellation)
                return candidate;

            int rank;
            if (registry.TryGetPlaneForBody(candidate, out var constellation, out var plane))
            {
                if (constellation != originalConstellation)
                    continue;
                rank = plane == originalPlane ? 0 : 1;
            }
            else
            {
                rank = 2;
            }

            if (rank < bestRank)
            {
                replacement = candidate;
                bestRank = rank;
                if (rank == 0)
                    break;
            }
        }

        return replacement;
    }

    private void TrackAdjacentSatellite(int direction)
    {
        if (ctx?.CameraController == null || ctx.BodyService == null)
            return;

        // Match the existing node-burn navigation lock. Also prevent held manual
        // thrust from transferring to a different craft when the selection changes.
        if (ctx.ThrustController != null && ctx.ThrustController.IsThrusting)
            return;

        NBody selected = CameraVisibilityPolicy.SelectedBody(ctx.CameraTracker);
        if (selected == null || ctx.ConstellationRegistry == null ||
            !ctx.ConstellationRegistry.TryGetPlaneForBody(selected, out _, out var plane))
            return;

        // Registry order is the original spawn order; maneuvering never changes it.
        var members = plane.Members;
        int currentIndex = -1;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] == selected)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
            return;

        for (int offset = 1; offset < members.Count; offset++)
        {
            int index = (currentIndex + direction * offset + members.Count) % members.Count;
            NBody candidate = members[index];
            // Deregistration can precede Unity's deferred destruction.
            if (candidate == null || !ctx.BodyService.Bodies.Contains(candidate))
                continue;

            ctx.CameraController.TrackBodyPreservingCameraMode(candidate);
            if (ctx.TutorialController != null && ctx.TutorialController.inTutorialMode)
                ctx.TutorialController.hasSwitchedSatellites = true;
            return;
        }
    }
}
