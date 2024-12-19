using UnityEngine;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    public CameraMovement cameraMovement; // Assign in Inspector
    private List<NBody> bodies;
    private int currentIndex = 0;

    void Start()
    {
        bodies = GravityManager.Instance.Bodies.FindAll(body => body.CompareTag("Planet"));
        if (bodies.Count > 0 && cameraMovement != null)
        {
            // Initially set the first body as target
            cameraMovement.SetTargetBody(bodies[currentIndex]);
        }
    }

    void Update()
    {
        // Press Tab to cycle through bodies
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            currentIndex = (currentIndex + 1) % bodies.Count;
            cameraMovement.SetTargetBody(bodies[currentIndex]);
            Debug.Log($"Camera now tracking: {bodies[currentIndex].name}");
        }
    }
}