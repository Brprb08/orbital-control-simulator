using UnityEngine;
using UnityEngine.EventSystems;

public class FreeCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    public float movementSpeed = 1000f;
    public float rotationSensitivity = 120f;

    private bool isFreeMode = false;

    private float yaw = 0f;
    private float pitch = 0f;

    void Update()
    {
        if (!isFreeMode)
            return;

        if (IsTypingInInputField())
            return;

        HandleMovement();
        HandleRotation();
    }

    private bool IsTypingInInputField()
    {
        var selected = EventSystem.current.currentSelectedGameObject;
        return selected != null && selected.GetComponent<TMPro.TMP_InputField>() != null;
    }

    private void HandleMovement()
    {
        Vector3 move = Vector3.zero;

        // Keyboard movement only – ignore analog input completely
        if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) move += Vector3.back;
        if (Input.GetKey(KeyCode.A)) move += Vector3.left;
        if (Input.GetKey(KeyCode.D)) move += Vector3.right;

        // Optional: Vertical movement (fly mode)
        if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;

        if (move != Vector3.zero)
        {
            move.Normalize(); // consistent speed in all directions
            transform.Translate(move * movementSpeed * Time.unscaledDeltaTime, Space.Self);
        }
    }

    private void HandleRotation()
    {
        if (Input.GetMouseButton(1)) // Right mouse button
        {
            yaw += Input.GetAxis("Mouse X") * rotationSensitivity * Time.unscaledDeltaTime;
            pitch -= Input.GetAxis("Mouse Y") * rotationSensitivity * Time.unscaledDeltaTime;

            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    public void TogglePlacementMode(bool enable)
    {
        isFreeMode = enable;

        if (enable)
        {
            Vector3 currentEuler = transform.rotation.eulerAngles;
            yaw = currentEuler.y;
            pitch = Mathf.Clamp(currentEuler.x, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
