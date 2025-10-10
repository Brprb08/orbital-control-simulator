using UnityEngine;

/// <summary>
/// Quits the application when the Escape key is pressed (also stops play mode in the editor).
/// </summary>
public class QuitOnEscape : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
