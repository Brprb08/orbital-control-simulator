using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

public static class NativePhysics
{
    /// <summary>Shared live/preview finite-burn integration. Previews supply copied fuel buffers.
    /// Only finite entries consume fuel; returned delta-v is in world units/s.</summary>
    [DllImport("PhysicsPlugin", EntryPoint = "BatchTwoBodyIntegrateMuExWithFuel", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BatchTwoBodyIntegrateMuExWithFuel(
        [In, Out] double3[] positions, [In, Out] double3[] velocities,
        [In] double[] masses, [In] Vector3[] thrusts,
        [In] float[] dragCoeffs, [In] float[] areasUU,
        [In] sbyte[] normalSign, [In] byte[] isThrusting, [In, Out] sbyte[] latchedParityIO,
        int count, double muUnity, float totalDt, int substeps, [Out] double[] deltaVOut,
        [In] double[] dryMasses, [In, Out] double[] fuelMasses,
        [In] double[] ispSeconds, [In] byte[] finiteFuel);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    static NativePhysics()
    {
        string unityPluginsPath = Path.Combine(Application.dataPath, "Plugins/x86_64/PhysicsPlugin.dll");
        Debug.Log($"[NATIVE PHYSICS]: Checking for DLL");
        if (File.Exists(unityPluginsPath)) Debug.Log("[NATIVE PHYSICS]: DLL exists at expected path!");
        else Debug.LogError("[NATIVE PHYSICS]: DLL NOT FOUND! Check file path.");

        IntPtr handle = LoadLibrary(unityPluginsPath);
        if (handle == IntPtr.Zero)
            Debug.LogError($"[NATIVE PHYSICS]: DLL load failed! Error Code: {Marshal.GetLastWin32Error()}");
        else
            Debug.Log("[NATIVE PHYSICS]: DLL loaded successfully");
    }

    // NativePhysics.cs
    [DllImport("PhysicsPlugin", EntryPoint = "BatchTwoBodyIntegrateMuEx", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BatchTwoBodyIntegrateMuEx(
    [In, Out] Unity.Mathematics.double3[] positions,
    [In, Out] Unity.Mathematics.double3[] velocities,
    [In] double[] masses,
    [In] UnityEngine.Vector3[] thrusts,
    [In] float[] dragCoeffs,
    [In] float[] areasUU,
    [In] sbyte[] normalSign,      // 0 free, +1 Normal, -1 AntiNormal
    [In] byte[] isThrusting,
    [In, Out] sbyte[] latchedParityIO,
    int count,
    double muUnity,
    float totalDt,
    int substeps
);

    /// <summary>Returns applied thrust delta-v in world units/s, excluding gravity and drag.</summary>
    [DllImport("PhysicsPlugin", EntryPoint = "BatchTwoBodyIntegrateMuExWithDeltaV", CallingConvention = CallingConvention.Cdecl)]
    public static extern void BatchTwoBodyIntegrateMuExWithDeltaV(
        [In, Out] double3[] positions,
        [In, Out] double3[] velocities,
        [In] double[] masses,
        [In] Vector3[] thrusts,
        [In] float[] dragCoeffs,
        [In] float[] areasUU,
        [In] sbyte[] normalSign,
        [In] byte[] isThrusting,
        [In, Out] sbyte[] latchedParityIO,
        int count,
        double muUnity,
        float totalDt,
        int substeps,
        [Out] double[] deltaVOut);

}

