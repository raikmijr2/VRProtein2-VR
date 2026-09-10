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
            if (frameLabel) frameLabel.text = "Necesitas ≥ 2 frames de trayectoria";
            return;
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
