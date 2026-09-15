using UnityEngine;

namespace UMol {

public partial class AnimationPlayerUI {

    public void OnPlayPause() {
        var sm = UnityMolMain.getStructureManager();
        var target = GetCurrentTarget(sm);

        if (target == null) {
            return;
        }

        var s = GetAnimatedStructure(sm);

        // Sin animación todavía → generar sintética y arrancar
        if (s == null) {
            if (SyntheticAnimator.GenerateFor(target)) {
                manualPlay = true;
                manualTimer = 0f;
                return;
            }
            return;
        }

        if (_morphActive) {
            manualPlay = !manualPlay;
            manualTimer = 0f;
            return;
        }

        if (s.trajPlayer != null) {
            bool next = !s.trajPlayer.play;
            s.trajPlayer.play          = next;
            s.trajPlayer.looping       = looping;
            s.trajPlayer.trajFramerate = speed;
            return;
        }

        if (IsXTC(s)) { manualPlay = !manualPlay; manualTimer = 0f; return; }

        if (CanInterpolate(s)) {
            manualPlay = !manualPlay;
            manualTimer = 0f;
            return;
        }

    }

    public void OnPrev() {
        var s = GetAnimatedStructure();
        if (s == null) return;
        smoothPlay = false; manualPlay = false;
        if (s.trajPlayer   != null) s.trajPlayer.play   = false;
        if (s.modelsPlayer != null) s.modelsPlayer.play = false;
        StepFrame(s, false);
    }

    public void OnNext() {
        var s = GetAnimatedStructure();
        if (s == null) return;
        smoothPlay = false; manualPlay = false;
        if (s.trajPlayer   != null) s.trajPlayer.play   = false;
        if (s.modelsPlayer != null) s.modelsPlayer.play = false;
        StepFrame(s, true);
    }

    public void OnSpeedDown() { speedIdx = Mathf.Max(0, speedIdx - 1);                       ApplySpeed(); if (speedLabel) speedLabel.text = SpeedText(); }
    public void OnSpeedUp()   { speedIdx = Mathf.Min(speedSteps.Length - 1, speedIdx + 1);  ApplySpeed(); if (speedLabel) speedLabel.text = SpeedText(); }

    void ApplySpeed() {
        var s = GetAnimatedStructure();
        if (s == null) return;
        if (s.trajPlayer   != null) s.trajPlayer.trajFramerate    = speed;
        if (s.modelsPlayer != null) s.modelsPlayer.modelFramerate = speed;
    }

    public void OnLoopToggle() {
        looping = !looping;
        var s = GetAnimatedStructure();
        if (s?.trajPlayer   != null) s.trajPlayer.looping   = looping;
        if (s?.modelsPlayer != null) s.modelsPlayer.looping = looping;
        Color c = looping ? btnOrange : btnNormal;
        if (loopBtnImg)   loopBtnImg.color = c;
        UpdateButtonColorBlock(loopBtn, c);
        if (loopBtnLabel) loopBtnLabel.text = looping ? "Loop: ON" : "Loop: OFF";
    }

    public void OnMorphClick()         => LaunchMorph(0);
    public void OnMorphQualityClick()  => LaunchMorph(1);
    public void OnMorphPhysicalClick() => LaunchMorph(2);

    void LaunchMorph(int mode) {
        var sm = UnityMolMain.getStructureManager();
        var s  = GetCurrentTarget(sm);
        if (s == null) return;

        smoothPlay = false;
        manualPlay = false;
        if (s.trajPlayer   != null) s.trajPlayer.play   = false;
        if (s.modelsPlayer != null) s.modelsPlayer.play = false;

        bool ok = mode == 0 ? MorphGenerator.Generate(s)
                : mode == 1 ? MorphGeneratorQuality.Generate(s)
                :             MorphGeneratorPhysical.Generate(s);
        if (!ok) {
            if (frameLabel) frameLabel.text = "Necessites ≥ 2 frames de trajectòria";
            return;
        }

        // DIAG temporal: amplitud real del morph generado, para verificar si los
        // frames intermedios realmente se mueven o si casi todo el desplazamiento
        // esta concentrado entre el ultimo frame y el primero (el salto del loop).
        if (frameLabel != null && s.modelFrames != null && s.modelFrames.Count > 1) {
            var f0    = s.modelFrames[0];
            var fMid  = s.modelFrames[s.modelFrames.Count / 2];
            var fLast = s.modelFrames[s.modelFrames.Count - 1];
            int n = Mathf.Min(f0.Length, fLast.Length);
            float maxD0Mid = 0f, maxD0Last = 0f, maxDMidLast = 0f;
            for (int i = 0; i < n; i++) {
                maxD0Mid    = Mathf.Max(maxD0Mid,    Vector3.Distance(f0[i], fMid[i]));
                maxD0Last   = Mathf.Max(maxD0Last,   Vector3.Distance(f0[i], fLast[i]));
                maxDMidLast = Mathf.Max(maxDMidLast, Vector3.Distance(fMid[i], fLast[i]));
            }
            lastMorphAmplitude = $"0→mid:{maxD0Mid:F2} mid→last:{maxDMidLast:F2} 0→last:{maxD0Last:F2}";
        }

        s.setModel(0);
        _morphActive = true;
        manualPlay   = true;
        manualTimer  = 0f;
        morphPlayheadFrame = 0f;
    }

    string SpeedText() => speed < 1f ? $"{speed:F2} fps" : $"{speed:F0} fps";
}
}
