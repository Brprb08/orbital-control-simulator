using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Provides free movement and rotation control for the camera.
/// Allows toggling between free camera mode and locked tracking modes.
/// </summary>
public class FreeCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 400f;
    public float sensitivity = 120f;

    private bool isFreeMode = false;

    private float yaw = 0f;
    private float pitch = 0f;

    /// <summary>
    /// Handles free camera movement and rotation based on user input.
    /// </summary>
    void Update()
    {
        if (!isFreeMode)
        {
            return;
        }

        if (EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
        {
            return; // Don't allow WASD movement or camera control while typing.
        }

        // Movement input (WASD or arrow keys).
        float rawX = Input.GetAxisRaw("Horizontal");
        float rawZ = Input.GetAxisRaw("Vertical");

        // Deadzone to eliminate drift
        rawX = Mathf.Abs(rawX) > 0.1f ? rawX : 0f;
        rawZ = Mathf.Abs(rawZ) > 0.1f ? rawZ : 0f;

        float moveX = rawX * speed * Time.unscaledDeltaTime;
        float moveZ = rawZ * speed * Time.unscaledDeltaTime;

        if (moveX != 0f || moveZ != 0f)
        {
            transform.Translate(moveX, 0, moveZ, Space.Self);
        }

        // Rotation input (hold right mouse button to rotate).
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * sensitivity * Time.unscaledDeltaTime;
            pitch -= Input.GetAxis("Mouse Y") * sensitivity * Time.unscaledDeltaTime;
            pitch = Mathf.Clamp(pitch, -89f, 89f); // Prevent flipping over

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    /// <summary>
    /// Toggles free camera mode on or off.
    /// </summary>
    /// <param name="enable">True to enable FreeCam mode, false to disable.</param>
    public void TogglePlacementMode(bool enable)
    {
        isFreeMode = enable;

        if (enable)
        {
            Vector3 currentEuler = transform.rotation.eulerAngles;
            yaw = currentEuler.y;
            pitch = currentEuler.x;
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f); // Normalize it
        }
    }
}
