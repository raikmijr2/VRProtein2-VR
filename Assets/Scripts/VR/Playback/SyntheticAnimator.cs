using UnityEngine;
using System.Collections.Generic;

namespace UMol {

/// Genera frames de animación sintéticos para estructuras con un solo modelo.
/// Cada átomo oscila según una dirección aleatoria con amplitud proporcional a
/// su B-factor (factor de temperatura del PDB), creando un movimiento de
/// "respiración" físicamente plausible.
public static class SyntheticAnimator {

    const int   NUM_FRAMES = 48;    // frames por ciclo (loop suave)
    const float BASE_AMP   = 0.25f; // Angstroms base
    const float BFAC_SCALE = 0.008f; // escala del B-factor sobre la amplitud

    /// Genera frames sintéticos y los asigna a la estructura.
    /// Solo actúa si la estructura tiene exactamente 1 modelo y no tiene
    /// trayectoria real cargada.
    public static bool GenerateFor(UnityMolStructure s) {
        if (s == null)                              return false;
        if (s.trajectoryLoaded)                     return false; // XTC ya cargado
        if (s.trajectoryMode && s.modelFrames != null &&
            s.modelFrames.Count > 1)                return false; // ya generado
        if (s.models == null || s.models.Count > 1) return false; // NMR: usa sus propios frames

        var atoms = s.currentModel.allAtoms;
        int n = atoms.Count;
        if (n == 0) return false;

        // Posiciones base
        Vector3[] basePos = new Vector3[n];
        for (int i = 0; i < n; i++)
            basePos[i] = atoms[i].position;

        // Dirección y fase aleatoria por átomo (seed fijo = reproducible)
        var rng = new System.Random(s.name.GetHashCode());
        var dirs   = new Vector3[n];
        var phases = new float[n];
        var amps   = new float[n];

        for (int i = 0; i < n; i++) {
            dirs[i] = new Vector3(
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0),
                (float)(rng.NextDouble() * 2.0 - 1.0)
            ).normalized;
            phases[i] = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float bfac = Mathf.Max(0f, atoms[i].bfactor);
            amps[i] = BASE_AMP + bfac * BFAC_SCALE;
        }

        // Genera un ciclo completo de frames (el último = el primero → loop perfecto)
        var frames = new List<Vector3[]>(NUM_FRAMES);
        for (int f = 0; f < NUM_FRAMES; f++) {
            float t = (float)f / NUM_FRAMES * Mathf.PI * 2f;
            var frame = new Vector3[n];
            for (int i = 0; i < n; i++) {
                float disp = amps[i] * Mathf.Sin(t + phases[i]);
                frame[i] = basePos[i] + dirs[i] * disp;
            }
            frames.Add(frame);
        }

        s.modelFrames   = frames;
        s.trajectoryMode = true;

        return true;
    }
}
}
