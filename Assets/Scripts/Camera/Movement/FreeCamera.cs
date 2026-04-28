using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Simple free-fly camera used when not tracking a target. Supports WASD + Space/Ctrl movement
/// and right-mouse drag look. Runs with unscaled time to work while paused.
/// </summary>
public class FreeCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    public float movementSpeed = 3000f;
    public float rotationSensitivity = 120f;

    [Header("Free Camera State")]
    private bool isFreeMode = false;

    [Header("Camera Rotation")]
    private float yaw = 0f;
    private float pitch = 0f;

    private SimContext ctx;

    /// <summary>Injects references from the simulation context.</summary>
    public void Initialize(SimContext ctx)
    {
        this.ctx = ctx;
    }

    void Update()
    {
        if (!isFreeMode) return;
        if (IsTypingInInputField()) return;

        HandleMovement();
        HandleRotation();
    }

    /// <summary>Returns true if a TMP input field is currently focused.</summary>
    private bool IsTypingInInputField()
    {
        var selected = EventSystem.current.currentSelectedGameObject;
        return selected != null && selected.GetComponent<TMPro.TMP_InputField>() != null;
    }

    /// <summary>Processes WASD/Space/Ctrl movement in local space using unscaled time.</summary>
    private void HandleMovement()
    {
        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) move += Vector3.back;
        if (Input.GetKey(KeyCode.A)) move += Vector3.left;
        if (Input.GetKey(KeyCode.D)) move += Vector3.right;
        if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl)) move += Vector3.down;

        if (move != Vector3.zero)
        {
            move.Normalize();
            transform.Translate(move * movementSpeed * Time.unscaledDeltaTime, Space.Self);
        }
    }

    /// <summary>Applies right-mouse drag look with clamped pitch.</summary>
    private void HandleRotation()
    {
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * rotationSensitivity * Time.unscaledDeltaTime;
            pitch -= Input.GetAxis("Mouse Y") * rotationSensitivity * Time.unscaledDeltaTime;

            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    /// <summary>
    /// Enables or disables free-camera mode. When enabling, seeds yaw/pitch from current rotation.
    /// </summary>
    public void TogglePlacementMode(bool enable)
    {
        isFreeMode = enable;

        if (isFreeMode)
        {
            Vector3 currentEuler = transform.rotation.eulerAngles;
            yaw = currentEuler.y;
            pitch = Mathf.Clamp(currentEuler.x, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
