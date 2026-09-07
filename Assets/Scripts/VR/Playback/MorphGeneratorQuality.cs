using UnityEngine;
using System.Collections.Generic;

namespace UMol {

// Per-residue rigid-body interpolation (RigiMol-style, like PyMOL quality morph).
// Each residue is treated as a rigid body: centroid is Lerp'd, orientation is Slerp'd
// via the Kabsch/QCP optimal rotation between the two endpoint conformations.
public static class MorphGeneratorQuality {

    public const int DEFAULT_STEPS = 60;

    public static bool Generate(UnityMolStructure s, int steps = DEFAULT_STEPS) {
        if (s == null || steps < 2) return false;

        Vector3[] first, last;
        int atomCount;

        if (s.trajectoryLoaded && s.xdr != null && s.xdr.NumberFrames >= 2) {
            atomCount = s.currentModel.allAtoms.Count;
            s.trajSetFrame(0);
            first = new Vector3[atomCount];
            for (int i = 0; i < atomCount; i++) first[i] = s.currentModel.allAtoms[i].position;
            s.trajSetFrame(s.xdr.NumberFrames - 1);
            last = new Vector3[atomCount];
            for (int i = 0; i < atomCount; i++) last[i] = s.currentModel.allAtoms[i].position;
            s.trajSetFrame(0);
        } else if (s.trajectoryMode && s.modelFrames != null && s.modelFrames.Count >= 2) {
            first     = s.modelFrames[0];
            last      = s.modelFrames[s.modelFrames.Count - 1];
            atomCount = first.Length;
        } else if (s.models != null && s.models.Count >= 2) {
            var fa = s.models[0].allAtoms;
            var la = s.models[s.models.Count - 1].allAtoms;
            atomCount = Mathf.Min(fa.Count, la.Count);
            first = new Vector3[atomCount];
            last  = new Vector3[atomCount];
            for (int i = 0; i < atomCount; i++) { first[i] = fa[i].position; last[i] = la[i].position; }
        } else {
            return false;
        }

        var allAtoms = s.currentModel.allAtoms;
        int n = Mathf.Min(atomCount, allAtoms.Count);

        // Group atom indices by residue
        var groups = new Dictionary<UnityMolResidue, List<int>>();
        for (int i = 0; i < n; i++) {
            var res = allAtoms[i].residue;
            if (!groups.TryGetValue(res, out var list)) { list = new List<int>(); groups[res] = list; }
            list.Add(i);
        }

        // Per-residue centroid and optimal rotation (Kabsch/QCP)
        var centFirst = new Dictionary<UnityMolResidue, Vector3>();
        var centLast  = new Dictionary<UnityMolResidue, Vector3>();
        var rotations = new Dictionary<UnityMolResidue, Quaternion>();
        var P = new List<Vector3>();
        var Q = new List<Vector3>();

        foreach (var kv in groups) {
            var res  = kv.Key;
            var idxs = kv.Value;
            int m    = idxs.Count;

            Vector3 cf = Vector3.zero, cl = Vector3.zero;
            foreach (int idx in idxs) { cf += first[idx]; cl += last[idx]; }
            cf /= m; cl /= m;
            centFirst[res] = cf;
            centLast[res]  = cl;

            P.Clear(); Q.Clear();
            foreach (int idx in idxs) { P.Add(first[idx] - cf); Q.Add(last[idx] - cl); }
            rotations[res] = KabschRotation(P, Q);
        }

        // Interpolate frames
        var frames = new List<Vector3[]>(steps);
        for (int f = 0; f < steps; f++) {
            float t     = (float)f / (steps - 1);
            var   frame = new Vector3[n];

            foreach (var kv in groups) {
                var res  = kv.Key;
                var idxs = kv.Value;
                Vector3    ct  = Vector3.Lerp(centFirst[res], centLast[res], t);
                Quaternion rot = Quaternion.Slerp(Quaternion.identity, rotations[res], t);
                Vector3    cf  = centFirst[res];

                foreach (int idx in idxs)
                    frame[idx] = ct + rot * (first[idx] - cf);
            }
            frames.Add(frame);
        }

        s.modelFrames    = frames;
        s.trajectoryMode = true;
        s.currentFrameId = 0;
        return true;
    }

    // QCP algorithm (Theobald 2005): finds optimal rotation quaternion from P→Q
    // via power iteration on the 4×4 symmetric F matrix built from the cross-covariance.
    static Quaternion KabschRotation(List<Vector3> P, List<Vector3> Q) {
        int n = P.Count;
        if (n == 0) return Quaternion.identity;
        if (n == 1) {
            return (P[0].sqrMagnitude < 1e-10f || Q[0].sqrMagnitude < 1e-10f)
                ? Quaternion.identity
                : Quaternion.FromToRotation(P[0], Q[0]);
        }

        float Sxx=0,Sxy=0,Sxz=0, Syx=0,Syy=0,Syz=0, Szx=0,Szy=0,Szz=0;
        for (int i = 0; i < n; i++) {
            float px=P[i].x, py=P[i].y, pz=P[i].z;
            float qx=Q[i].x, qy=Q[i].y, qz=Q[i].z;
            Sxx+=px*qx; Sxy+=px*qy; Sxz+=px*qz;
            Syx+=py*qx; Syy+=py*qy; Syz+=py*qz;
            Szx+=pz*qx; Szy+=pz*qy; Szz+=pz*qz;
        }

        // 4×4 symmetric F matrix — dominant eigenvector is the optimal quaternion [w,x,y,z]
        float F00=Sxx+Syy+Szz, F01=Syz-Szy, F02=Szx-Sxz, F03=Sxy-Syx;
        float F11=Sxx-Syy-Szz, F12=Sxy+Syx, F13=Szx+Sxz;
        float F22=-Sxx+Syy-Szz, F23=Syz+Szy;
        float F33=-Sxx-Syy+Szz;

        float q0=1f,q1=0f,q2=0f,q3=0f;
        for (int iter = 0; iter < 64; iter++) {
            float r0 = F00*q0 + F01*q1 + F02*q2 + F03*q3;
            float r1 = F01*q0 + F11*q1 + F12*q2 + F13*q3;
            float r2 = F02*q0 + F12*q1 + F22*q2 + F23*q3;
            float r3 = F03*q0 + F13*q1 + F23*q2 + F33*q3;
            float norm = Mathf.Sqrt(r0*r0 + r1*r1 + r2*r2 + r3*r3);
            if (norm < 1e-10f) break;
            float inv = 1f / norm;
            q0=r0*inv; q1=r1*inv; q2=r2*inv; q3=r3*inv;
        }

        // Unity Quaternion(x,y,z,w); QCP gives [w=q0, x=q1, y=q2, z=q3]
        return q0 < 0f ? new Quaternion(-q1,-q2,-q3,-q0) : new Quaternion(q1,q2,q3,q0);
    }
}
}
