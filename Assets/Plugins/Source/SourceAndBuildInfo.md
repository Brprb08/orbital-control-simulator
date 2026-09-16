### N-Body Simulation Source Code

This folder contains the C++ source file (`Dopri54Physics.cpp`) used to build the native plugin (`PhysicsPlugin.dll`) for the Unity simulation. The compiled DLL is located in `Assets/Plugins/x86_64`.

### Purpose

This code handles the native gravity, force accumulation, and Dormand-Prince integration used by the runtime physics plugin. Included here for transparency and to allow modification or rebuilding if needed.

### How to Build the DLL

1. Use any C++ compiler that supports dynamic linking.
2. Compile the source into a Windows DLL using a command like:

```text
g++ -std=c++17 -O2 -shared -static-libgcc -static-libstdc++ -o PhysicsPlugin.dll Dopri54Physics.cpp
```

### Replacing the DLL in Unity

- Save your scene and close Unity Editor first; Windows locks loaded native plugins.
- Go to `Assets/Plugins/x86_64/`
- Replace the existing `PhysicsPlugin.dll` with your newly compiled version
- Reopen Unity to load the replacement plugin. A project refresh does not unload the old DLL.

### Notes

Unity does not directly use the `.cpp` file, only the compiled DLL. This file is included for review, debugging, or extending the physics logic.
