#include <cmath>
#include <algorithm>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <vector> // NEW
#include <cstdint>

#if defined(_WIN32)
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

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

    // inline Vector3d ToVector3dFromVector3(const Vector3 &v) { return {v.x, v.y, v.z}; }
    inline Vector3d ToVector3dFromDouble3(const double3 &v) { return {v.x, v.y, v.z}; }
    inline double3 ToDouble3(const Vector3d &v) { return {v.x, v.y, v.z}; }

    void LogDebug(const std::string &msg)
    {
        std::ofstream log("physics_debug.log", std::ios::app);
        log << msg << std::endl;
    }

    // ---- Constants / Units ------------------------------------------------------
    // 1 Unity unit = 10 km
    const double G = 6.67430e-23; // matches your Unity-side G
    const double UNIT_TO_KM = 10.0;
    const double EARTH_RADIUS_KM = 6378.137;   // WGS-84 mean equatorial radius
    const double OMEGA_EARTH = 7.2921150e-5;   // rad/s, Earth rotation
    static const double DRAG_CUTOFF_KM = 80.0; // turn off drag below this altitude

    // --- Jacchia/Roberts-like table (10 km bins up to 500 km) ---
    static const int JR_N = 51;
    static const double JR_ALT[JR_N] = {
        0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140,
        150, 160, 170, 180, 190, 200, 210, 220, 230, 240,
        250, 260, 270, 280, 290, 300, 310, 320, 330, 340,
        350, 360, 370, 380, 390, 400, 410, 420, 430, 440,
        450, 460, 470, 480, 490, 500};
    // Units: kg / km^3  (not SI kg/m^3)
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
    static double JR_H[JR_N - 1];

    struct JRInit
    {
        JRInit()
        {
            for (int i = 0; i < JR_N - 1; ++i)
            {
                const double dh = JR_ALT[i + 1] - JR_ALT[i];         // 10 km
                JR_H[i] = -dh / std::log(JR_RHO[i + 1] / JR_RHO[i]); // km
            }
        }
    } _jrInit;

    // Log-linear interpolation across each 10 km slab.
    // Returns density in kg / km^3 (consistent with v in km/s and A in km^2).
    static inline double DensityAtKm(double altKm)
    {
        if (altKm <= JR_ALT[0])
            return JR_RHO[0];
        if (altKm >= JR_ALT[JR_N - 1])
            return 0.0;

        const int idx = std::min(int(altKm / 10.0), JR_N - 2);
        const double dH = altKm - JR_ALT[idx];
        const double rho = JR_RHO[idx] * std::exp(-dH / JR_H[idx]);
        // avoid underflow; tiny density gives negligible drag anyway
        return (rho < 1e-15) ? 0.0 : rho;
    }

    static Vector3d ComputeDragAcceleration(
        const Vector3d &velUU,    // Unity units / s (1u = 10 km)
        const Vector3d &posRelUU, // Unity units, relative to Earth@origin
        double mass,              // kg
        double areaUU,            // Unity units^2  (1u^2 = 100 km^2)
        double Cd)
    {
        const double xkm = posRelUU.x * UNIT_TO_KM;
        const double ykm = posRelUU.y * UNIT_TO_KM;
        const double zkm = posRelUU.z * UNIT_TO_KM;
        const double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);

        // NOTE: don't clamp with std::max here
        const double alt = rkm - EARTH_RADIUS_KM;

        // hard cutoff: no drag below 60 km
        if (alt <= DRAG_CUTOFF_KM)
            return {0, 0, 0};

        const double rho = DensityAtKm(alt); // as-is
        if (rho <= 0.0)
            return {0, 0, 0};

        // Convert velocity to km/s and subtract atmospheric co-rotation
        const Vector3d vkm = {velUU.x * UNIT_TO_KM, velUU.y * UNIT_TO_KM, velUU.z * UNIT_TO_KM};
        const Vector3d vatm = {-OMEGA_EARTH * ykm, OMEGA_EARTH * xkm, 0.0}; // km/s
        const Vector3d vrel = {vkm.x - vatm.x, vkm.y - vatm.y, vkm.z - vatm.z};

        const double speed = std::sqrt(vrel.x * vrel.x + vrel.y * vrel.y + vrel.z * vrel.z);
        if (speed < 1e-6)
            return {0, 0, 0};

        // areaUU is in u^2; convert to km^2
        const double A_km2 = areaUU * UNIT_TO_KM * UNIT_TO_KM;

        // a[km/s^2] = -0.5 * Cd * A * rho / m * v * |v|
        const double factor = -0.5 * Cd * A_km2 * rho / mass;
        const Vector3d a_km = {factor * vrel.x * speed, factor * vrel.y * speed, factor * vrel.z * speed};

        // Return in Unity units (u/s^2) -> divide by 10
        return {a_km.x / UNIT_TO_KM, a_km.y / UNIT_TO_KM, a_km.z / UNIT_TO_KM};
    }

    // DP5 coefficients (unchanged)
    static const double c_dp[7] = {0.0, 1. / 5, 3. / 10, 4. / 5, 8. / 9, 1.0, 1.0};
    static const double a_dp[7][6] = {
        {}, {1. / 5}, {3. / 40, 9. / 40}, {44. / 45, -56. / 15, 32. / 9}, {19372. / 6561, -25360. / 2187, 64448. / 6561, -212. / 729}, {9017. / 3168, -355. / 33, 46732. / 5247, 49. / 176, -5103. / 18656}, {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84}};
    static const double b_dp[7] = {35. / 384, 0, 500. / 1113, 125. / 192, -2187. / 6784, 11. / 84, 0};

#ifndef NORMAL_IS_RXV
#define NORMAL_IS_RXV 1
#endif

    static inline void Cross(const Vector3d &a, const Vector3d &b, Vector3d &out)
    {
        out.x = a.y * b.z - a.z * b.y;
        out.y = a.z * b.x - a.x * b.z;
        out.z = a.x * b.y - a.y * b.x;
    }

    static inline double Dot(const Vector3d &a, const Vector3d &b)
    {
        return a.x * b.x + a.y * b.y + a.z * b.z;
    }

    static inline bool NormalHat(const Vector3d &r, const Vector3d &v, Vector3d &nHatOut)
    {
        Vector3d n;
#if NORMAL_IS_RXV
        Cross(r, v, n); // h = r × v  (typical)
#else
        Cross(v, r, n); // h = v × r  (if that’s your convention)
#endif
        const double n2 = Dot(n, n);
        if (n2 <= 1e-30)
            return false;
        const double invn = 1.0 / std::sqrt(n2);
        nHatOut = {n.x * invn, n.y * invn, n.z * invn};
        return true;
    }

    // NEW: returns +1 for prograde (incl <= 90°), -1 for retrograde (incl > 90°)
    // Reference "up" is +Y.
    static inline double OrbitParityY(const Vector3d &r, const Vector3d &v)
    {
        Vector3d nHat;
        if (!NormalHat(r, v, nHat))
            return +1.0;
        // cos(incl) = n̂ · k, with k = +Y = (0,1,0)
        const double cosi = nHat.y;
        return (cosi >= 0.0) ? +1.0 : -1.0;
    }

    extern "C" EXPORT void BatchTwoBodyIntegrateMuEx(
        double3 *positions,
        double3 *velocities,
        const double *masses,
        const Vector3 *thrusts,
        const float *dragCoeffs,
        const float *areasUU,
        const int8_t *normalSign,   // 0=free, +1=Normal, -1=AntiNormal
        const uint8_t *isThrusting, // NEW: 0/1 per body
        int8_t *latchedParityIO,    // NEW: in/out per body: 0=no latch, +1/-1 latched parity
        int count,
        double mu,
        float totalDt,
        int substeps)
    {
        if (count <= 0)
            return;
        if (substeps < 1)
            substeps = 1;

        const double dt = static_cast<double>(totalDt) / static_cast<double>(substeps);

        auto gravA = [&](const Vector3d &r) -> Vector3d
        {
            const double r2 = r.x * r.x + r.y * r.y + r.z * r.z;
            if (r2 < 1e-20)
                return Vector3d{0, 0, 0};
            const double invR = 1.0 / std::sqrt(r2);
            const double invR3 = invR * invR * invR;
            const double s = -mu * invR3;
            return Vector3d{s * r.x, s * r.y, s * r.z};
        };

        // #pragma omp parallel for
        for (int i = 0; i < count; ++i)
        {
            const double msc = masses[i];
            if (msc <= 1e-6)
                continue;

            Vector3d pos = ToVector3dFromDouble3(positions[i]);
            Vector3d vel = ToVector3dFromDouble3(velocities[i]);

            const Vector3 Ti = thrusts[i];
            const Vector3d thBase = {Ti.x / msc, Ti.y / msc, Ti.z / msc};
            const double thMag = std::sqrt(thBase.x * thBase.x + thBase.y * thBase.y + thBase.z * thBase.z);

            const double Cd = static_cast<double>(dragCoeffs[i]);
            const double Auu = static_cast<double>(areasUU[i]);
            const int8_t flag = normalSign ? normalSign[i] : 0; // 0 = free, ±1 = lateral shaping
            const bool lateralMode = (flag != 0);

            for (int s = 0; s < substeps; ++s)
            {
                Vector3d kx[7], kv[7];

                // ---- Stage 0 ----
                kx[0] = vel;
                kv[0] = gravA(pos);

                if (!lateralMode)
                {
                    // Free thrust: use the acceleration from the thrust vector as-is.
                    kv[0].x += thBase.x;
                    kv[0].y += thBase.y;
                    kv[0].z += thBase.z;
                }
                else
                {
                    // Lateral mode (Normal/AntiNormal): project onto instantaneous orbital normal
                    Vector3d nHat;
                    if (NormalHat(pos, vel, nHat) && thMag > 0.0)
                    {
                        // Use the ship's actual thrust vector to pick the side:
                        // side = sign(thBase ⋅ nHat)
                        const double dotTN =
                            thBase.x * nHat.x +
                            thBase.y * nHat.y +
                            thBase.z * nHat.z;

                        if (std::fabs(dotTN) > 1e-12)
                        {
                            const double sgn = (dotTN >= 0.0) ? +1.0 : -1.0;
                            const double mag = thMag * sgn;

                            kv[0].x += nHat.x * mag;
                            kv[0].y += nHat.y * mag;
                            kv[0].z += nHat.z * mag;
                        }
                    }
                    // else: degenerate normal => no lateral thrust this substep
                }

                // drag at stage 0
                // {
                //     Vector3d d0 = ComputeDragAcceleration(vel, pos, msc, Auu, Cd);
                //     kv[0].x += d0.x;
                //     kv[0].y += d0.y;
                //     kv[0].z += d0.z;
                // }

                // ---- Stages 1..6 ----
                for (int st = 1; st < 7; ++st)
                {
                    Vector3d pi = pos, vi = vel;
                    for (int j = 0; j < st; ++j)
                    {
                        pi.x += dt * a_dp[st][j] * kx[j].x;
                        pi.y += dt * a_dp[st][j] * kx[j].y;
                        pi.z += dt * a_dp[st][j] * kx[j].z;

                        vi.x += dt * a_dp[st][j] * kv[j].x;
                        vi.y += dt * a_dp[st][j] * kv[j].y;
                        vi.z += dt * a_dp[st][j] * kv[j].z;
                    }

                    kx[st] = vi;
                    kv[st] = gravA(pi);

                    if (!lateralMode)
                    {
                        kv[st].x += thBase.x;
                        kv[st].y += thBase.y;
                        kv[st].z += thBase.z;
                    }
                    else
                    {
                        Vector3d nHat;
                        if (NormalHat(pi, vi, nHat) && thMag > 0.0)
                        {
                            const double dotTN =
                                thBase.x * nHat.x +
                                thBase.y * nHat.y +
                                thBase.z * nHat.z;

                            if (std::fabs(dotTN) > 1e-12)
                            {
                                const double sgn = (dotTN >= 0.0) ? +1.0 : -1.0;
                                const double mag = thMag * sgn;

                                kv[st].x += nHat.x * mag;
                                kv[st].y += nHat.y * mag;
                                kv[st].z += nHat.z * mag;
                            }
                        }
                    }

                    // Vector3d ds = ComputeDragAcceleration(vi, pi, msc, Auu, Cd);
                    // kv[st].x += ds.x;
                    // kv[st].y += ds.y;
                    // kv[st].z += ds.z;
                }

                // ---- Accumulate this substep ----
                for (int st = 0; st < 7; ++st)
                {
                    pos.x += dt * b_dp[st] * kx[st].x;
                    pos.y += dt * b_dp[st] * kx[st].y;
                    pos.z += dt * b_dp[st] * kx[st].z;

                    vel.x += dt * b_dp[st] * kv[st].x;
                    vel.y += dt * b_dp[st] * kv[st].y;
                    vel.z += dt * b_dp[st] * kv[st].z;
                }
            }

            // if (latchedParityIO)
            //     latchedParityIO[i] = latched;

            positions[i] = ToDouble3(pos);
            velocities[i] = ToDouble3(vel);
        }
    }
}
