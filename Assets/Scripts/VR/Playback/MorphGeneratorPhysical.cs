using UnityEngine;
using System.Collections.Generic;

namespace UMol {

// Velocity-aware rigid-body morph (for XDR/DCD trajectories only).
// Like MorphGeneratorQuality (Kabsch per-residue rotation) but applies per-residue
// power easing based on speed estimated from the first two trajectory frames:
//   t_eff = t^(meanSpeed / residueSpeed)
// → fast residues lead the motion, slow ones lag behind.
// Falls back to MorphGeneratorQuality when no XDR trajectory is available.
public static class MorphGeneratorPhysical {

    public const int DEFAULT_STEPS = 60;

    public static bool Generate(UnityMolStructure s, int steps = DEFAULT_STEPS) {
        if (s == null || steps < 2) return false;

        // Velocity easing only possible with XDR trajectory (professor's DCD files)
        if (!s.trajectoryLoaded || s.xdr == null || s.xdr.NumberFrames < 2)
            return MorphGeneratorQuality.Generate(s, steps);

        int N         = s.xdr.NumberFrames;
        var allAtoms  = s.currentModel.allAtoms;
        int atomCount = allAtoms.Count;

        // Read frame 0, frame 1 (for velocity), and last frame
        s.trajSetFrame(0);
        Vector3[] pos0 = Snapshot(allAtoms, atomCount);
        s.trajSetFrame(1);
        Vector3[] pos1 = Snapshot(allAtoms, atomCount);
        s.trajSetFrame(N - 1);
        Vector3[] posN = Snapshot(allAtoms, atomCount);
        s.trajSetFrame(0);

        // Group atoms by residue
        var groups = new Dictionary<UnityMolResidue, List<int>>();
        for (int i = 0; i < atomCount; i++) {
            var res = allAtoms[i].residue;
            if (!groups.TryGetValue(res, out var lst)) { lst = new List<int>(); groups[res] = lst; }
            lst.Add(i);
        }

        // Per-residue: centroid, Kabsch rotation, and speed estimate
        var centFirst = new Dictionary<UnityMolResidue, Vector3>();
        var centLast  = new Dictionary<UnityMolResidue, Vector3>();
        var rotations = new Dictionary<UnityMolResidue, Quaternion>();
        var speedMap  = new Dictionary<UnityMolResidue, float>();
        float totalSpeed = 0f;

        var P = new List<Vector3>();
        var Q = new List<Vector3>();

        foreach (var kv in groups) {
            var res  = kv.Key;
            var idxs = kv.Value;
            int m    = idxs.Count;

            Vector3 cf = Vector3.zero, cl = Vector3.zero, c1 = Vector3.zero;
            foreach (int idx in idxs) { cf += pos0[idx]; cl += posN[idx]; c1 += pos1[idx]; }
            cf /= m; cl /= m; c1 /= m;
            centFirst[res] = cf;
            centLast[res]  = cl;

            float spd    = (c1 - cf).magnitude;
            speedMap[res] = spd;
            totalSpeed   += spd;

            P.Clear(); Q.Clear();
            foreach (int idx in idxs) { P.Add(pos0[idx] - cf); Q.Add(posN[idx] - cl); }
            rotations[res] = Kabsch(P, Q);
        }

        float meanSpeed = groups.Count > 0 ? totalSpeed / groups.Count : 1f;
        if (meanSpeed < 1e-12f) meanSpeed = 1f;

        // Generate frames
        var frames = new List<Vector3[]>(steps);
        for (int f = 0; f < steps; f++) {
            float t     = (float)f / (steps - 1);
            var   frame = new Vector3[atomCount];

            foreach (var kv in groups) {
                var res  = kv.Key;
                var idxs = kv.Value;

                // gamma < 1 for fast residues (leads), gamma > 1 for slow (lags)
                float gamma = meanSpeed / Mathf.Max(1e-10f, speedMap[res]);
                gamma = Mathf.Clamp(gamma, 0.25f, 4f);
                float te = (t <= 0f) ? 0f : (t >= 1f) ? 1f : Mathf.Pow(t, gamma);

                Vector3    ct  = Vector3.Lerp(centFirst[res], centLast[res], te);
                Quaternion rot = Quaternion.Slerp(Quaternion.identity, rotations[res], te);
                Vector3    cf  = centFirst[res];

                foreach (int idx in idxs)
                    frame[idx] = ct + rot * (pos0[idx] - cf);
            }
            frames.Add(frame);
        }

        s.modelFrames    = frames;
        s.trajectoryMode = true;
        s.currentFrameId = 0;
        return true;
    }

    static Vector3[] Snapshot(List<UnityMolAtom> atoms, int n) {
        var p = new Vector3[n];
        for (int i = 0; i < n; i++) p[i] = atoms[i].position;
        return p;
    }

    // QCP optimal rotation quaternion (Theobald 2005) — same as MorphGeneratorQuality
    static Quaternion Kabsch(List<Vector3> P, List<Vector3> Q) {
        int n = P.Count;
        if (n == 0) return Quaternion.identity;
        if (n == 1)
            return (P[0].sqrMagnitude < 1e-10f || Q[0].sqrMagnitude < 1e-10f)
                ? Quaternion.identity : Quaternion.FromToRotation(P[0], Q[0]);

        float Sxx=0,Sxy=0,Sxz=0, Syx=0,Syy=0,Syz=0, Szx=0,Szy=0,Szz=0;
        for (int i = 0; i < n; i++) {
            float px=P[i].x,py=P[i].y,pz=P[i].z, qx=Q[i].x,qy=Q[i].y,qz=Q[i].z;
            Sxx+=px*qx; Sxy+=px*qy; Sxz+=px*qz;
            Syx+=py*qx; Syy+=py*qy; Syz+=py*qz;
            Szx+=pz*qx; Szy+=pz*qy; Szz+=pz*qz;
        }
        float F00=Sxx+Syy+Szz, F01=Syz-Szy, F02=Szx-Sxz, F03=Sxy-Syx;
        float F11=Sxx-Syy-Szz, F12=Sxy+Syx, F13=Szx+Sxz;
        float F22=-Sxx+Syy-Szz, F23=Syz+Szy, F33=-Sxx-Syy+Szz;
        float q0=1f,q1=0f,q2=0f,q3=0f;
        for (int iter = 0; iter < 64; iter++) {
            float r0=F00*q0+F01*q1+F02*q2+F03*q3;
            float r1=F01*q0+F11*q1+F12*q2+F13*q3;
            float r2=F02*q0+F12*q1+F22*q2+F23*q3;
            float r3=F03*q0+F13*q1+F23*q2+F33*q3;
            float norm=Mathf.Sqrt(r0*r0+r1*r1+r2*r2+r3*r3);
            if (norm < 1e-10f) break;
            float inv=1f/norm; q0=r0*inv; q1=r1*inv; q2=r2*inv; q3=r3*inv;
        }
        return q0 < 0f ? new Quaternion(-q1,-q2,-q3,-q0) : new Quaternion(q1,q2,q3,q0);
    }
}
}
