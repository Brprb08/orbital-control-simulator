using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AmbientManager : MonoBehaviour
{
    public List<AudioClip> ambientClips;
    public float fadeDuration = 2f;

    private AudioSource audioSource;
    private int currentClipIndex = 0;

    [Range(0f, 1f)]
    public float masterVolume = 1f;

    void Start()
    {
        if (ambientClips == null || ambientClips.Count == 0)
        {
            Debug.LogWarning("AmbientManager: No clips assigned.");
            return;
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.loop = false;
        audioSource.playOnAwake = false;
        audioSource.volume = 0f;
        audioSource.ignoreListenerPause = true;

        // Shuffle the clip order
        ShuffleClips();

        StartCoroutine(PlayAmbientLoop());
    }

    private IEnumerator PlayAmbientLoop()
    {
        while (true)
        {
            AudioClip clip = ambientClips[currentClipIndex];
            audioSource.clip = clip;
            audioSource.Play();

            // Fade in
            yield return StartCoroutine(FadeVolume(0f, 1f, fadeDuration));

            // Wait until just before the clip ends
            yield return new WaitForSecondsRealtime(clip.length - fadeDuration);

            // Fade out
            yield return StartCoroutine(FadeVolume(1f, 0f, fadeDuration));

            currentClipIndex = (currentClipIndex + 1) % ambientClips.Count;
        }
    }

    private IEnumerator FadeVolume(float start, float end, float duration)
    {
        float time = 0f;
        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            audioSource.volume = Mathf.Lerp(start, end, time / duration) * masterVolume;
            yield return null;
        }
        audioSource.volume = end * masterVolume;
    }

    private void ShuffleClips()
    {
        for (int i = 0; i < ambientClips.Count; i++)
        {
            int rand = Random.Range(i, ambientClips.Count);
            (ambientClips[i], ambientClips[rand]) = (ambientClips[rand], ambientClips[i]);
        }
    }
}
