using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Provides interop with a native physics plugin using DLL import.
/// Handles loading of native DLL and exposes numerical integrators for orbital simulation.
/// </summary>
public static class NativePhysics
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    /// <summary>
    /// Static constructor for NativePhysics.
    /// Attempts to load the native PhysicsPlugin DLL at runtime and logs success/failure.
    /// </summary>
    static NativePhysics()
    {
        string unityPluginsPath = Path.Combine(Application.dataPath, "Plugins/x86_64/PhysicsPlugin.dll");

        // PATH: {unityPluginsPath} for debug below
        Debug.Log($"[NATIVE PHYSICS]: Checking for DLL");

        if (File.Exists(unityPluginsPath))
        {
            Debug.Log("[NATIVE PHYSICS]: DLL exists at expected path!");
        }
        else
        {
            Debug.LogError("[NATIVE PHYSICS]: DLL NOT FOUND! Check file path.");
        }

        IntPtr handle = LoadLibrary(unityPluginsPath);
        if (handle == IntPtr.Zero)
        {
            Debug.LogError($"[NATIVE PHYSICS]: DLL load failed! Error Code: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            Debug.Log("[NATIVE PHYSICS]: DLL loaded successfully");
        }
    }

    /// <summary>
    /// Calls a native C++ function to integrate the motion of a body using the Dormand-Prince (Runge-Kutta) method.
    /// </summary>
    /// <param name="position">Reference to the current position (double precision).</param>
    /// <param name="velocity">Reference to the current velocity (double precision).</param>
    /// <param name="mass">Mass of the target body.</param>
    /// <param name="bodies">Array of positions of all other bodies (single precision).</param>
    /// <param name="masses">Array of masses of the other bodies (double precision).</param>
    /// <param name="numBodies">Number of other bodies.</param>
    /// <param name="deltaTime">Simulation timestep in seconds.</param>
    /// <param name="thrustImpulse">Impulse force (e.g., from propulsion).</param>
    /// <param name="dragCoeff">Drag coefficient for atmospheric resistance.</param>
    /// <param name="areaUU">Cross-sectional area used for drag calculations.</param>
    [DllImport("PhysicsPlugin", EntryPoint = "DormandPrinceSingle", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DormandPrinceSingle(
        ref double3 position,
        ref double3 velocity,
        double mass,
        Vector3[] bodies,
        double[] masses,
        int numBodies,
        float deltaTime,
        Vector3 thrustImpulse,
        float dragCoeff,
        float areaUU
    );
}
