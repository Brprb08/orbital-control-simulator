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
    const double EARTH_RADIUS_KM = 6378.137; // WGS-84 mean equatorial radius
    const double OMEGA_EARTH = 7.2921150e-5; // rad/s, Earth rotation

    // U.S. Standard Atmosphere 1976, geometric altitude, 5 km knots.
    // Source: PDAS Table 1, https://www.pdas.com/bigtables.html (2026-09-14).
    // See ATMOSPHERE_MODEL.md for provenance, interpolation, and validity limits.
    // This is a fixed reference atmosphere, not a Jacchia-Roberts model.
    static constexpr int ATMOSPHERE_COUNT = 201;
    static constexpr double ATMOSPHERE_STEP_KM = 5.0;
    static constexpr double ATMOSPHERE_REFERENCE_TOP_KM = 1000.0;
    static constexpr double DRAG_CUTOFF_KM = 1100.0;
    // kg/km^3; reference source uses kg/m^3, converted once here by 1e9.
    static const double ATMOSPHERE_RHO[ATMOSPHERE_COUNT] = {
        1.22500000e+09, 7.36430000e+08, 4.13510000e+08, 1.94760000e+08, 8.89100000e+07,
        4.00840000e+07, 1.84100000e+07, 8.46340000e+06, 3.99570000e+06, 1.96630000e+06,
        1.02690000e+06, 5.68100000e+05, 3.09680000e+05, 1.63210000e+05, 8.28290000e+04,
        3.99210000e+04, 1.84580000e+04, 8.21950000e+03, 3.44000000e+03, 1.38730000e+03,
        5.60440000e+02, 2.33250000e+02, 9.67340000e+01, 4.27940000e+01, 2.21990000e+01,
        1.29180000e+01, 8.14940000e+00, 5.46470000e+00, 3.83130000e+00, 2.78050000e+00,
        2.07520000e+00, 1.58480000e+00, 1.23360000e+00, 9.75260000e-01, 7.81550000e-01,
        6.33820000e-01, 5.19400000e-01, 4.29520000e-01, 3.58070000e-01, 3.00640000e-01,
        2.54070000e-01, 2.15960000e-01, 1.84560000e-01, 1.58490000e-01, 1.36710000e-01,
        1.18390000e-01, 1.02900000e-01, 8.97570000e-02, 7.85500000e-02, 6.89540000e-02,
        6.07060000e-02, 5.35870000e-02, 4.74200000e-02, 4.20580000e-02, 3.73820000e-02,
        3.32940000e-02, 2.97100000e-02, 2.65600000e-02, 2.37830000e-02, 2.13310000e-02,
        1.91590000e-02, 1.72320000e-02, 1.55190000e-02, 1.39940000e-02, 1.26340000e-02,
        1.14190000e-02, 1.03330000e-02, 9.36070000e-03, 8.48860000e-03, 7.70540000e-03,
        7.00110000e-03, 6.36700000e-03, 5.79540000e-03, 5.27950000e-03, 4.81320000e-03,
        4.39140000e-03, 4.00930000e-03, 3.66290000e-03, 3.34840000e-03, 3.06270000e-03,
        2.80280000e-03, 2.56620000e-03, 2.35070000e-03, 2.15430000e-03, 1.97520000e-03,
        1.81180000e-03, 1.66260000e-03, 1.52650000e-03, 1.40200000e-03, 1.28830000e-03,
        1.18430000e-03, 1.08910000e-03, 1.00200000e-03, 9.22220000e-04, 8.49130000e-04,
        7.82140000e-04, 7.20700000e-04, 6.64340000e-04, 6.12610000e-04, 5.65110000e-04,
        5.21480000e-04, 4.81390000e-04, 4.44540000e-04, 4.10650000e-04, 3.79490000e-04,
        3.50830000e-04, 3.24460000e-04, 3.00190000e-04, 2.77850000e-04, 2.57270000e-04,
        2.38320000e-04, 2.20860000e-04, 2.04770000e-04, 1.89930000e-04, 1.76250000e-04,
        1.63630000e-04, 1.51990000e-04, 1.41240000e-04, 1.31310000e-04, 1.22140000e-04,
        1.13670000e-04, 1.05840000e-04, 9.85970000e-05, 9.19030000e-05, 8.57100000e-05,
        7.99810000e-05, 7.46780000e-05, 6.97700000e-05, 6.52250000e-05, 6.10150000e-05,
        5.71140000e-05, 5.34990000e-05, 5.01470000e-05, 4.70380000e-05, 4.41540000e-05,
        4.14780000e-05, 3.89930000e-05, 3.66860000e-05, 3.45420000e-05, 3.25500000e-05,
        3.06980000e-05, 2.89760000e-05, 2.73740000e-05, 2.58820000e-05, 2.44910000e-05,
        2.31960000e-05, 2.19870000e-05, 2.08580000e-05, 1.98050000e-05, 1.88200000e-05,
        1.79000000e-05, 1.70390000e-05, 1.62330000e-05, 1.54780000e-05, 1.47710000e-05,
        1.41080000e-05, 1.34870000e-05, 1.29030000e-05, 1.23560000e-05, 1.18410000e-05,
        1.13580000e-05, 1.09040000e-05, 1.04770000e-05, 1.00740000e-05, 9.69470000e-06,
        9.33700000e-06, 8.99920000e-06, 8.68010000e-06, 8.37830000e-06, 8.09280000e-06,
        7.82230000e-06, 7.56590000e-06, 7.32270000e-06, 7.09170000e-06, 6.87230000e-06,
        6.66350000e-06, 6.46490000e-06, 6.27570000e-06, 6.09520000e-06, 5.92310000e-06,
        5.75870000e-06, 5.60160000e-06, 5.45140000e-06, 5.30750000e-06, 5.16980000e-06,
        5.03770000e-06, 4.91110000e-06, 4.78950000e-06, 4.67280000e-06, 4.56050000e-06,
        4.45250000e-06, 4.34860000e-06, 4.24840000e-06, 4.15190000e-06, 4.05870000e-06,
        3.96870000e-06, 3.88180000e-06, 3.79770000e-06, 3.71630000e-06, 3.63750000e-06,
        3.56110000e-06};
    static double ATMOSPHERE_LOG_RATIO[ATMOSPHERE_COUNT - 1];

    struct AtmosphereInit
    {
        AtmosphereInit()
        {
            for (int i = 0; i < ATMOSPHERE_COUNT - 1; ++i)
                ATMOSPHERE_LOG_RATIO[i] = std::log(ATMOSPHERE_RHO[i + 1] / ATMOSPHERE_RHO[i]);
        }
    } atmosphereInit;

    static inline double DensityAtKm(double altKm)
    {
        if (altKm <= 0.0)
            return ATMOSPHERE_RHO[0];
        if (altKm >= DRAG_CUTOFF_KM || !std::isfinite(altKm))
            return 0.0;
        if (altKm > ATMOSPHERE_REFERENCE_TOP_KM)
        {
            // Numerical extension, not additional reference data. Continue the last
            // scale height, then fade with zero slope at either end of the taper.
            const double t = (altKm - ATMOSPHERE_REFERENCE_TOP_KM) /
                             (DRAG_CUTOFF_KM - ATMOSPHERE_REFERENCE_TOP_KM);
            const double fade = (1.0 - t) * (1.0 - t) * (1.0 + 2.0 * t);
            return ATMOSPHERE_RHO[ATMOSPHERE_COUNT - 1] * fade * std::exp((altKm - ATMOSPHERE_REFERENCE_TOP_KM) / ATMOSPHERE_STEP_KM * ATMOSPHERE_LOG_RATIO[ATMOSPHERE_COUNT - 2]);
        }
        // Constant-time indexing; only one exponential and no per-call table search.
        const double coordinate = altKm / ATMOSPHERE_STEP_KM;
        const int idx = std::min(static_cast<int>(coordinate), ATMOSPHERE_COUNT - 2);
        return ATMOSPHERE_RHO[idx] * std::exp((coordinate - idx) * ATMOSPHERE_LOG_RATIO[idx]);
    }

    static Vector3d ComputeDragAcceleration(
        const Vector3d &velUU,    // Unity units / s (1u = 10 km)
        const Vector3d &posRelUU, // Unity units, relative to Earth@origin
        double mass,              // kg
        double areaUU,            // Unity units^2  (1u^2 = 100 km^2)
        double Cd)
    {
        // Most constellation members spend their time outside the atmosphere. The
        // caller marks those bodies with zero drag so all seven RK stages can skip
        // the radius, density, and relative-velocity work immediately.
        if (Cd <= 0.0 || areaUU <= 0.0 || mass <= 1e-6)
            return {0, 0, 0};

        const double xkm = posRelUU.x * UNIT_TO_KM;
        const double ykm = posRelUU.y * UNIT_TO_KM;
        const double zkm = posRelUU.z * UNIT_TO_KM;
        const double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);

        // NOTE: don't clamp with std::max here
        double alt = rkm - EARTH_RADIUS_KM;

        // above atmosphere ceiling -> no drag
        if (alt >= DRAG_CUTOFF_KM)
            return {0, 0, 0};

        if (alt < 0.0)
            alt = 0.0;

        double rho = DensityAtKm(alt);
        if (rho <= 0.0)
            return {0, 0, 0};

        // Convert velocity to km/s and subtract atmospheric co-rotation
        const Vector3d vkm = {velUU.x * UNIT_TO_KM, velUU.y * UNIT_TO_KM, velUU.z * UNIT_TO_KM};
        // Earth rotates about +Y/-Y in Unity world space; for the stated eastward
        // surface motion (+X -> +Z -> -X -> -Z), use co-rotation in the XZ plane.
        const Vector3d vatm = {-OMEGA_EARTH * zkm, 0.0, OMEGA_EARTH * xkm}; // km/s
        const Vector3d vrel = {vkm.x - vatm.x, vkm.y - vatm.y, vkm.z - vatm.z};

        const double speed = std::sqrt(vrel.x * vrel.x + vrel.y * vrel.y + vrel.z * vrel.z);
        if (speed < 1e-6)
            return {0, 0, 0};

        // areaUU is in u^2; convert to km^2
        const double A_km2 = areaUU * UNIT_TO_KM * UNIT_TO_KM;

        // a[km/s^2] = -0.5 * Cd * A * rho / m * v * |v|
        const double factor = -0.5 * Cd * A_km2 * rho / mass;
        Vector3d a_km = {factor * vrel.x * speed, factor * vrel.y * speed, factor * vrel.z * speed};
        static const double MAX_DRAG_ACCEL_KM_S2 = .1; // tune this
        const double a2 = a_km.x * a_km.x + a_km.y * a_km.y + a_km.z * a_km.z;
        if (a2 > MAX_DRAG_ACCEL_KM_S2 * MAX_DRAG_ACCEL_KM_S2)
        {
            const double invA = 1.0 / std::sqrt(a2);
            const double s = MAX_DRAG_ACCEL_KM_S2 * invA;
            a_km.x *= s;
            a_km.y *= s;
            a_km.z *= s;
        }

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

    static void IntegrateBatch(
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
        int substeps,
        double *deltaVOut,
        const double *dryMasses = nullptr, double *fuelMasses = nullptr,
        const double *ispSeconds = nullptr, const uint8_t *finiteFuel = nullptr)
    {
        if (count <= 0)
            return;
        if (substeps < 1)
            substeps = 1;

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
            if (deltaVOut)
                deltaVOut[i] = 0.0;
            const bool finite = finiteFuel && finiteFuel[i] != 0;
            const double dryMass = finite ? dryMasses[i] : masses[i];
            double fuel = finite ? fuelMasses[i] : 0.0;
            const double initialMass = finite ? dryMass + fuel : masses[i];
            if (!std::isfinite(initialMass) || dryMass <= 1e-6 || fuel < 0.0 ||
                (finite && (!std::isfinite(ispSeconds[i]) || ispSeconds[i] <= 0.0)))
                continue;

            Vector3d pos = ToVector3dFromDouble3(positions[i]);
            Vector3d vel = ToVector3dFromDouble3(velocities[i]);

            const Vector3 Ti = thrusts[i];
            const double forceMagnitude = std::sqrt(double(Ti.x) * Ti.x + double(Ti.y) * Ti.y + double(Ti.z) * Ti.z);

            const double Cd = static_cast<double>(dragCoeffs[i]);
            const double Auu = static_cast<double>(areasUU[i]);
            const int8_t flag = normalSign ? normalSign[i] : 0; // 0 = free, ±1 = lateral shaping
            const bool lateralMode = (flag != 0);
            int localSubsteps = substeps;

            if (Cd > 0.0 && Auu > 0.0)
            {
                const double xkm = pos.x * UNIT_TO_KM;
                const double ykm = pos.y * UNIT_TO_KM;
                const double zkm = pos.z * UNIT_TO_KM;
                const double rkm = std::sqrt(xkm * xkm + ykm * ykm + zkm * zkm);
                const double altKm = rkm - EARTH_RADIUS_KM;

                // Dense atmosphere is the stiff regime. Spend more integrator work there
                // so re-entry remains smooth instead of taking giant drag-biased hops.
                if (altKm < 120.0)
                    localSubsteps *= 2;
                if (altKm < 80.0)
                    localSubsteps *= 2;
                if (altKm < 50.0)
                    localSubsteps *= 2;
            }

            const double nominalDt = static_cast<double>(totalDt) / static_cast<double>(localSubsteps);

            for (int s = 0; s < localSubsteps; ++s)
            {
                double remaining = nominalDt;
                while (remaining > 0.0)
                {
                    const double msc = finite ? dryMass + fuel : initialMass;
                    bool firing = !finite || fuel > 0.0;
                    // Do not spend propellant on a rejected/degenerate normal command.
                    if (finite && firing && lateralMode)
                    {
                        Vector3d normal;
                        firing = NormalHat(pos, vel, normal) &&
                                 std::fabs((Ti.x * normal.x + Ti.y * normal.y + Ti.z * normal.z) / msc) > 1e-12;
                    }
                    const double massFlow = finite && firing
                                                ? forceMagnitude * (UNIT_TO_KM * 1000.0) / (ispSeconds[i] * 9.80665)
                                                : 0.0;
                    double dt = remaining;
                    bool exhausted = false;
                    if (massFlow > 0.0)
                    {
                        // Resolve burnout exactly and limit each RK step to 1% mass loss.
                        dt = std::min(dt, 0.01 * msc / massFlow);
                        const double burnSeconds = fuel / massFlow;
                        exhausted = burnSeconds <= dt;
                        if (exhausted)
                            dt = burnSeconds;
                    }
                    const Vector3d thBase = firing ? Vector3d{Ti.x / msc, Ti.y / msc, Ti.z / msc} : Vector3d{0, 0, 0};
                    const double thMag = std::sqrt(thBase.x * thBase.x + thBase.y * thBase.y + thBase.z * thBase.z);
                    Vector3d kx[7], kv[7];
                    double thrustAcceleration[7] = {};

                    // ---- Stage 0 ----
                    kx[0] = vel;
                    kv[0] = gravA(pos);

                    if (!lateralMode)
                    {
                        // Free thrust: use the acceleration from the thrust vector as-is.
                        kv[0].x += thBase.x;
                        kv[0].y += thBase.y;
                        kv[0].z += thBase.z;
                        thrustAcceleration[0] = thMag;
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
                                thrustAcceleration[0] = thMag;
                            }
                        }
                        // else: degenerate normal => no lateral thrust this substep
                    }

                    // drag at stage 0
                    {
                        Vector3d d0 = ComputeDragAcceleration(vel, pos, msc, Auu, Cd);
                        kv[0].x += d0.x;
                        kv[0].y += d0.y;
                        kv[0].z += d0.z;
                    }

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
                        // Mass, thrust acceleration, and drag share the RK stage time.
                        const double stageMass = std::max(dryMass, msc - massFlow * dt * c_dp[st]);
                        const Vector3d thBase = firing ? Vector3d{Ti.x / stageMass, Ti.y / stageMass, Ti.z / stageMass} : Vector3d{0, 0, 0};
                        const double thMag = std::sqrt(thBase.x * thBase.x + thBase.y * thBase.y + thBase.z * thBase.z);

                        if (!lateralMode)
                        {
                            kv[st].x += thBase.x;
                            kv[st].y += thBase.y;
                            kv[st].z += thBase.z;
                            thrustAcceleration[st] = thMag;
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
                                    thrustAcceleration[st] = thMag;
                                }
                            }
                        }

                        Vector3d ds = ComputeDragAcceleration(vi, pi, stageMass, Auu, Cd);
                        kv[st].x += ds.x;
                        kv[st].y += ds.y;
                        kv[st].z += ds.z;
                    }

                    // Integrate the magnitude of applied thrust with the state RK weights.
                    // Rejected lateral thrust, gravity, and drag contribute no delta-v.
                    if (deltaVOut)
                    {
                        double increment = 0.0;
                        for (int st = 0; st < 7; ++st)
                            increment += dt * b_dp[st] * thrustAcceleration[st];
                        // DP has a negative weight: suppress negative quadrature artifacts
                        // at discontinuous thrust thresholds without changing dynamics.
                        deltaVOut[i] += std::max(0.0, increment);
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
                    if (finite && massFlow > 0.0)
                        fuel = exhausted ? 0.0 : std::max(0.0, fuel - massFlow * dt);
                    remaining = std::max(0.0, remaining - dt);
                }
            }
            if (finite)
                fuelMasses[i] = fuel;

            // if (latchedParityIO)
            //     latchedParityIO[i] = latched;

            positions[i] = ToDouble3(pos);
            velocities[i] = ToDouble3(vel);
        }
    }
    // Preserve the prediction ABI; telemetry is opt-in for live integration.
    EXPORT void BatchTwoBodyIntegrateMuEx(
        double3 *positions, double3 *velocities, const double *masses,
        const Vector3 *thrusts, const float *dragCoeffs, const float *areasUU,
        const int8_t *normalSign, const uint8_t *isThrusting, int8_t *latchedParityIO,
        int count, double mu, float totalDt, int substeps)
    {
        IntegrateBatch(positions, velocities, masses, thrusts, dragCoeffs, areasUU,
                       normalSign, isThrusting, latchedParityIO, count, mu, totalDt, substeps, nullptr);
    }

    EXPORT void BatchTwoBodyIntegrateMuExWithDeltaV(
        double3 *positions, double3 *velocities, const double *masses,
        const Vector3 *thrusts, const float *dragCoeffs, const float *areasUU,
        const int8_t *normalSign, const uint8_t *isThrusting, int8_t *latchedParityIO,
        int count, double mu, float totalDt, int substeps, double *deltaVOut)
    {
        IntegrateBatch(positions, velocities, masses, thrusts, dragCoeffs, areasUU,
                       normalSign, isThrusting, latchedParityIO, count, mu, totalDt, substeps, deltaVOut);
    }

    // Existing exports remain ABI-compatible for ballistic prediction and older callers.
    EXPORT void BatchTwoBodyIntegrateMuExWithFuel(
        double3 *positions, double3 *velocities, const double *masses,
        const Vector3 *thrusts, const float *dragCoeffs, const float *areasUU,
        const int8_t *normalSign, const uint8_t *isThrusting, int8_t *latchedParityIO,
        int count, double mu, float totalDt, int substeps, double *deltaVOut,
        const double *dryMasses, double *fuelMasses, const double *ispSeconds, const uint8_t *finiteFuel)
    {
        IntegrateBatch(positions, velocities, masses, thrusts, dragCoeffs, areasUU,
                       normalSign, isThrusting, latchedParityIO, count, mu, totalDt, substeps, deltaVOut,
                       dryMasses, fuelMasses, ispSeconds, finiteFuel);
    }
}
