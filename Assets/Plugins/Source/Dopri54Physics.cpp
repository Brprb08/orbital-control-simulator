#include <cmath>
#include <algorithm>
#include <fstream>
#include <sstream>
#include <iomanip>

extern "C"
{
    /**
     * @struct Vector3
     * @brief 3D vector using single-precision floats.
     */
    struct Vector3
    {
        float x, y, z;
    };

    /**
     * @struct Vector3d
     * @brief 3D vector using double-precision floats.
     */
    struct Vector3d
    {
        double x, y, z;
    };

    /**
     * @struct double3
     * @brief Unity-compatible 3D double-precision vector.
     */
    struct double3
    {
        double x, y, z;
    };

    /** Converts Vector3 to Vector3d */
    inline Vector3d ToVector3dFromVector3(const Vector3 &v) { return {v.x, v.y, v.z}; }

    /** Converts double3 to Vector3d */
    inline Vector3d ToVector3dFromDouble3(const double3 &v) { return {v.x, v.y, v.z}; }

    /** Converts Vector3d to double3 */
    inline double3 ToDouble3(const Vector3d &v) { return {v.x, v.y, v.z}; }

    /**
     * @brief Appends a message to the debug log file.
     * @param msg Message to write.
     */
    void LogDebug(const std::string &msg)
    {
        std::ofstream log("physics_debug.log", std::ios::app);
        log << msg << std::endl;
    }

    // Constants
    const double G = 6.67430e-23;                ///< Gravitational constant (scaled for sim units).
    const double minDistSq = 1e-20;              ///< Minimum distance squared to avoid singularities.
    const double maxForce = 1;                   ///< Cap on maximum gravitational force per object.
    const double UNIT_TO_KM = 10.0;              ///< Unit conversion: 1 sim unit = 10 km.
    const double EARTH_RADIUS_KM = 637.8 * 10.0; ///< Earth's radius in sim units.
    const double OMEGA_EARTH = 7.2921150e-5;     ///< Earth's angular velocity (rad/s).
    const double DENSITY_SCALE = 1.0;            ///< Global scaling for atmosphere density.

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

    // Original JR_RHO multiplied by 1000×
    static const double JR_RHO[JR_N] = {
        1.35e9, 4.56e8, 9.82e7, 2.05e7, 4.46e6,
        1.15e6, 3.48e5, 9.11e4, 2.06e4, 3.81e3,
        725.0, 267.0, 107.0, 51.0, 10.0,
        1.95, 1.15, 0.68, 0.40, 0.24,
        0.135, 0.090, 0.056, 0.035, 0.022,
        0.187, 0.1459, 0.1136, 0.0885, 0.0689,
        0.0537, 0.0418, 0.0326, 0.0254, 0.0198,
        0.0154, 0.0120, 0.00938, 0.0073, 0.00568,
        0.00487, 0.00378, 0.00292, 0.00232, 0.00197,
        0.00168, 0.00138, 0.00106, 0.000803, 0.000622,
        0.000485};

    // will hold scale heights
    static double JR_H[JR_N - 1];

    /**
     * @brief Initializes JR_H based on JR_ALT and JR_RHO.
     */
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
     * @brief Computes atmospheric density at a given altitude using exponential interpolation.
     * @param altKm Altitude in kilometers.
     * @return Density in kg/km³.
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
        if (altKm < 130)
        {
            double rho = JR_RHO[idx] * std::exp(-dH / JR_H[idx]);
            return std::min(rho, 1e4);
        }
        return JR_RHO[idx] * std::exp(-dH / JR_H[idx]) * DENSITY_SCALE;
    }

    /**
     * @brief Calculates drag acceleration on a body, accounting for Earth’s rotation.
     * @param velUU Velocity in sim units.
     * @param posRelUU Position relative to Earth's center (sim units).
     * @param mass Object mass.
     * @param areaUU Cross-sectional area in sim units².
     * @param Cd Drag coefficient.
     * @return Acceleration vector due to drag.
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

        // if (alt < 100)
        // {
        //     LogDebug(
        //         "drag @ alt=" + std::to_string(alt) + " km" + "  rho=" + std::to_string(rho) + " kg/km³  a=" + std::to_string(a.x * UNIT_TO_KM) + "," + std::to_string(a.y * UNIT_TO_KM) + "," + std::to_string(a.z * UNIT_TO_KM) + " km/s²");
        // }

        return {a.x / UNIT_TO_KM, a.y / UNIT_TO_KM, a.z / UNIT_TO_KM};
    }

    /**
     * @brief Computes gravitational acceleration from multiple bodies.
     * @param pos Current position of the body.
     * @param masses Array of body masses.
     * @param bodies Array of body positions.
     * @param n Number of other bodies.
     * @param mass Mass of the target body.
     * @return Acceleration vector.
     */
    Vector3d ComputeAcceleration(Vector3d pos, double *masses, Vector3d *bodies, int n, double mass)
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

            // double F = std::min(G * masses[i] / r2, maxForce);
            double rawF = G * masses[i] / r2;
            if (rawF >= maxForce)
            {
                LogDebug("Gravity force capped: " + std::to_string(rawF));
            }
            double F = std::min(rawF, maxForce);
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
     * @brief Performs one integration step using the Dormand-Prince 5th order Runge-Kutta method.
     * @param pos Position (input/output).
     * @param vel Velocity (input/output).
     * @param mass Mass of the object.
     * @param dt Timestep.
     * @param bodies Array of body positions.
     * @param masses Array of body masses.
     * @param n Number of bodies.
     * @param thrustAcc Thrust acceleration vector.
     * @param dragCoeff Drag coefficient.
     * @param areaUU Cross-sectional area in sim units.
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
        kv[0] = ComputeAcceleration(pos, (double *)masses, (Vector3d *)bodies, n, mass);
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
            kv[i] = ComputeAcceleration(pi, (double *)masses, (Vector3d *)bodies, n, mass);
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
     * @brief Public C-callable wrapper for DormandPrinceStep.
     * Converts to/from float/double types and Unity-compatible structs.
     *
     * @param position Pointer to position vector (in/out).
     * @param velocity Pointer to velocity vector (in/out).
     * @param mass Mass of the object.
     * @param bodies Array of other body positions (float).
     * @param masses Array of other body masses.
     * @param numBodies Number of other bodies.
     * @param dt Timestep in seconds.
     * @param thrustImpulse Impulse applied (force * dt).
     * @param dragCoeff Drag coefficient.
     * @param areaUU Cross-sectional area (sim units²).
     */
    extern "C" __attribute__((visibility("default"))) void DormandPrinceSingle(
        double3 *position,
        double3 *velocity,
        double mass,
        Vector3 *bodies,
        double *masses,
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
            massesD[i] = masses[i];
        }

        Vector3d th{thrustImpulse.x / mass, thrustImpulse.y / mass, thrustImpulse.z / mass};

        DormandPrinceStep(
            posD, velD,
            mass,
            (double)dt,
            bodiesD, massesD, numBodies,
            th,
            (double)dragCoeff,
            (double)areaUU);

        *position = ToDouble3(posD);
        *velocity = ToDouble3(velD);
    }
}