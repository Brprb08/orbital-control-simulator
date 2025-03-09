### N-Body Simulation Source Code

This folder contains the source code (`rk4Physics.cpp`) for the N-Body simulation used in the Unity project. The compiled DLL (`dllFilehere.dll`) is in the `Plugins/x86_64` directory.

### Purpose
The source code is included for reference and to allow rebuilding the DLL if needed.

### How to Build the DLL
1. Use a C++ compiler that supports dynamic linking.
2. Compile the code to a DLL with a command like:

```
g++ -shared -fPIC -o dllFileNameHere.dll rk4Physics.cpp
```

### After rebuilding DLL file -> Replace the DLL in Unity:
- Navigate to Assets/Plugins/x86_64/.
- Delete the existing dllFilehere.dll.
- Copy the newly compiled dllFileNameHere.dll into the same directory.
- Refresh the Unity project (e.g., right-click the Assets folder and select "Refresh").

### Additional Info
This source code is not directly used by Unity but is provided for transparency and potential modification.
