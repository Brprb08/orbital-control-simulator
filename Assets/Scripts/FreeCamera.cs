using UnityEngine;
public class FreeCamera : MonoBehaviour
{
    public float speed = 10f; // Movement speed
    public float sensitivity = 100f; // Look sensitivity

    private bool isFreeMode = false;

    void Update()
    {
        if (!isFreeMode)
        {
            // Do nothing if not in FreeCam mode
            return;
        }

        // Movement (scales properly with time)
        float moveX = Input.GetAxis("Horizontal") * speed;
        float moveZ = Input.GetAxis("Vertical") * speed;
        transform.Translate(moveX, 0, moveZ, Space.Self);

        // Rotation (adjusted to rotate in place)
        if (Input.GetMouseButton(1))
        {
            float rotationX = Input.GetAxis("Mouse X") * sensitivity * Time.unscaledDeltaTime;
            float rotationY = Input.GetAxis("Mouse Y") * sensitivity * Time.unscaledDeltaTime;

            // Apply local rotation for in-place movement
            transform.Rotate(Vector3.up, rotationX, Space.Self);      // Horizontal rotation (yaw)
            transform.Rotate(Vector3.right, -rotationY, Space.Self);  // Vertical rotation (pitch)
        }
    }

    public void TogglePlacementMode(bool enable)
    {
        isFreeMode = enable;
        Debug.Log($"FreeCam mode: {isFreeMode}");
    }
}