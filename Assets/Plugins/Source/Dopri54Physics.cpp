#include <cmath>

// Unity-Compatible C Interface
extern "C"
{
    // Unity Vector3 equivalent (float)
    struct Vector3
    {
        float x, y, z;
    };

    // Internal double‐precision Vector3
    struct Vector3d
    {
        double x, y, z;
    };

    // Internal double‐precision double3
    struct double3
    {
        double x, y, z;
    };

    // Conversion helpers
    inline Vector3d ToVector3dFromVector3(const Vector3 &v)
    {
        return {v.x, v.y, v.z};
    }

    inline Vector3d ToVector3dFromDouble3(const double3 &v)
    {
        return {v.x, v.y, v.z};
    }

    inline double3 ToDouble3(const Vector3d &v)
    {
        return {v.x, v.y, v.z};
    }

    const double G = 6.67430e-23;   // gravitational constant
    const double minDistSq = 1e-20; // avoid singularity
    const double maxForce = 1e8;    // cap force

    // Compute gravitational acceleration
    Vector3d ComputeAcceleration(Vector3d pos, double *masses, Vector3d *bodies, int n)
    {
        Vector3d a = {0, 0, 0};
        for (int i = 0; i < n; i++)
        {
            Vector3d d = {bodies[i].x - pos.x,
                          bodies[i].y - pos.y,
                          bodies[i].z - pos.z};
            double r2 = d.x * d.x + d.y * d.y + d.z * d.z;
            if (r2 < minDistSq)
                continue;
            double F = G * masses[i] / r2;
            F = fmin(F, maxForce);
            double r = sqrt(r2);
            double f_r = F / r;
            a.x += f_r * d.x;
            a.y += f_r * d.y;
            a.z += f_r * d.z;
        }
        return a;
    }

    // ——— Dormand–Prince 5(4) coefficients ———
    static const double c_dp[7] = {
        0.0,
        1.0 / 5.0,
        3.0 / 10.0,
        4.0 / 5.0,
        8.0 / 9.0,
        1.0,
        1.0};

    static const double a_dp[7][6] = {
        {},
        {1.0 / 5.0},
        {3.0 / 40.0, 9.0 / 40.0},
        {44.0 / 45.0, -56.0 / 15.0, 32.0 / 9.0},
        {19372.0 / 6561.0, -25360.0 / 2187.0,
         64448.0 / 6561.0, -212.0 / 729.0},
        {9017.0 / 3168.0, -355.0 / 33.0,
         46732.0 / 5247.0, 49.0 / 176.0, -5103.0 / 18656.0},
        {35.0 / 384.0, 0.0,
         500.0 / 1113.0, 125.0 / 192.0,
         -2187.0 / 6784.0, 11.0 / 84.0}};

    // 5th-order weights
    static const double b_dp[7] = {
        35.0 / 384.0,
        0.0,
        500.0 / 1113.0,
        125.0 / 192.0,
        -2187.0 / 6784.0,
        11.0 / 84.0,
        0.0};

    // (unused here) 4th-order weights for the embedded solution
    static const double b4_dp[7] = {
        5179.0 / 57600.0,
        0.0,
        7571.0 / 16695.0,
        393.0 / 640.0,
        -92097.0 / 339200.0,
        187.0 / 2100.0,
        1.0 / 40.0};

    // Single fixed-step Dormand–Prince 5th-order update:
    static void DormandPrinceStep(
        Vector3d &pos,
        Vector3d &vel,
        double mass,
        double dt,
        const Vector3d *bodies,
        const double *masses,
        int n,
        Vector3d thrustImpulse)
    {
        if (mass <= 1e-6)
            return;

        // precompute thrust acceleration
        Vector3d th = {
            thrustImpulse.x / mass,
            thrustImpulse.y / mass,
            thrustImpulse.z / mass};

        // stage storage
        Vector3d kx[7], kv[7];

        // --- stage 1 (i=0) ---
        kx[0] = vel;
        kv[0] = ComputeAcceleration(pos, (double *)masses, (Vector3d *)bodies, n);
        kv[0].x += th.x;
        kv[0].y += th.y;
        kv[0].z += th.z;

        // --- stages 2..7 (i=1..6) ---
        for (int i = 1; i < 7; ++i)
        {
            // form the intermediate state
            Vector3d pi = pos, vi = vel;
            for (int j = 0; j < i; ++j)
            {
                pi.x += dt * a_dp[i][j] * kx[j].x;
                pi.y += dt * a_dp[i][j] * kx[j].y;
                pi.z += dt * a_dp[i][j] * kx[j].z;
                vi.x += dt * a_dp[i][j] * kv[j].x;
                vi.y += dt * a_dp[i][j] * kv[j].y;
                vi.z += dt * a_dp[i][j] * kv[j].z;
            }
            kx[i] = vi;
            kv[i] = ComputeAcceleration(pi, (double *)masses, (Vector3d *)bodies, n);
            kv[i].x += th.x;
            kv[i].y += th.y;
            kv[i].z += th.z;
        }

        // --- combine to get 5th-order update ---
        for (int i = 0; i < 7; ++i)
        {
            pos.x += dt * b_dp[i] * kx[i].x;
            pos.y += dt * b_dp[i] * kx[i].y;
            pos.z += dt * b_dp[i] * kx[i].z;
            vel.x += dt * b_dp[i] * kv[i].x;
            vel.y += dt * b_dp[i] * kv[i].y;
            vel.z += dt * b_dp[i] * kv[i].z;
        }
    }

    // External entry‐point
    extern "C" __attribute__((visibility("default"))) void DormandPrinceSingle(
        double3 *position,
        double3 *velocity,
        float mass,
        Vector3 *bodies,
        float *masses,
        int numBodies,
        float dt,
        Vector3 thrustImpulse)
    {
        if (mass <= 1e-6f)
            return;

        // convert to doubles
        Vector3d posD = ToVector3dFromDouble3(*position);
        Vector3d velD = ToVector3dFromDouble3(*velocity);

        // copy bodies & masses
        Vector3d bodiesD[256];
        double massesD[256];
        for (int i = 0; i < numBodies; i++)
        {
            bodiesD[i] = ToVector3dFromVector3(bodies[i]);
            massesD[i] = (double)masses[i];
        }

        // take one DOPRI5 step
        DormandPrinceStep(
            posD, velD,
            (double)mass,
            (double)dt,
            bodiesD, massesD, numBodies,
            ToVector3dFromVector3(thrustImpulse));

        // write back
        *position = ToDouble3(posD);
        *velocity = ToDouble3(velD);
    }
}
