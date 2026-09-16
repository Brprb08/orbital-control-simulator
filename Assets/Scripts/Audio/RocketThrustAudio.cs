using UnityEngine;
using System.Collections;

public class RocketThrustAudio : MonoBehaviour
{
    public AudioClip thrustSound;
    public float maxVolume = 1.0f;
    public float fadeDuration = 1.0f;

    private AudioSource thrustSource;
    private Coroutine fadeCoroutine;
    private bool thrustActive;

    private SimContext ctx;

    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
    }

    void Awake()
    {
        thrustSource = gameObject.AddComponent<AudioSource>();
        thrustSource.playOnAwake = false;
        thrustSource.loop = true;
        thrustSource.volume = 0f;

        if (thrustSound != null)
        {
            thrustSource.clip = thrustSound;
        }
    }

    // Retained for scene button callbacks. The burn owner decides whether the input was accepted.
    public void StartThrust() => SyncBurnState();

    public void StopThrust() => SyncBurnState();

    private void SyncBurnState()
    {
        SetThrustActive(ctx?.ThrustController != null && ctx.ThrustController.IsThrusting);
    }

    public void SetThrustActive(bool active)
    {
        if (thrustActive == active || thrustSource == null) return;
        thrustActive = active;
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        if (active)
        {
            if (thrustSound == null) return;
            if (!thrustSource.isPlaying)
                thrustSource.Play();
            fadeCoroutine = StartCoroutine(FadeIn());
        }
        else
        {
            fadeCoroutine = StartCoroutine(FadeOut());
        }
    }

    private IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            thrustSource.volume = Mathf.Lerp(0f, maxVolume, t / fadeDuration);
            yield return null;
        }
        thrustSource.volume = maxVolume;
    }

    private IEnumerator FadeOut()
    {
        float startVolume = thrustSource.volume;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            thrustSource.volume = Mathf.Lerp(startVolume, 0f, t / fadeDuration);
            yield return null;
        }
        thrustSource.volume = 0f;
        thrustSource.Stop();
    }
}
