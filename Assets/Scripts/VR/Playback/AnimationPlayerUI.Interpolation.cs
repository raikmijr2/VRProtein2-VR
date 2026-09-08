using UnityEngine;

namespace UMol {

public partial class AnimationPlayerUI {

    bool CanInterpolate(UnityMolStructure s) => !IsXTC(s) && GetTotalFrames(s) > 1;

    void StartSmoothPlay(UnityMolStructure s) {
        if (s.modelsPlayer != null) s.modelsPlayer.play = false;

        int total = GetTotalFrames(s);
        lerpFromFrame = Mathf.Clamp(GetCurrentFrame(s), 0, total - 1);
        lerpToFrame   = (lerpFromFrame + 1) % total;
        lerpT         = 0f;

        if (!s.trajectoryMode && s.models != null && s.models.Count > 1) {
            modelPosCache = new Vector3[s.models.Count][];
            for (int f = 0; f < s.models.Count; f++) {
                var atoms = s.models[f].allAtoms;
                modelPosCache[f] = new Vector3[atoms.Count];
                for (int i = 0; i < atoms.Count; i++)
                    modelPosCache[f][i] = atoms[i].position;
            }
        } else {
            modelPosCache = null;
        }

        smoothPlay = true;
        _repSkipCounter = 0;
        _repSkipRate    = 1;
    }

    void StopSmoothPlay(UnityMolStructure s) {
        smoothPlay = false;
        _repSkipCounter = 0;
        _repSkipRate    = 1;
        if (s != null) s.setModel(lerpFromFrame);
    }

    void UpdateSmooth(UnityMolStructure s) {
        int total = GetTotalFrames(s);
        lerpT += Time.deltaTime * speed;

        while (lerpT >= 1f) {
            lerpT -= 1f;
            lerpFromFrame = lerpToFrame;
            if (looping) {
                lerpToFrame = (lerpFromFrame + 1) % total;
            } else {
                if (lerpFromFrame >= total - 1) { smoothPlay = false; s.setModel(total - 1); return; }
                lerpToFrame = lerpFromFrame + 1;
            }
        }

        ApplyLerp(s, lerpFromFrame, lerpToFrame, lerpT);
    }

    void ApplyLerp(UnityMolStructure s, int frameA, int frameB, float t) {
        Vector3[] posA, posB;

        if (s.trajectoryMode && s.modelFrames != null) {
            if (frameA >= s.modelFrames.Count || frameB >= s.modelFrames.Count) return;
            posA = s.modelFrames[frameA];
            posB = s.modelFrames[frameB];
        } else if (modelPosCache != null) {
            if (frameA >= modelPosCache.Length || frameB >= modelPosCache.Length) return;
            posA = modelPosCache[frameA];
            posB = modelPosCache[frameB];
        } else return;

        int n = Mathf.Min(posA.Length, posB.Length);
        if (lerpBuffer == null || lerpBuffer.Length < n) lerpBuffer = new Vector3[n];
        bool anyNaN = false;
        for (int i = 0; i < n; i++) {
            Vector3 v = Vector3.Lerp(posA[i], posB[i], t);
            // Guard: a NaN atom position corrupts the whole mesh (Invalid localAABB)
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) {
                v = posA[i];  // fallback to frame A
                anyNaN = true;
            }
            lerpBuffer[i] = v;
        }
        if (anyNaN) Debug.LogWarning("[AnimPlayer] NaN en posiciones DCD — frame descartado");

        s.trajAtomPositions = lerpBuffer;
        s.trajUpdateAtomPositions();

        if (++_repSkipCounter >= _repSkipRate) {
            _repSkipCounter = 0;
            float t0 = Time.realtimeSinceStartup;
            s.updateRepresentations(trajectory: true);
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;
            _repSkipRate = ms > 16f ? Mathf.Clamp(Mathf.RoundToInt(ms / 16f), 2, 8) : 1;
        }
    }

    // ── Helpers de estado ─────────────────────────────────────────────────────

    UnityMolStructure GetAnimatedStructure(UnityMolStructureManager sm = null) {
        if (sm == null) sm = UnityMolMain.getStructureManager();
        var s = GetCurrentTarget(sm);
        if (s == null) return null;
        if (s.trajectoryLoaded) return s;
        if (s.trajectoryMode && s.modelFrames != null && s.modelFrames.Count > 1) return s;
        if (s.models != null && s.models.Count > 1) return s;
        return null;
    }

    bool IsXTC(UnityMolStructure s) => s.trajectoryLoaded && s.xdr != null;

    bool IsCurrentlyPlaying(UnityMolStructure s) {
        if (smoothPlay || manualPlay) return true;
        if (s.trajPlayer   != null) return s.trajPlayer.play;
        if (s.modelsPlayer != null) return s.modelsPlayer.play;
        return false;
    }

    int GetCurrentFrame(UnityMolStructure s) {
        if (_morphActive)     return s.currentFrameId;
        if (IsXTC(s))         return s.xdr.CurrentFrame;
        if (s.trajectoryMode) return s.currentFrameId;
        return s.currentModelId;
    }

    int GetTotalFrames(UnityMolStructure s) {
        if (_morphActive && s.modelFrames != null)         return s.modelFrames.Count;
        if (IsXTC(s))                                      return s.xdr.NumberFrames;
        if (s.trajectoryMode && s.modelFrames != null)     return s.modelFrames.Count;
        return s.models != null ? s.models.Count : 1;
    }

    void StepFrame(UnityMolStructure s, bool fwd) {
        if (_morphActive && s.modelFrames != null) {
            int total = s.modelFrames.Count;
            int next  = s.currentFrameId + (fwd ? 1 : -1);
            if (next >= total) next = looping ? 0 : total - 1;
            if (next < 0)      next = looping ? total - 1 : 0;
            s.setModel(next);
            return;
        }
        if (IsXTC(s)) s.trajNext(fwd, looping);
        else          s.modelNext(fwd, looping);
    }

    // Returns true if the FULL-PROTEIN selection ("all_<name>") has an active rep with
    // actual geometry. Partial-selection reps don't count — if the full protein is hidden
    // but a small subset is still showing, we still want to restore the whole protein.
    static bool HasVisibleFullRep(UnityMolStructure s) {
        if (s?.representations == null) return false;
        string fullName = s.ToSelectionName(); // "all_ack28"
        foreach (var r in s.representations)
            if (r.selection?.name == fullName && r.isActive() &&
                (r.nbAtomsInRep > 0 || r.nbBondsInRep > 0))
                return true;
        return false;
    }
}
}
