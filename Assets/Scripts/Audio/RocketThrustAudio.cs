using UnityEngine;
using System.Collections;

public class RocketThrustAudio : MonoBehaviour
{
    public AudioClip thrustSound;
    public float maxVolume = 1.0f;
    public float fadeDuration = 1.0f;

    private AudioSource thrustSource;
    private Coroutine fadeCoroutine;

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

    public void StartThrust()
    {
        if (thrustSound == null) return;

        if (!thrustSource.isPlaying)
        {
            thrustSource.Play();
        }

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadeIn());
    }

    public void StopThrust()
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadeOut());
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
