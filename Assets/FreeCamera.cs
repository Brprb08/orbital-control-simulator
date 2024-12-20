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
        transform.Translate(moveX, 0, moveZ);

        // Rotation (adjusted to ignore Time.timeScale)
        if (Input.GetMouseButton(1))
        {
            float rotationX = Input.GetAxis("Mouse X") * sensitivity * Time.unscaledDeltaTime;
            float rotationY = Input.GetAxis("Mouse Y") * sensitivity * Time.unscaledDeltaTime;
            transform.eulerAngles += new Vector3(-rotationY, rotationX, 0);
        }
    }

    public void TogglePlacementMode(bool enable)
    {
        isFreeMode = enable;
        Debug.Log($"FreeCam mode: {isFreeMode}");
    }
}