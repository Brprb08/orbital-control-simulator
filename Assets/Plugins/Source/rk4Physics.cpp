#include <cmath>

// Unity-Compatible Vector3 Struct
extern "C"
{

    struct Vector3
    {
        float x, y, z;
    };

    const float G = 6.67430e-23f; // Gravitational constant

    Vector3 ComputeAcceleration(Vector3 position, float mass, Vector3 *bodies, float *masses, int numBodies, int centralBodyIndex)
    {
        Vector3 acceleration = {0.0f, 0.0f, 0.0f};
        const float minDistSq = 0.0001f;
        const float maxForce = 1e8f;

        for (int i = 0; i < numBodies; i++)
        {
            Vector3 dir = {
                bodies[i].x - position.x,
                bodies[i].y - position.y,
                bodies[i].z - position.z};

            float distSq = dir.x * dir.x + dir.y * dir.y + dir.z * dir.z;
            if (distSq < minDistSq)
                continue;

            float force = (G * masses[i]) / distSq;
            force = fmin(force, maxForce);

            float dist = sqrt(distSq);
            acceleration.x += (force / dist) * dir.x;
            acceleration.y += (force / dist) * dir.y;
            acceleration.z += (force / dist) * dir.z;
        }

        return acceleration;
    }

    void RungeKuttaStep(Vector3 &pos, Vector3 &vel, float mass, float dt,
                        Vector3 *bodies, float *masses, int numBodies, int centralBodyIndex, Vector3 thrustImpulse)
    {
        if (mass <= 0.00001f)
            return;

        Vector3 thrustAcc = {thrustImpulse.x / mass, thrustImpulse.y / mass, thrustImpulse.z / mass};

        Vector3 k1_v = ComputeAcceleration(pos, mass, bodies, masses, numBodies, centralBodyIndex);
        k1_v.x += thrustAcc.x;
        k1_v.y += thrustAcc.y;
        k1_v.z += thrustAcc.z;
        Vector3 k1_x = vel;

        Vector3 pos_k1 = {pos.x + (0.5f * dt) * k1_x.x, pos.y + (0.5f * dt) * k1_x.y, pos.z + (0.5f * dt) * k1_x.z};
        Vector3 vel_k1 = {vel.x + (0.5f * dt) * k1_v.x, vel.y + (0.5f * dt) * k1_v.y, vel.z + (0.5f * dt) * k1_v.z};
        Vector3 k2_v = ComputeAcceleration(pos_k1, mass, bodies, masses, numBodies, centralBodyIndex);
        k2_v.x += thrustAcc.x;
        k2_v.y += thrustAcc.y;
        k2_v.z += thrustAcc.z;
        Vector3 k2_x = vel_k1;

        Vector3 pos_k2 = {pos.x + (0.5f * dt) * k2_x.x, pos.y + 0.5f * dt * k2_x.y, pos.z + (0.5f * dt) * k2_x.z};
        Vector3 vel_k2 = {vel.x + (0.5f * dt) * k2_v.x, vel.y + 0.5f * dt * k2_v.y, vel.z + (0.5f * dt) * k2_v.z};
        Vector3 k3_v = ComputeAcceleration(pos_k2, mass, bodies, masses, numBodies, centralBodyIndex);
        k3_v.x += thrustAcc.x;
        k3_v.y += thrustAcc.y;
        k3_v.z += thrustAcc.z;
        Vector3 k3_x = vel_k2;

        Vector3 pos_k3 = {pos.x + k3_x.x * dt, pos.y + k3_x.y * dt, pos.z + k3_x.z * dt};
        Vector3 vel_k3 = {vel.x + k3_v.x * dt, vel.y + k3_v.y * dt, vel.z + k3_v.z * dt};
        Vector3 k4_v = ComputeAcceleration(pos_k3, mass, bodies, masses, numBodies, centralBodyIndex);
        k4_v.x += thrustAcc.x;
        k4_v.y += thrustAcc.y;
        k4_v.z += thrustAcc.z;
        Vector3 k4_x = vel_k3;

        vel.x += (dt / 6.0f) * (k1_v.x + 2.0f * k2_v.x + 2.0f * k3_v.x + k4_v.x);
        vel.y += (dt / 6.0f) * (k1_v.y + 2.0f * k2_v.y + 2.0f * k3_v.y + k4_v.y);
        vel.z += (dt / 6.0f) * (k1_v.z + 2.0f * k2_v.z + 2.0f * k3_v.z + k4_v.z);

        pos.x += (dt / 6.0f) * (k1_x.x + 2.0f * k2_x.x + 2.0f * k3_x.x + k4_x.x);
        pos.y += (dt / 6.0f) * (k1_x.y + 2.0f * k2_x.y + 2.0f * k3_x.y + k4_x.y);
        pos.z += (dt / 6.0f) * (k1_x.z + 2.0f * k2_x.z + 2.0f * k3_x.z + k4_x.z);
    }

    extern "C" __attribute__((visibility("default"))) void RungeKuttaSingle(Vector3 *position, Vector3 *velocity, float mass,
                                                                            Vector3 *bodies, float *masses, int numBodies, float dt, Vector3 thrustImpulse)
    {
        int centralBodyIndex = 0;
        if (mass <= 1e-6f)
            return;

        RungeKuttaStep(*position, *velocity, mass, dt, bodies, masses, numBodies, centralBodyIndex, thrustImpulse);
    }
}
