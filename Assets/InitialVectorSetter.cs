using UnityEngine;

public class InitialVelocitySetter : MonoBehaviour
{
    public NBody centralBody; // Assign the central body in the Inspector

    void Start()
    {
        if (GravityManager.Instance == null)
        {
            Debug.LogError("GravityManager instance not found in the scene.");
            return;
        }

        foreach (NBody body in GravityManager.Instance.Bodies)
        {
            if (body != centralBody)
            {
                Vector3 direction = body.transform.position - centralBody.transform.position;
                float distance = direction.magnitude;
                float speed = Mathf.Sqrt(PhysicsConstants.G * centralBody.mass / distance);
                // Set velocity perpendicular to the direction (assuming Y-up)
                body.velocity = Vector3.Cross(direction.normalized, Vector3.up) * speed;
            }
        }
    }
}