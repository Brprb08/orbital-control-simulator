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

    static const double DENSITY_SCALE = 2;

    static const int densTableSize = 51;

    /**
     * Altitudes in km for the atmospheric model.
     */
    static const double altitudes[densTableSize] = {
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

    /**
     * Atmospheric densities at corresponding altitudes.
     * Values are scaled to simulation mass units.
     */
    static const double densities[densTableSize] = {
        1.225e9, 4.135e8, 8.891e7, 1.841e7, 3.996e6,
        1.027e6, 3.097e5, 8.283e4, 1.846e4, 3.416e3,
        650.0, 240.0, 96.0, 46.0, 22.0,
        1.78, 1.05, 0.62, 0.36, 0.21,
        0.12, 0.080, 0.050, 0.031, 0.020,
        0.170326, 0.132650, 0.103308, 0.080456, 0.062660,
        0.048799, 0.038005, 0.029598, 0.023051, 0.017952,
        0.013981, 0.010889, 0.008480, 0.006604, 0.005143,
        0.004006, 0.003120, 0.002430, 0.001892, 0.001474,
        0.001148, 0.000894, 0.000696, 0.000542, 0.000422,
        0.000329};

    static double scaleHeights[densTableSize - 1];

    /**
     * Initializes scale heights between atmospheric layers using log scale.
     */
    struct DensityTableInit
    {
        DensityTableInit()
        {
            for (int i = 0; i < densTableSize - 1; ++i)
            {
                double h0 = altitudes[i], h1 = altitudes[i + 1];
                double rho0 = densities[i], rho1 = densities[i + 1];
                scaleHeights[i] = -(h1 - h0) / std::log(rho1 / rho0);
            }
        }
    } _densityTableInit;

    /**
     * Gets atmospheric density (in simulation mass units) at a given altitude in km.
     * Applies DENSITY_SCALE below 200km to account for missing lower-atmosphere effects.
     */
    static double ComputeAtmosphericDensity(double altitudeKm)
    {
        if (altitudeKm <= altitudes[0])
            return densities[0];
        if (altitudeKm >= altitudes[densTableSize - 1])
            return 0.0;

        // find segment
        int idx = int(std::upper_bound(altitudes, altitudes + densTableSize, altitudeKm) - altitudes) - 1;
        double h0 = altitudes[idx];
        double rho = densities[idx] * std::exp(-(altitudeKm - h0) / scaleHeights[idx]);
        if (altitudeKm > 200)
        {
            return rho;
        }
        return rho * DENSITY_SCALE;
    }

    /**
     * Computes drag acceleration based on position, velocity, area, mass, and drag coefficient.
     * Accounts for Earth rotation to calculate relative wind.
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

        Vector3d vkm = {velUU.x * UNIT_TO_KM,
                        velUU.y * UNIT_TO_KM,
                        velUU.z * UNIT_TO_KM};

        Vector3d vatm = {-OMEGA_EARTH * ykm,
                         OMEGA_EARTH * xkm,
                         0.0};

        Vector3d vrel = {vkm.x - vatm.x,
                         vkm.y - vatm.y,
                         vkm.z - vatm.z};
        double speed = std::sqrt(vrel.x * vrel.x + vrel.y * vrel.y + vrel.z * vrel.z);
        if (speed < 1e-6)
            return {0, 0, 0};

        double areaKm2 = areaUU * UNIT_TO_KM * UNIT_TO_KM;
        double factor = -0.5 * Cd * areaKm2 * rho / mass;
        Vector3d a = {factor * vrel.x * speed,
                      factor * vrel.y * speed,
                      factor * vrel.z * speed};

        return {a.x / UNIT_TO_KM,
                a.y / UNIT_TO_KM,
                a.z / UNIT_TO_KM};
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