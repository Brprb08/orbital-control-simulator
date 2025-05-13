// #include <cmath>
// #include <algorithm>

// extern "C"
// {
//     struct Vector3
//     {
//         float x, y, z;
//     };

//     struct Vector3d
//     {
//         double x, y, z;
//     };

//     struct double3
//     {
//         double x, y, z;
//     };

//     /**
//      * Cast from Vector3 (float) to Vector3d (double).
//      */
//     inline Vector3d ToVector3dFromVector3(const Vector3 &v) { return {v.x, v.y, v.z}; }

//     /**
//      * Convert from double3 to Vector3d. Structs are basically the same shape.
//      */
//     inline Vector3d ToVector3dFromDouble3(const double3 &v) { return {v.x, v.y, v.z}; }

//     /**
//      * Converts from Vector3d to double3.
//      */
//     inline double3 ToDouble3(const Vector3d &v) { return {v.x, v.y, v.z}; }

//     const double G = 6.67430e-23;
//     const double minDistSq = 1e-20;
//     const double maxForce = 1e8;

//     const double UNIT_TO_KM = 10.0;
//     const double EARTH_RADIUS_KM = 637.8 * UNIT_TO_KM;
//     const double OMEGA_EARTH = 7.2921150e-5;

//     static const double DENSITY_SCALE = 2;

//     static const int densTableSize = 51;

//     /**
//      * Altitudes in km for the atmospheric model.
//      */
//     static const double altitudes[densTableSize] = {
//         0, 10, 20, 30, 40,
//         50, 60, 70, 80, 90,
//         100, 110, 120, 130, 140,
//         150, 160, 170, 180, 190,
//         200, 210, 220, 230, 240,
//         250, 260, 270, 280, 290,
//         300, 310, 320, 330, 340,
//         350, 360, 370, 380, 390,
//         400, 410, 420, 430, 440,
//         450, 460, 470, 480, 490,
//         500};

//     /**
//      * Atmospheric densities at corresponding altitudes.
//      * Values are scaled to simulation mass units.
//      */
//     static const double densities[densTableSize] = {
//         1.225e9, 4.135e8, 8.891e7, 1.841e7, 3.996e6,
//         1.027e6, 3.097e5, 8.283e4, 1.846e4, 3.416e3,
//         650.0, 240.0, 96.0, 46.0, 22.0,
//         1.78, 1.05, 0.62, 0.36, 0.21,
//         0.12, 0.080, 0.050, 0.031, 0.020,
//         0.170326, 0.132650, 0.103308, 0.080456, 0.062660,
//         0.048799, 0.038005, 0.029598, 0.023051, 0.017952,
//         0.013981, 0.010889, 0.008480, 0.006604, 0.005143,
//         0.004407, 0.003512, 0.002730, 0.002184, 0.001837,
//         0.001582, 0.001307, 0.001007, 0.000761, 0.000591,
//         0.000461};

//     static double scaleHeights[densTableSize - 1];

//     /**
//      * Initializes scale heights between atmospheric layers using log scale.
//      */
//     struct DensityTableInit
//     {
//         DensityTableInit()
//         {
//             for (int i = 0; i < densTableSize - 1; ++i)
//             {
//                 double h0 = altitudes[i], h1 = altitudes[i + 1];
//                 double rho0 = densities[i], rho1 = densities[i + 1];
//                 scaleHeights[i] = -(h1 - h0) / std::log(rho1 / rho0);
//             }
//         }
//     } _densityTableInit;

//     /**
//      * Gets atmospheric density (in simulation mass units) at a given altitude in km.
//      * Applies DENSITY_SCALE below 200km to account for missing lower-atmosphere effects.
//      */
//     static double ComputeAtmosphericDensity(double altitudeKm)
//     {
//         if (altitudeKm <= altitudes[0])
//             return densities[0];
//         if (altitudeKm >= altitudes[densTableSize - 1])
//             return 0.0;

//         // find segment
//         int idx = int(std::upper_bound(altitudes, altitudes + densTableSize, altitudeKm) - altitudes) - 1;
//         double h0 = altitudes[idx];
//         double rho = densities[idx] * std::exp(-(altitudeKm - h0) / scaleHeights[idx]);
//         if (altitudeKm > 200)
//         {
//             return rho;
//         }
//         return rho * DENSITY_SCALE;
//     }

//     /**
//      * Computes drag acceleration based on position, velocity, area, mass, and drag coefficient.
//      * Accounts for Earth rotation to calculate relative wind.
//      */
//     static Vector3d ComputeDragAcceleration(
//         const Vector3d &velUU,
//         const Vector3d &posRelUU,
//         double mass,
//         double areaUU,
//         double Cd)
//     {
//         double xkm = posRelUU.x * UNIT_TO_KM;
//         double ykm = posRelUU.y * UNIT_TO_KM;
//         double zkm = posRelUU.z * UNIT_TO_KM;
//         double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);
//         double alt = std::max(0.0, rkm - EARTH_RADIUS_KM);

//         double rho = ComputeAtmosphericDensity(alt);
//         if (rho < 1e-12)
//             return {0, 0, 0};

//         Vector3d vkm = {velUU.x * UNIT_TO_KM,
//                         velUU.y * UNIT_TO_KM,
//                         velUU.z * UNIT_TO_KM};

//         Vector3d vatm = {-OMEGA_EARTH * ykm,
//                          OMEGA_EARTH * xkm,
//                          0.0};

//         Vector3d vrel = {vkm.x - vatm.x,
//                          vkm.y - vatm.y,
//                          vkm.z - vatm.z};
//         double speed = std::sqrt(vrel.x * vrel.x + vrel.y * vrel.y + vrel.z * vrel.z);
//         if (speed < 1e-6)
//             return {0, 0, 0};

//         double areaKm2 = areaUU * UNIT_TO_KM * UNIT_TO_KM;
//         double factor = -0.5 * Cd * areaKm2 * rho / mass;
//         Vector3d a = {factor * vrel.x * speed,
//                       factor * vrel.y * speed,
//                       factor * vrel.z * speed};

//         return {a.x / UNIT_TO_KM,
//                 a.y / UNIT_TO_KM,
//                 a.z / UNIT_TO_KM};
//     }

//     /**
//      * Computes gravitational acceleration on a body due to all other bodies in the system.
//      * Caps force to prevent simulation blowups.
//      */
//     Vector3d ComputeAcceleration(Vector3d pos, double *masses, Vector3d *bodies, int n)
//     {
//         Vector3d a{0, 0, 0};
//         for (int i = 0; i < n; i++)
//         {
//             Vector3d d{bodies[i].x - pos.x,
//                        bodies[i].y - pos.y,
//                        bodies[i].z - pos.z};
//             double r2 = d.x * d.x + d.y * d.y + d.z * d.z;
//             if (r2 < minDistSq)
//                 continue;
//             double F = std::min(G * masses[i] / r2, maxForce);
//             double r = std::sqrt(r2);
//             double f_r = F / r;
//             a.x += f_r * d.x;
//             a.y += f_r * d.y;
//             a.z += f_r * d.z;
//         }
//         return a;
//     }

//     static const double c_dp[7] = {0.0, 1. / 5, 3. / 10, 4. / 5, 8. / 9, 1.0, 1.0};
//     static const double a_dp[7][6] = {
//         {}, {1. / 5}, {3. / 40, 9. / 40}, {44. / 45, -56. / 15, 32. / 9}, {19372. / 6561, -25360. / 2187, 64448. / 6561, -212. / 729}, {9017. / 3168, -355. / 33, 46732. / 5247, 49. / 176, -5103. / 18656}, {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84}};
//     static const double b_dp[7] = {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84, 0};

//     /**
//      * Dormand-Prince 5th order Runge-Kutta integrator step for a single object.
//      * Includes gravity, thrust, and atmospheric drag.
//      *
//      * @param pos Position (updated in-place)
//      * @param vel Velocity (updated in-place)
//      * @param mass Object mass
//      * @param dt Timestep
//      * @param bodies Array of body positions (first one is assumed to be Earth)
//      * @param masses Array of body masses
//      * @param n Number of bodies
//      * @param thrustAcc Acceleration due to applied thrust
//      * @param dragCoeff Drag coefficient
//      * @param areaUU Cross-sectional area (in sim units^2)
//      */
//     static void DormandPrinceStep(
//         Vector3d &pos,
//         Vector3d &vel,
//         double mass,
//         double dt,
//         const Vector3d *bodies,
//         const double *masses,
//         int n,
//         Vector3d thrustAcc,
//         double dragCoeff,
//         double areaUU)
//     {
//         if (mass <= 1e-6)
//             return;

//         Vector3d kx[7], kv[7];

//         kx[0] = vel;
//         kv[0] = ComputeAcceleration(pos, (double *)masses, (Vector3d *)bodies, n);
//         kv[0].x += thrustAcc.x;
//         kv[0].y += thrustAcc.y;
//         kv[0].z += thrustAcc.z;

//         Vector3d drag1 = ComputeDragAcceleration(vel, {pos.x - bodies[0].x, pos.y - bodies[0].y, pos.z - bodies[0].z}, mass, areaUU, dragCoeff);
//         kv[0].x += drag1.x;
//         kv[0].y += drag1.y;
//         kv[0].z += drag1.z;

//         for (int i = 1; i < 7; i++)
//         {
//             Vector3d pi = pos, vi = vel;
//             for (int j = 0; j < i; j++)
//             {
//                 pi.x += dt * a_dp[i][j] * kx[j].x;
//                 pi.y += dt * a_dp[i][j] * kx[j].y;
//                 pi.z += dt * a_dp[i][j] * kx[j].z;
//                 vi.x += dt * a_dp[i][j] * kv[j].x;
//                 vi.y += dt * a_dp[i][j] * kv[j].y;
//                 vi.z += dt * a_dp[i][j] * kv[j].z;
//             }
//             kx[i] = vi;
//             kv[i] = ComputeAcceleration(pi, (double *)masses, (Vector3d *)bodies, n);
//             kv[i].x += thrustAcc.x;
//             kv[i].y += thrustAcc.y;
//             kv[i].z += thrustAcc.z;

//             Vector3d relPos = {pi.x - bodies[0].x,
//                                pi.y - bodies[0].y,
//                                pi.z - bodies[0].z};
//             Vector3d drag_i = ComputeDragAcceleration(vi, relPos, mass, areaUU, dragCoeff);
//             kv[i].x += drag_i.x;
//             kv[i].y += drag_i.y;
//             kv[i].z += drag_i.z;
//         }

//         for (int i = 0; i < 7; i++)
//         {
//             pos.x += dt * b_dp[i] * kx[i].x;
//             pos.y += dt * b_dp[i] * kx[i].y;
//             pos.z += dt * b_dp[i] * kx[i].z;
//             vel.x += dt * b_dp[i] * kv[i].x;
//             vel.y += dt * b_dp[i] * kv[i].y;
//             vel.z += dt * b_dp[i] * kv[i].z;
//         }
//     }

//     /**
//      * Public C-style entry point to integrate position and velocity of a single body.
//      * Converts inputs to double precision, runs the integrator, and converts back.
//      *
//      * @param position Input/output position
//      * @param velocity Input/output velocity
//      * @param mass Mass of the object
//      * @param bodies Array of other bodies (assumes 256 max)
//      * @param masses Array of their masses
//      * @param numBodies Number of other bodies
//      * @param dt Timestep
//      * @param thrustImpulse Thrust impulse applied this step (force * dt)
//      * @param dragCoeff Drag coefficient
//      * @param areaUU Area in simulation units^2
//      */
//     extern "C" __attribute__((visibility("default"))) void DormandPrinceSingle(
//         double3 *position,
//         double3 *velocity,
//         float mass,
//         Vector3 *bodies,
//         float *masses,
//         int numBodies,
//         float dt,
//         Vector3 thrustImpulse,
//         float dragCoeff,
//         float areaUU)
//     {
//         if (mass <= 1e-6f)
//             return;

//         Vector3d posD = ToVector3dFromDouble3(*position);
//         Vector3d velD = ToVector3dFromDouble3(*velocity);

//         Vector3d bodiesD[256];
//         double massesD[256];
//         for (int i = 0; i < numBodies; i++)
//         {
//             bodiesD[i] = ToVector3dFromVector3(bodies[i]);
//             massesD[i] = (double)masses[i];
//         }

//         Vector3d th{thrustImpulse.x / mass, thrustImpulse.y / mass, thrustImpulse.z / mass};

//         DormandPrinceStep(
//             posD, velD,
//             (double)mass,
//             (double)dt,
//             bodiesD, massesD, numBodies,
//             th,
//             (double)dragCoeff,
//             (double)areaUU);

//         *position = ToDouble3(posD);
//         *velocity = ToDouble3(velD);
//     }
// }

// ------------------------------------------------------------------

#include <cmath>
#include <algorithm>

extern "C"
{
    struct Vector3
    {
        float x, y, z;
    };

    struct Vector3d
    {
        double x, y, z;
    };

    struct double3
    {
        double x, y, z;
    };

    /**
     * Cast from Vector3 (float) to Vector3d (double).
     */
    inline Vector3d ToVector3dFromVector3(const Vector3 &v) { return {v.x, v.y, v.z}; }

    /**
     * Convert from double3 to Vector3d. Structs are basically the same shape.
     */
    inline Vector3d ToVector3dFromDouble3(const double3 &v) { return {v.x, v.y, v.z}; }

    /**
     * Converts from Vector3d to double3.
     */
    inline double3 ToDouble3(const Vector3d &v) { return {v.x, v.y, v.z}; }

    const double G = 6.67430e-23;
    const double minDistSq = 1e-20;
    const double maxForce = 1e8;

    const double UNIT_TO_KM = 10.0;
    const double EARTH_RADIUS_KM = 637.8 * UNIT_TO_KM;
    const double OMEGA_EARTH = 7.2921150e-5;
    const double DENSITY_SCALE = 1.0;

    static const int JR_N = 51;
    static const double JR_ALT[JR_N] = {
        0, 10, 20, 30, 40,
        50, 60, 70, 80, 90,
        100, 110, 120, 130, 140,
        150, 160, 170, 180, 190,
        200, 210, 220, 230, 240,
        250, 260, 270, 280, 290,
        300, 310, 320, 330, 340,
        350, 360, 370, 380, 390,
        400, 410, 420, 430, 440,
        450, 460, 470, 480, 490,
        500};

    // Original JR_RHO multiplied by 1000× (i.e. scale factor = 1000)
    static const double JR_RHO[JR_N] = {
        1.35e9, 4.56e8, 9.82e7, 2.05e7, 4.46e6,
        1.15e6, 3.48e5, 9.11e4, 2.06e4, 3.81e3,
        725.0, 267.0, 107.0, 51.0, 24.0,
        1.95, 1.15, 0.68, 0.40, 0.24,
        0.135, 0.090, 0.056, 0.035, 0.022,
        0.187, 0.1459, 0.1136, 0.0885, 0.0689,
        0.0537, 0.0418, 0.0326, 0.0254, 0.0198,
        0.0154, 0.0120, 0.00938, 0.0073, 0.00568,
        0.00487, 0.00378, 0.00292, 0.00232, 0.00197,
        0.00168, 0.00138, 0.00106, 0.000803, 0.000622,
        0.000485};

    // static const double JR_RHO[JR_N] = {
    //     1.225e9, 4.135e8, 8.891e7, 1.841e7, 3.996e6,
    //     1.027e6, 3.097e5, 8.283e4, 1.846e4, 3.416e3,
    //     650.0, 240.0, 96.0, 46.0, 22.0,
    //     // 1.602, 1.05, 0.682, 0.429, 0.2415,
    //     1.78, 1.05, 0.62, 0.36, 0.21,
    //     // 2.5315, 1.48625, 0.879, 0.432, 0.22925,
    //     // 2.492, 1.47, 0.868, 0.504, 0.294,
    //     0.12, 0.080, 0.050, 0.031, 0.020,
    //     0.170326, 0.132650, 0.103308, 0.080456, 0.062660,
    //     0.048799, 0.038005, 0.029598, 0.023051, 0.017952,
    //     0.013981, 0.010889, 0.008480, 0.006604, 0.005143,
    //     0.004407, 0.003512, 0.002730, 0.002184, 0.001837,
    //     0.001582, 0.001307, 0.001007, 0.000761, 0.000591,
    //     0.000461};
    // 1.225000e+11, 4.135000e+10, 8.988000e+09, 1.886000e+09, 3.996000e+08,
    // 1.027000e+08, 3.097000e+07, 8.283000e+06, 1.846000e+06, 3.416000e+05,
    // 5.606000e+04, 1.488000e+04, 3.725000e+03, 8.484000e+02, 2.027000e+02,
    // 5.606000e+01, 1.846000e+01, 7.210000e+00, 3.970000e+00, 2.330000e+00,
    // 1.430000e+00, 9.690000e-01, 6.510000e-01, 4.520000e-01, 3.210000e-01,
    // 2.310000e-01,
    // 1.640000e-01, 1.170000e-01, 8.440000e-02, 6.120000e-02,
    // 4.330000e-02, 3.090000e-02, 2.220000e-02, 1.600000e-02, 1.160000e-02,
    // 8.510000e-03, 6.300000e-03, 4.700000e-03, 3.530000e-03, 2.660000e-03,
    // 2.030000e-03, 1.570000e-03, 1.210000e-03, 9.350000e-04, 7.270000e-04,
    // 5.680000e-04, 4.500000e-04, 3.560000e-04, 2.820000e-04, 2.250000e-04,
    // 1.800000e-04};

    // 0, 10, 20, 30, 40,
    //     50, 60, 70, 80, 90,
    //     100, 110, 120, 130, 140,
    //     150, 160, 170, 180, 190,
    //     200, 210, 220, 230, 240,

    // will hold per‑bin scale heights
    static double JR_H[JR_N - 1];
    struct JRInit
    {
        JRInit()
        {
            // compute scale heights between the 10 km points
            for (int i = 0; i < JR_N - 1; ++i)
            {
                double dh = JR_ALT[i + 1] - JR_ALT[i];
                JR_H[i] = -dh / std::log(JR_RHO[i + 1] / JR_RHO[i]);
            }
        }
    } _jrInit;

    /**
     * Returns Jacchia–Roberts density (kg/km³) at any altitude [0,500] km
     * via simple exponential interpolation within the nearest 10 km band.
     */
    static inline double ComputeAtmosphericDensity(double altKm)
    {
        if (altKm <= JR_ALT[0])
            return JR_RHO[0];
        if (altKm >= JR_ALT[JR_N - 1])
            return 0.0;

        // pick band
        int idx = std::min(int(altKm / 10.0), JR_N - 2);
        double dH = altKm - JR_ALT[idx];
        return JR_RHO[idx] * std::exp(-dH / JR_H[idx]) * DENSITY_SCALE;
    }

    /**
     * Drag accel (–½ρCdA/m · vrel|vrel|), including Earth rotation.
     */
    static Vector3d ComputeDragAcceleration(
        const Vector3d &velUU,
        const Vector3d &posRelUU,
        double mass,
        double areaUU,
        double Cd)
    {
        double xkm = posRelUU.x * UNIT_TO_KM;
        double ykm = posRelUU.y * UNIT_TO_KM;
        double zkm = posRelUU.z * UNIT_TO_KM;
        double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);
        double alt = std::max(0.0, rkm - EARTH_RADIUS_KM);

        double rho = ComputeAtmosphericDensity(alt);
        if (rho < 1e-12)
            return {0, 0, 0};

        Vector3d vkm = {velUU.x * UNIT_TO_KM, velUU.y * UNIT_TO_KM, velUU.z * UNIT_TO_KM};
        Vector3d vatm = {-OMEGA_EARTH * ykm, OMEGA_EARTH * xkm, 0.0};
        Vector3d vrel = {vkm.x - vatm.x, vkm.y - vatm.y, vkm.z - vatm.z};
        double speed = std::sqrt(vrel.x * vrel.x + vrel.y * vrel.y + vrel.z * vrel.z);
        if (speed < 1e-6)
            return {0, 0, 0};

        double A2 = areaUU * UNIT_TO_KM * UNIT_TO_KM;
        double factor = -0.5 * Cd * A2 * rho / mass;
        Vector3d a = {factor * vrel.x * speed,
                      factor * vrel.y * speed,
                      factor * vrel.z * speed};
        return {a.x / UNIT_TO_KM, a.y / UNIT_TO_KM, a.z / UNIT_TO_KM};
    }

    /**
     * Computes gravitational acceleration on a body due to all other bodies in the system.
     * Caps force to prevent simulation blowups.
     */
    Vector3d ComputeAcceleration(Vector3d pos, double *masses, Vector3d *bodies, int n)
    {
        Vector3d a{0, 0, 0};
        for (int i = 0; i < n; i++)
        {
            Vector3d d{bodies[i].x - pos.x,
                       bodies[i].y - pos.y,
                       bodies[i].z - pos.z};
            double r2 = d.x * d.x + d.y * d.y + d.z * d.z;
            if (r2 < minDistSq)
                continue;
            double F = std::min(G * masses[i] / r2, maxForce);
            double r = std::sqrt(r2);
            double f_r = F / r;
            a.x += f_r * d.x;
            a.y += f_r * d.y;
            a.z += f_r * d.z;
        }
        return a;
    }

    static const double c_dp[7] = {0.0, 1. / 5, 3. / 10, 4. / 5, 8. / 9, 1.0, 1.0};
    static const double a_dp[7][6] = {
        {}, {1. / 5}, {3. / 40, 9. / 40}, {44. / 45, -56. / 15, 32. / 9}, {19372. / 6561, -25360. / 2187, 64448. / 6561, -212. / 729}, {9017. / 3168, -355. / 33, 46732. / 5247, 49. / 176, -5103. / 18656}, {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84}};
    static const double b_dp[7] = {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84, 0};

    /**
     * Dormand-Prince 5th order Runge-Kutta integrator step for a single object.
     * Includes gravity, thrust, and atmospheric drag.
     *
     * @param pos Position (updated in-place)
     * @param vel Velocity (updated in-place)
     * @param mass Object mass
     * @param dt Timestep
     * @param bodies Array of body positions (first one is assumed to be Earth)
     * @param masses Array of body masses
     * @param n Number of bodies
     * @param thrustAcc Acceleration due to applied thrust
     * @param dragCoeff Drag coefficient
     * @param areaUU Cross-sectional area (in sim units^2)
     */
    static void DormandPrinceStep(
        Vector3d &pos,
        Vector3d &vel,
        double mass,
        double dt,
        const Vector3d *bodies,
        const double *masses,
        int n,
        Vector3d thrustAcc,
        double dragCoeff,
        double areaUU)
    {
        if (mass <= 1e-6)
            return;

        Vector3d kx[7], kv[7];

        kx[0] = vel;
        kv[0] = ComputeAcceleration(pos, (double *)masses, (Vector3d *)bodies, n);
        kv[0].x += thrustAcc.x;
        kv[0].y += thrustAcc.y;
        kv[0].z += thrustAcc.z;

        Vector3d drag1 = ComputeDragAcceleration(vel, {pos.x - bodies[0].x, pos.y - bodies[0].y, pos.z - bodies[0].z}, mass, areaUU, dragCoeff);
        kv[0].x += drag1.x;
        kv[0].y += drag1.y;
        kv[0].z += drag1.z;

        for (int i = 1; i < 7; i++)
        {
            Vector3d pi = pos, vi = vel;
            for (int j = 0; j < i; j++)
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
            kv[i].x += thrustAcc.x;
            kv[i].y += thrustAcc.y;
            kv[i].z += thrustAcc.z;

            Vector3d relPos = {pi.x - bodies[0].x,
                               pi.y - bodies[0].y,
                               pi.z - bodies[0].z};
            Vector3d drag_i = ComputeDragAcceleration(vi, relPos, mass, areaUU, dragCoeff);
            kv[i].x += drag_i.x;
            kv[i].y += drag_i.y;
            kv[i].z += drag_i.z;
        }

        for (int i = 0; i < 7; i++)
        {
            pos.x += dt * b_dp[i] * kx[i].x;
            pos.y += dt * b_dp[i] * kx[i].y;
            pos.z += dt * b_dp[i] * kx[i].z;
            vel.x += dt * b_dp[i] * kv[i].x;
            vel.y += dt * b_dp[i] * kv[i].y;
            vel.z += dt * b_dp[i] * kv[i].z;
        }
    }

    /**
     * Public C-style entry point to integrate position and velocity of a single body.
     * Converts inputs to double precision, runs the integrator, and converts back.
     *
     * @param position Input/output position
     * @param velocity Input/output velocity
     * @param mass Mass of the object
     * @param bodies Array of other bodies (assumes 256 max)
     * @param masses Array of their masses
     * @param numBodies Number of other bodies
     * @param dt Timestep
     * @param thrustImpulse Thrust impulse applied this step (force * dt)
     * @param dragCoeff Drag coefficient
     * @param areaUU Area in simulation units^2
     */
    extern "C" __attribute__((visibility("default"))) void DormandPrinceSingle(
        double3 *position,
        double3 *velocity,
        float mass,
        Vector3 *bodies,
        float *masses,
        int numBodies,
        float dt,
        Vector3 thrustImpulse,
        float dragCoeff,
        float areaUU)
    {
        if (mass <= 1e-6f)
            return;

        Vector3d posD = ToVector3dFromDouble3(*position);
        Vector3d velD = ToVector3dFromDouble3(*velocity);

        Vector3d bodiesD[256];
        double massesD[256];
        for (int i = 0; i < numBodies; i++)
        {
            bodiesD[i] = ToVector3dFromVector3(bodies[i]);
            massesD[i] = (double)masses[i];
        }

        Vector3d th{thrustImpulse.x / mass, thrustImpulse.y / mass, thrustImpulse.z / mass};

        DormandPrinceStep(
            posD, velD,
            (double)mass,
            (double)dt,
            bodiesD, massesD, numBodies,
            th,
            (double)dragCoeff,
            (double)areaUU);

        *position = ToDouble3(posD);
        *velocity = ToDouble3(velD);
    }
}

// … rest of your ComputeAcceleration, Dormand–Prince integrator, etc., unchanged …
// (Simply swap your old ComputeAtmosphericDensity with this one.)

//     /**
//      * Computes gravitational acceleration on a body due to all other bodies in the system.
//      * Caps force to prevent simulation blowups.
//      */
//     Vector3d ComputeAcceleration(Vector3d pos, double *masses, Vector3d *bodies, int n)
//     {
//         Vector3d a{0, 0, 0};
//         for (int i = 0; i < n; i++)
//         {
//             // Vector3d d{bodies[i].x - pos.x,
//             //            bodies[i].y - pos.y,
//             //            bodies[i].z - pos.z};
//             double r2 = pos.x * pos.x + pos.y * pos.y + pos.z * pos.z;
//             if (r2 < minDistSq)
//                 continue;

//             double F = std::min(G * masses[i] / r2, maxForce);
//             double invR = 1.0 / std::sqrt(r2);
//             double f_r = F * invR;

//             a.x += f_r * pos.x;
//             a.y += f_r * pos.y;
//             a.z += f_r * pos.z;
//         }
//         return a;
//     }

//     // Dormand–Prince integrator coefficients
//     static const double c_dp[7] = {0.0, 1. / 5, 3. / 10, 4. / 5, 8. / 9, 1.0, 1.0};
//     static const double a_dp[7][6] = {
//         {},
//         {1. / 5},
//         {3. / 40, 9. / 40},
//         {44. / 45, -56. / 15, 32. / 9},
//         {19372. / 6561, -25360. / 2187, 64448. / 6561, -212. / 729},
//         {9017. / 3168, -355. / 33, 46732. / 5247, 49. / 176, -5103. / 18656},
//         {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84}};
//     static const double b_dp[7] = {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84, 0};

//     static void DormandPrinceStep(
//         Vector3d &pos,
//         Vector3d &vel,
//         double mass,
//         double dt,
//         const Vector3d *bodies,
//         const double *masses,
//         int n,
//         Vector3d thrustAcc,
//         double dragCoeff,
//         double areaUU)
//     {
//         if (mass <= 1e-6)
//             return;

//         Vector3d kx[7], kv[7];
//         kx[0] = vel;
//         kv[0] = ComputeAcceleration(pos, (double *)masses, (Vector3d *)bodies, n);
//         kv[0].x += thrustAcc.x;
//         kv[0].y += thrustAcc.y;
//         kv[0].z += thrustAcc.z;

//         Vector3d drag1 = ComputeDragAcceleration(vel,
//                                                  {pos.x - bodies[0].x, pos.y - bodies[0].y, pos.z - bodies[0].z},
//                                                  mass, areaUU, dragCoeff);
//         kv[0].x += drag1.x;
//         kv[0].y += drag1.y;
//         kv[0].z += drag1.z;

//         for (int i = 1; i < 7; i++)
//         {
//             Vector3d pi = pos, vi = vel;
//             for (int j = 0; j < i; j++)
//             {
//                 pi.x += dt * a_dp[i][j] * kx[j].x;
//                 pi.y += dt * a_dp[i][j] * kx[j].y;
//                 pi.z += dt * a_dp[i][j] * kx[j].z;
//                 vi.x += dt * a_dp[i][j] * kv[j].x;
//                 vi.y += dt * a_dp[i][j] * kv[j].y;
//                 vi.z += dt * a_dp[i][j] * kv[j].z;
//             }
//             kx[i] = vi;
//             kv[i] = ComputeAcceleration(pi, (double *)masses, (Vector3d *)bodies, n);
//             kv[i].x += thrustAcc.x;
//             kv[i].y += thrustAcc.y;
//             kv[i].z += thrustAcc.z;

//             Vector3d di = ComputeDragAcceleration(vi,
//                                                   {pi.x - bodies[0].x, pi.y - bodies[0].y, pi.z - bodies[0].z},
//                                                   mass, areaUU, dragCoeff);
//             kv[i].x += di.x;
//             kv[i].y += di.y;
//             kv[i].z += di.z;
//         }

//         for (int i = 0; i < 7; i++)
//         {
//             pos.x += dt * b_dp[i] * kx[i].x;
//             pos.y += dt * b_dp[i] * kx[i].y;
//             pos.z += dt * b_dp[i] * kx[i].z;
//             vel.x += dt * b_dp[i] * kv[i].x;
//             vel.y += dt * b_dp[i] * kv[i].y;
//             vel.z += dt * b_dp[i] * kv[i].z;
//         }
//     }

//     extern "C" __attribute__((visibility("default"))) void DormandPrinceSingle(
//         double3 *position,
//         double3 *velocity,
//         float mass,
//         Vector3 *bodies,
//         float *masses,
//         int numBodies,
//         float dt,
//         Vector3 thrustImpulse,
//         float dragCoeff,
//         float areaUU)
//     {
//         if (mass <= 1e-6f)
//             return;

//         Vector3d posD = ToVector3dFromDouble3(*position);
//         Vector3d velD = ToVector3dFromDouble3(*velocity);

//         Vector3d bodiesD[256];
//         double massesD[256];
//         for (int i = 0; i < numBodies; i++)
//         {
//             bodiesD[i] = ToVector3dFromVector3(bodies[i]);
//             massesD[i] = (double)masses[i];
//         }

//         Vector3d th = {thrustImpulse.x / mass, thrustImpulse.y / mass, thrustImpulse.z / mass};

//         DormandPrinceStep(
//             posD, velD,
//             (double)mass,
//             (double)dt,
//             bodiesD, massesD, numBodies,
//             th,
//             (double)dragCoeff,
//             (double)areaUU);

//         *position = ToDouble3(posD);
//         *velocity = ToDouble3(velD);
//     }
// }

// -------------------------------------------------------------------

// #include <cmath>
// #include <algorithm>
// #include <cstring>
// extern "C"
// {

//     struct Vector3
//     {
//         float x, y, z;
//     };

//     struct Vector3d
//     {
//         double x, y, z;
//     };

//     struct double3
//     {
//         double x, y, z;
//     };

//     /**
//      * Cast from Vector3 (float) to Vector3d (double).
//      */
//     inline Vector3d ToVector3dFromVector3(const Vector3 &v) { return {v.x, v.y, v.z}; }

//     /**
//      * Convert from double3 to Vector3d. Structs are basically the same shape.
//      */
//     inline Vector3d ToVector3dFromDouble3(const double3 &v) { return {v.x, v.y, v.z}; }

//     /**
//      * Converts from Vector3d to double3.
//      */
//     inline double3 ToDouble3(const Vector3d &v) { return {v.x, v.y, v.z}; }

//     const double G = 6.67430e-23;
//     const double minDistSq = 1e-20;
//     const double maxForce = 1e8;

//     const double UNIT_TO_KM = 10.0;
//     const double EARTH_RADIUS_KM = 637.813 * UNIT_TO_KM;
//     const double OMEGA_EARTH = 7.2921150e-5;

//     static const double DENSITY_SCALE = 2;

//     static const int densTableSize = 51;

//     /**
//      * Altitudes in km for the atmospheric model.
//      */
//     static const double altitudes[densTableSize] = {
//         0, 10, 20, 30, 40,
//         50, 60, 70, 80, 90,
//         100, 110, 120, 130, 140,
//         150, 160, 170, 180, 190,
//         200, 210, 220, 230, 240,
//         250, 260, 270, 280, 290,
//         300, 310, 320, 330, 340,
//         350, 360, 370, 380, 390,
//         400, 410, 420, 430, 440,
//         450, 460, 470, 480, 490,
//         500};

//     /**
//      * Atmospheric densities at corresponding altitudes.
//      * Values are scaled to simulation mass units.
//      */
//     static const double densities[densTableSize] = {
//         1.225e9, 4.135e8, 8.891e7, 1.841e7, 3.996e6,
//         1.027e6, 3.097e5, 8.283e4, 1.846e4, 3.416e3,
//         650.0, 240.0, 96.0, 46.0, 22.0,
//         1.913, 1.129, .665, .387, .226,
//         0.129, 0.086, 0.050, 0.031, 0.020,
//         // 1.78, 1.05, 0.62, 0.36, 0.21,
//         // 0.12, 0.080, 0.050, 0.031, 0.020,
//         0.170326, 0.132650, 0.103308, 0.080456, 0.062660,
//         0.048799, 0.038005, 0.029598, 0.023051, 0.017952,
//         0.013981, 0.010889, 0.008480, 0.006604, 0.005143,
//         0.004006, 0.003120, 0.002430, 0.001892, 0.001474,
//         0.001148, 0.000894, 0.000696, 0.000542, 0.000422,
//         0.000329};

//     static double scaleHeights[densTableSize - 1];

//     /**
//      * Initializes scale heights between atmospheric layers using log scale.
//      */
//     // struct DensityTableInit
//     // {
//     //     DensityTableInit()
//     //     {
//     //         for (int i = 0; i < densTableSize - 1; ++i)
//     //         {
//     //             double h0 = altitudes[i], h1 = altitudes[i + 1];
//     //             double rho0 = densities[i], rho1 = densities[i + 1];
//     //             scaleHeights[i] = -(h1 - h0) / std::log(rho1 / rho0);
//     //         }
//     //     }
//     // } _densityTableInit;

//     /**
//      * Gets atmospheric density (in simulation mass units) at a given altitude in km.
//      * Applies DENSITY_SCALE below 200km to account for missing lower-atmosphere effects.
//      */
//     // static double ComputeAtmosphericDensity(double altitudeKm)
//     // {
//     //     if (altitudeKm <= altitudes[0])
//     //         return densities[0];
//     //     if (altitudeKm >= altitudes[densTableSize - 1])
//     //         return 0.0;

//     //     // find segment
//     //     int idx = int(std::upper_bound(altitudes, altitudes + densTableSize, altitudeKm) - altitudes) - 1;
//     //     double h0 = altitudes[idx];
//     //     double rho = densities[idx] * std::exp(-(altitudeKm - h0) / scaleHeights[idx]);
//     //     // if (altitudeKm > 200)
//     //     // {
//     //     //     return rho;
//     //     // }
//     //     // return rho * DENSITY_SCALE;
//     //     return rho;
//     // }

//     // extern "C" {
//     //     #include "nrlmsise-00.h"
//     // }

//     // static double ComputeAtmosphericDensity(double altitudeKm)
//     // {
//     //     // —— 1) Admin / constants —————————————————————
//     //     //  gas constants
//     //     constexpr double M = 28.9644e-3; // mean molecular mass [kg/mol]
//     //     constexpr double R = 8.31432;    // universal gas constant [J/(mol·K)]
//     //     constexpr double g0 = 9.80665;   // sea‑level gravity [m/s²]
//     //     // reference point at 100 km
//     //     constexpr double z0 = 100e3;      // 100 km in m
//     //     constexpr double rho0 = 5.297e-7; // kg/m³ at 100 km (JR reference)

//     //     // —— 2) “Solar / geomagnetic” inputs —————————————
//     //     double F107 = 150.0;     // moderate solar activity
//     //     int Ap = 4;              // very quiet geomagnetic
//     //     double lat = 45.0;       // 45° N, say
//     //     double lon = 0.0;        // longitude [°]
//     //     double utcSec = 43200.0; // seconds past midnight UTC

//     //     // local solar time [h]
//     //     double lst = utcSec / 3600.0 + lon / 15.0;

//     //     // —— 3) Exospheric temperature T∞ [K] ——————————————
//     //     //  JR71: T_inf = 379 + 3.24*(F10.7-70) + 0.25*(Ap-4)
//     //     double Tinf = 379.0 + 3.24 * (F107 - 70.0) + 0.25 * (Ap - 4.0);
//     //     // latitude correction
//     //     Tinf -= 0.06 * lat;
//     //     // diurnal term (peaks ~14 LT)
//     //     Tinf += 0.46 * (Tinf - 273.0) * std::cos((lst - 14.0) * M_PI / 12.0);

//     //     // —— 4) Temperature profile T(z) [K] ——————————————
//     //     double Tz;
//     //     if (altitudeKm <= 100.0)
//     //     {
//     //         // linear ramp from 273 K at 0 km up to T∞ at 100 km
//     //         Tz = 273.0 + (Tinf - 273.0) * (altitudeKm / 100.0);
//     //     }
//     //     else if (altitudeKm < 120.0)
//     //     {
//     //         // smooth exponential approach from 100→120 km
//     //         double Δh = altitudeKm - 100.0;
//     //         Tz = 273.0 + (Tinf - 273.0) * (1.0 - std::exp(-Δh / 50.0));
//     //     }
//     //     else
//     //     {
//     //         Tz = Tinf;
//     //     }

//     //     // —— 5) Scale height H [m] ——————————————————————
//     //     double H = (R * Tz) / (M * g0);

//     //     // —— 6) Barometric density ρ(z) ————————————————
//     //     double z = altitudeKm * 1000.0; // km→m
//     //     // include T‑ratio correction ρ ∝ T0/T(z)
//     //     double rho = rho0 * (273.0 / Tz) * std::exp(-(z - z0) / H);

//     //     return rho;
//     // }

//     // static const int CIRA_N = 11;
//     // static const double ciraAlts[CIRA_N] = {0, 50, 100, 150, 200, 250, 300, 350, 400, 450, 500};
//     // static const double ciraDensitiesEq[CIRA_N] = {
//     //     1.225e+0 * 1e9, 1.027e-3 * 1e9, 5.297e-7 * 1e9,
//     //     2.789e-9 * 1e9, 1.206e-11 * 1e9, 4.110e-14 * 1e9,
//     //     1.004e-16 * 1e9, 1.676e-18 * 1e9, 1.548e-20 * 1e9,
//     //     8.439e-23 * 1e9, 2.180e-25 * 1e9};

//     // // and similarly for 45° and 90° arrays:
//     // static const double ciraDensities45[CIRA_N] = {
//     //     1.200e+0 * 1e9, 0.900e-3 * 1e9, 5.000e-7 * 1e9,
//     //     2.600e-9 * 1e9, 1.100e-11 * 1e9, 4.000e-14 * 1e9,
//     //     9.500e-17 * 1e9, 1.500e-18 * 1e9, 1.400e-20 * 1e9,
//     //     7.500e-23 * 1e9, 2.000e-25 * 1e9};

//     // static const double ciraDensities90[CIRA_N] = {
//     //     1.175e+0 * 1e9, 0.800e-3 * 1e9, 4.500e-7 * 1e9,
//     //     2.400e-9 * 1e9, 1.000e-11 * 1e9, 3.500e-14 * 1e9,
//     //     8.000e-17 * 1e9, 1.200e-18 * 1e9, 1.200e-20 * 1e9,
//     //     6.500e-23 * 1e9, 1.800e-25 * 1e9};

//     // // Bilinear interp: first altitude, then latitude
//     // static double ComputeAtmosphericDensity_CIRA86(double altitudeKm, double latDeg)
//     // {
//     //     // clamp
//     //     altitudeKm = std::clamp(altitudeKm, ciraAlts[0], ciraAlts[CIRA_N - 1]);
//     //     latDeg = std::clamp(latDeg, 0.0, 90.0);

//     //     // 1) Find altitude bracket
//     //     int i = int(std::upper_bound(ciraAlts, ciraAlts + CIRA_N, altitudeKm) - ciraAlts) - 1;
//     //     double z0 = ciraAlts[i], z1 = ciraAlts[i + 1];
//     //     double t = (altitudeKm - z0) / (z1 - z0);

//     //     // 2) Interpolate in each lat band
//     //     double rhoEq = ciraDensitiesEq[i] * (1 - t) + ciraDensitiesEq[i + 1] * t;
//     //     double rho45 = ciraDensities45[i] * (1 - t) + ciraDensities45[i + 1] * t;
//     //     double rho90 = ciraDensities90[i] * (1 - t) + ciraDensities90[i + 1] * t;

//     //     // 3) Now interpolate in latitude between Equator→45° and 45°→90°
//     //     double f = latDeg / 45.0;
//     //     if (f <= 1.0)
//     //     {
//     //         return rhoEq * (1 - f) + rho45 * f;
//     //     }
//     //     else
//     //     {
//     //         double f2 = (latDeg - 45.0) / 45.0;
//     //         return rho45 * (1 - f2) + rho90 * f2;
//     //     }
//     // }

//     // static double ComputeJR_kgm3(double altitudeKm,
//     //                              double F107,
//     //                              double F107avg)
//     // {
//     //     // constants
//     //     constexpr double M = 28.9644e-3;  // kg/mol
//     //     constexpr double R = 8.31432;     // J/(mol·K)
//     //     constexpr double g0 = 9.80665;    // m/s²
//     //     constexpr double z0 = 100e3;      // 100 km in m
//     //     constexpr double rho0 = 5.297e-7; // kg/m³ at 100 km

//     //     // 1) exospheric temperature (JR71)
//     //     double Tinf = 379.0 + 3.24 * (F107 - 70.0) + 0.25 * (F107avg - 70.0);

//     //     // 2) temperature profile T(z)
//     //     double Tz;
//     //     if (altitudeKm <= 100.0)
//     //     {
//     //         Tz = 273.0 + (Tinf - 273.0) * (altitudeKm / 100.0);
//     //     }
//     //     else
//     //     {
//     //         Tz = Tinf;
//     //     }

//     //     // 3) scale height H [m]
//     //     double H = (R * Tz) / (M * g0);

//     //     // 4) barometric density ρ(z) [kg/m³]
//     //     double z_m = altitudeKm * 1000.0;
//     //     return rho0 * (273.0 / Tz) * std::exp(-(z_m - z0) / H) * 1e9;
//     // }

//     // /**
//     //  * Computes drag acceleration based on position, velocity, area, mass, and drag coefficient.
//     //  * Accounts for Earth rotation to calculate relative wind.
//     //  */

//     struct DensityTableInit
//     {
//         DensityTableInit()
//         {
//             for (int i = 0; i < densTableSize - 1; ++i)
//             {
//                 double h0 = altitudes[i];
//                 double h1 = altitudes[i + 1];
//                 double ρ0 = densities[i];
//                 double ρ1 = densities[i + 1];
//                 scaleHeights[i] = -(h1 - h0) / std::log(ρ1 / ρ0);
//             }
//         }
//     } _densityTableInit;

//     // 2) Pure CIRA lookup [kg/km³]:
//     static double ComputeCIRA86(double altitudeKm)
//     {
//         if (altitudeKm <= altitudes[0])
//             return densities[0];
//         if (altitudeKm >= altitudes[densTableSize - 1])
//             return 0.0;
//         int idx = int(std::upper_bound(altitudes, altitudes + densTableSize, altitudeKm) - altitudes) - 1;
//         double h0 = altitudes[idx];
//         double rho = densities[idx] * std::exp(-(altitudeKm - h0) / scaleHeights[idx]);
//         // note: densities[] are *already* in kg/km³ units
//         return rho;
//     }

//     // 3) Smoothed JR barometric [kg/m³]:
//     static double ComputeJR_kgm3(double altitudeKm,
//                                  double F107,    // daily
//                                  double F107avg) // 81‑day avg
//     {
//         constexpr double M = 28.9644e-3;  // kg/mol
//         constexpr double R = 8.31432;     // J/(mol·K)
//         constexpr double g0 = 9.80665;    // m/s²
//         constexpr double z0 = 100e3;      // 100 km in m
//         constexpr double rho0 = 5.297e-7; // kg/m³ @100 km

//         // 1) exosphere T∞
//         double Tinf = 379.0 + 3.24 * (F107 - 70.0) + 0.25 * (F107avg - 70.0);

//         // 2) smooth T(z) 0→100, 100→120, >120
//         double Tz;
//         if (altitudeKm <= 100.0)
//         {
//             Tz = 273.0 + (Tinf - 273.0) * (altitudeKm / 100.0);
//         }
//         else if (altitudeKm < 120.0)
//         {
//             double dz = altitudeKm - 100.0;
//             Tz = 273.0 + (Tinf - 273.0) * (1 - std::exp(-dz / 50.0));
//         }
//         else
//         {
//             Tz = Tinf;
//         }

//         // 3) barometric scale height [m]
//         double H = (R * Tz) / (M * g0);

//         // 4) density [kg/m³]
//         double z_m = altitudeKm * 1000.0;
//         return rho0 * (273.0 / Tz) * std::exp(-(z_m - z0) / H);
//     }

//     // 4) Blended wrapper → kg/km³
//     static double ComputeAtmosphericDensity(const Vector3d &posRel,
//                                             double F107,
//                                             double F107avg)
//     {
//         // a) sim‑units → km
//         double xkm = posRel.x * UNIT_TO_KM;
//         double ykm = posRel.y * UNIT_TO_KM;
//         double zkm = posRel.z * UNIT_TO_KM;

//         // b) altitude above Earth [km]
//         double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);
//         double alt = std::max(0.0, rkm - EARTH_RADIUS_KM);

//         // c) choose CIRA86 or JR
//         if (alt < 200.0)
//         {
//             // CIRA densities[] are already in kg/km³
//             return ComputeCIRA86(alt) * DENSITY_SCALE;
//         }
//         else
//         {
//             // JR gives kg/m³ → convert to kg/km³
//             double rho_m3 = ComputeJR_kgm3(alt, F107, F107avg);
//             return rho_m3 * 1e9;
//         }
//     }

//     static Vector3d ComputeDragAcceleration(
//         const Vector3d &velUU,
//         const Vector3d &posRelUU,
//         double mass,
//         double areaUU,
//         double Cd)
//     {
//         double xkm = posRelUU.x * UNIT_TO_KM;
//         double ykm = posRelUU.y * UNIT_TO_KM;
//         double zkm = posRelUU.z * UNIT_TO_KM;
//         double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);
//         double alt = std::max(0.0, rkm - EARTH_RADIUS_KM);

//         double rho = ComputeJR_kgm3(alt, 150.0, 150.0);
//         if (rho < 1e-12)
//             return {0, 0, 0};

//         Vector3d vkm = {velUU.x * UNIT_TO_KM,
//                         velUU.y * UNIT_TO_KM,
//                         velUU.z * UNIT_TO_KM};

//         Vector3d vatm = {-OMEGA_EARTH * ykm,
//                          OMEGA_EARTH * xkm,
//                          0.0};

//         Vector3d vrel = {vkm.x - vatm.x,
//                          vkm.y - vatm.y,
//                          vkm.z - vatm.z};
//         double speed = std::sqrt(vrel.x * vrel.x + vrel.y * vrel.y + vrel.z * vrel.z);
//         if (speed < 1e-6)
//             return {0, 0, 0};

//         double areaKm2 = areaUU * UNIT_TO_KM * UNIT_TO_KM;
//         double factor = -0.5 * Cd * areaKm2 * rho / mass;
//         Vector3d a = {factor * vrel.x * speed,
//                       factor * vrel.y * speed,
//                       factor * vrel.z * speed};

//         return {a.x / UNIT_TO_KM,
//                 a.y / UNIT_TO_KM,
//                 a.z / UNIT_TO_KM};
//     }

//     /**
//      * Computes gravitational acceleration on a body due to all other bodies in the system.
//      * Caps force to prevent simulation blowups.
//      */
//     Vector3d ComputeAcceleration(Vector3d pos, double *masses, Vector3d *bodies, int n)
//     {
//         Vector3d a{0, 0, 0};
//         for (int i = 0; i < n; i++)
//         {
//             Vector3d d{bodies[i].x - pos.x,
//                        bodies[i].y - pos.y,
//                        bodies[i].z - pos.z};
//             double r2 = d.x * d.x + d.y * d.y + d.z * d.z;
//             if (r2 < minDistSq)
//                 continue;
//             double F = std::min(G * masses[i] / r2, maxForce);
//             double r = std::sqrt(r2);
//             double f_r = F / r;
//             a.x += f_r * d.x;
//             a.y += f_r * d.y;
//             a.z += f_r * d.z;
//         }
//         return a;
//     }

//     static const double c_dp[7] = {0.0, 1. / 5, 3. / 10, 4. / 5, 8. / 9, 1.0, 1.0};
//     static const double a_dp[7][6] = {
//         {}, {1. / 5}, {3. / 40, 9. / 40}, {44. / 45, -56. / 15, 32. / 9}, {19372. / 6561, -25360. / 2187, 64448. / 6561, -212. / 729}, {9017. / 3168, -355. / 33, 46732. / 5247, 49. / 176, -5103. / 18656}, {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84}};
//     static const double b_dp[7] = {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84, 0};

//     /**
//      * Dormand-Prince 5th order Runge-Kutta integrator step for a single object.
//      * Includes gravity, thrust, and atmospheric drag.
//      *
//      * @param pos Position (updated in-place)
//      * @param vel Velocity (updated in-place)
//      * @param mass Object mass
//      * @param dt Timestep
//      * @param bodies Array of body positions (first one is assumed to be Earth)
//      * @param masses Array of body masses
//      * @param n Number of bodies
//      * @param thrustAcc Acceleration due to applied thrust
//      * @param dragCoeff Drag coefficient
//      * @param areaUU Cross-sectional area (in sim units^2)
//      */
//     static void DormandPrinceStep(
//         Vector3d &pos,
//         Vector3d &vel,
//         double mass,
//         double dt,
//         const Vector3d *bodies,
//         const double *masses,
//         int n,
//         Vector3d thrustAcc,
//         double dragCoeff,
//         double areaUU)
//     {
//         if (mass <= 1e-6)
//             return;

//         Vector3d kx[7], kv[7];

//         kx[0] = vel;
//         kv[0] = ComputeAcceleration(pos, (double *)masses, (Vector3d *)bodies, n);
//         kv[0].x += thrustAcc.x;
//         kv[0].y += thrustAcc.y;
//         kv[0].z += thrustAcc.z;

//         Vector3d drag1 = ComputeDragAcceleration(vel, {pos.x - bodies[0].x, pos.y - bodies[0].y, pos.z - bodies[0].z}, mass, areaUU, dragCoeff);
//         kv[0].x += drag1.x;
//         kv[0].y += drag1.y;
//         kv[0].z += drag1.z;

//         for (int i = 1; i < 7; i++)
//         {
//             Vector3d pi = pos, vi = vel;
//             for (int j = 0; j < i; j++)
//             {
//                 pi.x += dt * a_dp[i][j] * kx[j].x;
//                 pi.y += dt * a_dp[i][j] * kx[j].y;
//                 pi.z += dt * a_dp[i][j] * kx[j].z;
//                 vi.x += dt * a_dp[i][j] * kv[j].x;
//                 vi.y += dt * a_dp[i][j] * kv[j].y;
//                 vi.z += dt * a_dp[i][j] * kv[j].z;
//             }
//             kx[i] = vi;
//             kv[i] = ComputeAcceleration(pi, (double *)masses, (Vector3d *)bodies, n);
//             kv[i].x += thrustAcc.x;
//             kv[i].y += thrustAcc.y;
//             kv[i].z += thrustAcc.z;

//             Vector3d relPos = {pi.x - bodies[0].x,
//                                pi.y - bodies[0].y,
//                                pi.z - bodies[0].z};
//             Vector3d drag_i = ComputeDragAcceleration(vi, relPos, mass, areaUU, dragCoeff);
//             kv[i].x += drag_i.x;
//             kv[i].y += drag_i.y;
//             kv[i].z += drag_i.z;
//         }

//         for (int i = 0; i < 7; i++)
//         {
//             pos.x += dt * b_dp[i] * kx[i].x;
//             pos.y += dt * b_dp[i] * kx[i].y;
//             pos.z += dt * b_dp[i] * kx[i].z;
//             vel.x += dt * b_dp[i] * kv[i].x;
//             vel.y += dt * b_dp[i] * kv[i].y;
//             vel.z += dt * b_dp[i] * kv[i].z;
//         }
//     }

//     /**
//      * Public C-style entry point to integrate position and velocity of a single body.
//      * Converts inputs to double precision, runs the integrator, and converts back.
//      *
//      * @param position Input/output position
//      * @param velocity Input/output velocity
//      * @param mass Mass of the object
//      * @param bodies Array of other bodies (assumes 256 max)
//      * @param masses Array of their masses
//      * @param numBodies Number of other bodies
//      * @param dt Timestep
//      * @param thrustImpulse Thrust impulse applied this step (force * dt)
//      * @param dragCoeff Drag coefficient
//      * @param areaUU Area in simulation units^2
//      */
//     extern "C" __attribute__((visibility("default"))) void DormandPrinceSingle(
//         double3 *position,
//         double3 *velocity,
//         double mass,
//         Vector3 *bodies,
//         double *masses,
//         int numBodies,
//         float dt,
//         Vector3 thrustImpulse,
//         float dragCoeff,
//         float areaUU)
//     {
//         if (mass <= 1e-6f)
//             return;

//         Vector3d posD = ToVector3dFromDouble3(*position);
//         Vector3d velD = ToVector3dFromDouble3(*velocity);

//         Vector3d bodiesD[256];
//         // double massesD[256];
//         for (int i = 0; i < numBodies; i++)
//         {
//             bodiesD[i] = ToVector3dFromVector3(bodies[i]);
//             // massesD[i] = (double)masses[i];
//         }

//         Vector3d th{thrustImpulse.x / mass, thrustImpulse.y / mass, thrustImpulse.z / mass};

//         DormandPrinceStep(
//             posD, velD,
//             (double)mass,
//             (double)dt,
//             bodiesD, masses, numBodies,
//             th,
//             (double)dragCoeff,
//             (double)areaUU);

//         *position = ToDouble3(posD);
//         *velocity = ToDouble3(velD);
//     }
// }