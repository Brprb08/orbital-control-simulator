using UnityEngine;
using UnityEngine.EventSystems;

/**
* FreeCamera provides free movement and rotation control for the camera.
* Allows toggling between free camera mode and locked tracking modes.
**/
public class FreeCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 400f;
    public float sensitivity = 120f;

    private bool isFreeMode = false;

    /**
    * Handles free camera movement and rotation based on user input.
    **/
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
        float moveX = Input.GetAxis("Horizontal") * speed;
        float moveZ = Input.GetAxis("Vertical") * speed;
        transform.Translate(moveX, 0, moveZ, Space.Self);

        // Rotation input (hold right mouse button to rotate).
        if (Input.GetMouseButton(1))
        {
            float rotationX = Input.GetAxis("Mouse X") * sensitivity * Time.unscaledDeltaTime;
            float rotationY = Input.GetAxis("Mouse Y") * sensitivity * Time.unscaledDeltaTime;

            transform.Rotate(Vector3.up, rotationX, Space.Self); // Horizontal (yaw).
            transform.Rotate(Vector3.right, -rotationY, Space.Self); // Vertical (pitch).
        }
    }

    /**
    * Toggles free camera mode on or off.
    * @param enable - True to enable FreeCam mode, false to disable.
    **/
    public void TogglePlacementMode(bool enable)
    {
        isFreeMode = enable;
    }
}
