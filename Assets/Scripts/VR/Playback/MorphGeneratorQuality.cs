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
            rotations[res] = KabschSolver.ComputeOptimalRotation(P, Q);
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

}
}
