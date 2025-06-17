using UnityEngine;

public class UIButtonSound : MonoBehaviour
{
    public AudioClip clickSound;         // Assign this in the Inspector
    private AudioSource audioSource;     // Audio source that plays the sound
    public float volume = .1f;

    void Awake()
    {
        // Add an AudioSource component if one doesn't already exist
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.volume = volume;
    }

    public void PlayClickSound()
    {
        if (clickSound != null)
            audioSource.PlayOneShot(clickSound);
    }
}