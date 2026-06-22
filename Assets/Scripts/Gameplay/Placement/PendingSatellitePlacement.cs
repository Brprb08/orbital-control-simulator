using System;
using UnityEngine;

/// <summary>
/// Tracks the manual-placement limbo state: a placeholder satellite exists,
/// but velocity has not been confirmed yet. It deliberately does not own UI,
/// camera behavior, spawning, or validation.
/// </summary>
[Serializable]
public class PendingSatellitePlacement
{
    [SerializeField] private GameObject satellite;

    public GameObject Satellite => satellite;
    public bool HasSatellite => satellite != null;
    public string SatelliteName => satellite != null ? satellite.name : string.Empty;

    public void Set(GameObject satellite)
    {
        this.satellite = satellite;
    }

    public void Clear()
    {
        satellite = null;
    }

    public void DestroyAndClear()
    {
        if (satellite == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEngine.Object.DestroyImmediate(satellite);
        else
            UnityEngine.Object.Destroy(satellite);
#else
        UnityEngine.Object.Destroy(satellite);
#endif

        satellite = null;
    }
}
