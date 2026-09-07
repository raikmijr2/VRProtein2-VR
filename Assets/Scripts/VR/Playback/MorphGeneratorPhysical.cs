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
            rotations[res] = KabschSolver.ComputeOptimalRotation(P, Q);
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

}
}
