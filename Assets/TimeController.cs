using UnityEngine;

public class TimeController : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject); // This object will persist between scene reloads
    }

    void Start()
    {
        // Reapply the time scale when the scene starts
        Time.timeScale = 1.0f; // Set to high speed
        Time.fixedDeltaTime = 0.2f; // Run physics every 0.2 in-game seconds
        Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F))
        {
            Time.timeScale = 100.0f;
            Time.fixedDeltaTime = 0.2f;
            Debug.Log($"Time scale set to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Time.timeScale = 1.0f;
            Time.fixedDeltaTime = 0.02f;
            Debug.Log($"Time scale reset to {Time.timeScale}, fixedDeltaTime = {Time.fixedDeltaTime}");
        }
    }
}