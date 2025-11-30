using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Mathematics;

public static class NativePhysics
{
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

}

