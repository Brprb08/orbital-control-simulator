using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit-mode tests for CameraMovement: verifies safe target assignment,
/// Earth-cam toggling, and placeholder targeting distance/height calculations.
/// </summary>
public class CameraMovement_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

    /// <summary>
    /// Setting a real target body should clear any placeholder, bind fields,
    /// and leave the pivot rotation valid (non-NaN).
    /// </summary>
    [UnityTest]
    public IEnumerator SetTargetBody_configures_rig_safely()
    {
        rig = SimTestBootstrap.CreateBasic(0); // Earth only
        var body = SimTestBootstrap_CreateBody(rig.Root.transform, "Sat", 20f, 40f, "Untagged");
        rig.CamMove.SetTargetBody(body);
        yield return null;

        Assert.AreEqual(body, rig.CamMove.targetBody);
        Assert.IsNull(rig.CamMove.targetPlaceholder);

        var q = rig.CamMove.cameraPivotTransform.rotation;
        Assert.IsFalse(float.IsNaN(q.x + q.y + q.z + q.w));
    }

    /// <summary>
    /// Switching to Earth as the target should flip the Earth-cam flag and bind the temporary Earth reference.
    /// </summary>
    [UnityTest]
    public IEnumerator SetTargetEarth_toggles_flag_and_distance_valid()
    {
        rig = SimTestBootstrap.CreateBasic(1);
        var earth = rig.Earth;

        bool wasEarth = rig.CamMove.inEarthCam;
        rig.CamMove.SetTargetEarth(earth);
        yield return null;

        Assert.AreNotEqual(wasEarth, rig.CamMove.inEarthCam);
        Assert.AreEqual(earth, rig.CamMove.tempEarthBody);
    }

    /// <summary>
    /// Targeting a placeholder should compute camera distance from placeholder scale
    /// and set height accordingly, with no real body targeted.
    /// </summary>
    [UnityTest]
    public IEnumerator SetTargetBodyPlaceholder_sets_distance_from_scale()
    {
        rig = SimTestBootstrap.CreateBasic(0);
        var ph = new GameObject("PH").transform;
        ph.SetParent(rig.Root.transform, false);
        ph.localScale = new Vector3(3f, 3f, 3f);

        rig.CamMove.SetTargetBodyPlaceholder(ph);
        yield return null;

        Assert.IsNull(rig.CamMove.targetBody);
        Assert.AreEqual(ph, rig.CamMove.targetPlaceholder);
        Assert.AreEqual(30f, rig.CamMove.distance, 0.001f);
        Assert.AreEqual(0.6f, rig.CamMove.height, 0.001f);
    }

    /// <summary>
    /// Creates a minimal NBody for tests without involving BodyService.
    /// </summary>
    private static NBody SimTestBootstrap_CreateBody(Transform parent, string name, float radius, float camRadius, string tag)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (!string.IsNullOrEmpty(tag)) go.tag = tag;
        var nb = go.AddComponent<NBody>();
        nb.isCentralBody = false;
        nb.radius = radius;
        nb.cameraDistanceRadius = camRadius;
        return nb;
    }
}
