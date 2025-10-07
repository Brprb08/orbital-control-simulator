using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CameraMovement_EditModeTests
{
    private SimTestRig rig;

    [TearDown]
    public void TearDown() => rig?.Dispose();

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

    // small helper to make a body without touching BodyService
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
