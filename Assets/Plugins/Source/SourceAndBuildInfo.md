### N-Body Simulation Source Code

This folder contains the C++ source file (`rk4Physics.cpp`) used to build the native plugin (`PhysicsPlugin.dll`) for the Unity simulation. The compiled DLL is located in `Assets/Plugins/x86_64`.

### Purpose

This code handles all N-body gravity and RK4 integration calculations. Included here for transparency and to allow modification or rebuilding if needed.

### How to Build the DLL

1. Use any C++ compiler that supports dynamic linking.
2. Compile the source into a Windows DLL using a command like:

```
g++ -shared -fPIC -o PhysicsPlugin.dll rk4Physics.cpp
```

### Replacing the DLL in Unity

- Go to `Assets/Plugins/x86_64/`
- Replace the existing `PhysicsPlugin.dll` with your newly compiled version
- In Unity, refresh the project to reload the plugin

### Notes

Unity doesn’t directly use the `.cpp` file — only the compiled DLL. This file is included for review, debugging, or extending the physics logic.

