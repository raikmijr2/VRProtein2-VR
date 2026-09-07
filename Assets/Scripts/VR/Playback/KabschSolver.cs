using System.Collections.Generic;
using UnityEngine;

namespace UMol {

/// <summary>
/// QCP (quaternion characteristic polynomial) optimal-rotation solver, used to
/// find the best rigid-body rotation aligning two matching point sets. Shared by
/// MorphGeneratorQuality and MorphGeneratorPhysical, which previously each kept
/// their own copy of this exact algorithm.
/// </summary>
public static class KabschSolver {

    public static Quaternion ComputeOptimalRotation(List<Vector3> P, List<Vector3> Q) {
        int n = P.Count;
        if (n == 0) return Quaternion.identity;
        if (n == 1) {
            return (P[0].sqrMagnitude < 1e-10f || Q[0].sqrMagnitude < 1e-10f)
                ? Quaternion.identity
                : Quaternion.FromToRotation(P[0], Q[0]);
        }

        float Sxx = 0, Sxy = 0, Sxz = 0, Syx = 0, Syy = 0, Syz = 0, Szx = 0, Szy = 0, Szz = 0;
        for (int i = 0; i < n; i++) {
            float px = P[i].x, py = P[i].y, pz = P[i].z;
            float qx = Q[i].x, qy = Q[i].y, qz = Q[i].z;
            Sxx += px * qx; Sxy += px * qy; Sxz += px * qz;
            Syx += py * qx; Syy += py * qy; Syz += py * qz;
            Szx += pz * qx; Szy += pz * qy; Szz += pz * qz;
        }

        // 4×4 symmetric F matrix — dominant eigenvector is the optimal quaternion [w,x,y,z]
        float F00 = Sxx + Syy + Szz, F01 = Syz - Szy, F02 = Szx - Sxz, F03 = Sxy - Syx;
        float F11 = Sxx - Syy - Szz, F12 = Sxy + Syx, F13 = Szx + Sxz;
        float F22 = -Sxx + Syy - Szz, F23 = Syz + Szy;
        float F33 = -Sxx - Syy + Szz;

        float q0 = 1f, q1 = 0f, q2 = 0f, q3 = 0f;
        for (int iter = 0; iter < 64; iter++) {
            float r0 = F00 * q0 + F01 * q1 + F02 * q2 + F03 * q3;
            float r1 = F01 * q0 + F11 * q1 + F12 * q2 + F13 * q3;
            float r2 = F02 * q0 + F12 * q1 + F22 * q2 + F23 * q3;
            float r3 = F03 * q0 + F13 * q1 + F23 * q2 + F33 * q3;
            float norm = Mathf.Sqrt(r0 * r0 + r1 * r1 + r2 * r2 + r3 * r3);
            if (norm < 1e-10f) break;
            float inv = 1f / norm;
            q0 = r0 * inv; q1 = r1 * inv; q2 = r2 * inv; q3 = r3 * inv;
        }

        // Unity Quaternion(x,y,z,w); QCP gives [w=q0, x=q1, y=q2, z=q3]
        return q0 < 0f ? new Quaternion(-q1, -q2, -q3, -q0) : new Quaternion(q1, q2, q3, q0);
    }
}
}
