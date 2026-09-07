using UnityEngine;
using System.Collections.Generic;

namespace UMol {

/// Genera una trayectoria morph interpolando linealmente entre el primer y
/// último frame de una estructura. Sustituye s.modelFrames con el resultado.
public static class MorphGenerator {

    public const int DEFAULT_STEPS = 60;

    /// <returns>true si el morph se generó, false si no hay suficientes frames.</returns>
    public static bool Generate(UnityMolStructure s, int steps = DEFAULT_STEPS) {
        if (s == null || steps < 2) return false;

        Vector3[] first, last;

        if (s.trajectoryLoaded && s.xdr != null && s.xdr.NumberFrames >= 2) {
            int atomCount = s.currentModel.allAtoms.Count;

            s.trajSetFrame(0);
            first = new Vector3[atomCount];
            for (int i = 0; i < atomCount; i++)
                first[i] = s.currentModel.allAtoms[i].position;

            s.trajSetFrame(s.xdr.NumberFrames - 1);
            last = new Vector3[atomCount];
            for (int i = 0; i < atomCount; i++)
                last[i] = s.currentModel.allAtoms[i].position;

            s.trajSetFrame(0);

        } else if (s.trajectoryMode && s.modelFrames != null && s.modelFrames.Count >= 2) {
            first = s.modelFrames[0];
            last  = s.modelFrames[s.modelFrames.Count - 1];
        } else if (s.models != null && s.models.Count >= 2) {
            var fa = s.models[0].allAtoms;
            var la = s.models[s.models.Count - 1].allAtoms;
            int atomCount = Mathf.Min(fa.Count, la.Count);
            first = new Vector3[atomCount];
            last  = new Vector3[atomCount];
            for (int i = 0; i < atomCount; i++) {
                first[i] = fa[i].position;
                last[i]  = la[i].position;
            }
        } else {
            Debug.LogWarning("[Morph] Sin frames suficientes — Generate devuelve false");
            return false;
        }

        int n = Mathf.Min(first.Length, last.Length);
        var frames = new List<Vector3[]>(steps);
        for (int f = 0; f < steps; f++) {
            float t = (float)f / (steps - 1);
            var frame = new Vector3[n];
            for (int i = 0; i < n; i++)
                frame[i] = Vector3.Lerp(first[i], last[i], t);
            frames.Add(frame);
        }

        s.modelFrames    = frames;
        s.trajectoryMode = true;
        s.currentFrameId = 0;
        return true;
    }
}
}
