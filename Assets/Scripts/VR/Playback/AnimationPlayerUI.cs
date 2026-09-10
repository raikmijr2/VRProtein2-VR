using UnityEngine;
using UnityEngine.UI;
using UMol.API;

namespace UMol {

/// <summary>
/// VR panel to play back trajectories/model animations and launch morphs for
/// the selected loaded structure. Split across 3 files: this one (fields,
/// Start/Update, protein-selection), .Interpolation.cs (playback/frame state
/// machine), .Callbacks.cs (button handlers). The UI hierarchy is an
/// Editor-built prefab (see AnimationPlayerPanel.prefab); this script only
/// holds the business logic and references to its parts.
/// </summary>
public partial class AnimationPlayerUI : MonoBehaviour {

    static readonly Color bgColor   = new Color(0.06f, 0.06f, 0.10f, 0.95f);
    static readonly Color btnNormal = new Color(0.18f, 0.35f, 0.72f, 1f);
    static readonly Color btnGreen  = new Color(0.10f, 0.60f, 0.30f, 1f);
    static readonly Color btnOrange = new Color(0.65f, 0.35f, 0.00f, 1f);
    static readonly Color btnGrey   = new Color(0.25f, 0.25f, 0.25f, 1f);

    [SerializeField] Text   structLabel;
    [SerializeField] Text   structIndexLabel;
    [SerializeField] Text   frameLabel;
    [SerializeField] Text   speedLabel;
    [SerializeField] Text   playBtnLabel;
    [SerializeField] Text   loopBtnLabel;
    [SerializeField] Image  playBtnImg;
    [SerializeField] Image  loopBtnImg;
    [SerializeField] Button playBtn;
    [SerializeField] Button loopBtn;
    [SerializeField] Button structPrevBtn;
    [SerializeField] Button structNextBtn;

    // Botones de mantener pulsado para +/- velocidad. HoldButtonHelper.onHold es
    // una Action de código (no UnityEvent), se cablea en Start().
    [SerializeField] HoldButtonHelper speedDownHold;
    [SerializeField] HoldButtonHelper speedUpHold;

    [SerializeField] Button morphBtn;
    [SerializeField] Image  morphBtnImg;
    [SerializeField] Text   morphBtnLabel;
    [SerializeField] Button morphQualityBtn;
    [SerializeField] Image  morphQualityBtnImg;
    [SerializeField] Button morphPhysicalBtn;
    [SerializeField] Image  morphPhysicalBtnImg;

    // Escala de velocidades: pasos finos en el rango lento, gruesos en el rápido
    static readonly float[] speedSteps = {
        0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 3f, 5f, 10f, 20f, 30f
    };
    int speedIdx = 4;  // arranca en 1 fps (índice 4)
    float speed   => speedSteps[speedIdx];
    bool  looping = true;

    // Índice de la proteína seleccionada para animar
    int targetStructIdx = 0;

    bool        smoothPlay    = false;
    int         lerpFromFrame = 0;
    int         lerpToFrame   = 1;
    float       lerpT         = 0f;
    Vector3[]   lerpBuffer;
    Vector3[][] modelPosCache;

    bool  manualPlay  = false;
    float manualTimer = 0f;
    float morphPlayheadFrame = 0f;

    bool _morphActive = false;

    int _repSkipCounter = 0;
    int _repSkipRate    = 1;

    // Restore-guard: if protein becomes invisible during playback, restore hb rep
    int  _visCheckFrame = 0;
    bool _hadVisibleRep = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start() {
        if (speedDownHold) speedDownHold.onHold = OnSpeedDown;
        if (speedUpHold)   speedUpHold.onHold   = OnSpeedUp;
        if (speedLabel)    speedLabel.text      = SpeedText();
    }

    void Update() {
        var sm = UnityMolMain.getStructureManager();
        int total = sm != null ? sm.loadedStructures.Count : 0;

        if (total == 0) {
            smoothPlay = false;
            manualPlay = false;
            if (structLabel)      structLabel.text      = "Sin proteína cargada";
            if (structIndexLabel) structIndexLabel.text = "";
            if (frameLabel)       frameLabel.text       = "";
            if (playBtnImg)       playBtnImg.color      = btnNormal;
            if (playBtnLabel)     playBtnLabel.text     = "▶ PLAY";
            UpdateButtonColorBlock(playBtn, btnNormal);
            return;
        }

        targetStructIdx = Mathf.Clamp(targetStructIdx, 0, total - 1);

        // Actualiza flechas de selección
        bool multiStruct = total > 1;
        if (structPrevBtn != null) {
            structPrevBtn.interactable = multiStruct;
            UpdateButtonColorBlock(structPrevBtn, multiStruct ? btnNormal : btnGrey);
        }
        if (structNextBtn != null) {
            structNextBtn.interactable = multiStruct;
            UpdateButtonColorBlock(structNextBtn, multiStruct ? btnNormal : btnGrey);
        }

        var s = GetCurrentTarget(sm);
        if (structLabel)
            structLabel.text = s != null ? s.name : "-";
        if (structIndexLabel)
            structIndexLabel.text = total > 1 ? $"{targetStructIdx + 1}/{total}" : "";

        var anim = GetAnimatedStructure(sm);
        if (anim == null || anim != s) {
            // La proteína seleccionada no tiene animación todavía
            if (frameLabel)   frameLabel.text  = "Sin animación  →  pulsa PLAY";
            if (playBtnImg)   playBtnImg.color = btnNormal;
            if (playBtnLabel) playBtnLabel.text = "▶ PLAY";
            UpdateButtonColorBlock(playBtn, btnNormal);
            return;
        }

        if (_morphActive) {
            // Morph frames advance via s.setModel(), which always does a full
            // representation rebuild (expensive, especially right after a
            // RÍGIDO/FÍSICO morph). Stepping ±1 frame per interval like the
            // branch below would, when a single setModel() call takes longer
            // than the interval, make Update() fall further and further
            // behind in real time while still reporting the requested speed -
            // it looks "stuck at 1 fps" no matter what speed says. Instead,
            // jump straight to the frame that should be showing right now
            // (computed from elapsed time), so at most one setModel() call
            // happens per Update() and playback position stays honest even
            // when the achievable visual rate is lower than requested.
            if (manualPlay && anim.modelFrames != null && anim.modelFrames.Count > 0) {
                int totalFrames = anim.modelFrames.Count;
                morphPlayheadFrame += Time.deltaTime * speed;
                int targetFrame;
                if (looping) {
                    float wrapped = morphPlayheadFrame % totalFrames;
                    if (wrapped < 0) wrapped += totalFrames;
                    targetFrame = Mathf.FloorToInt(wrapped);
                } else {
                    targetFrame = Mathf.Min(Mathf.FloorToInt(morphPlayheadFrame), totalFrames - 1);
                }
                if (targetFrame != anim.currentFrameId) anim.setModel(targetFrame);
            } else {
                morphPlayheadFrame = anim.currentFrameId; // stay in sync while paused/stepped manually
            }
        } else if (manualPlay && (anim.trajPlayer == null || !anim.trajPlayer.play)) {
            manualTimer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.1f, speed);
            if (manualTimer >= interval) { manualTimer = 0f; StepFrame(anim, true); }
        }

        // Auto-restore: if the protein becomes invisible while playing (e.g. user
        // switched to cartoon via the native VR menu and it rendered nothing for a
        // single-residue chain), bring back the hyperball representation.
        if (smoothPlay || manualPlay) {
            bool vis = HasVisibleFullRep(anim);
            if (vis) {
                _hadVisibleRep = true;
            } else if (_hadVisibleRep && ++_visCheckFrame >= 3) {
                _visCheckFrame = 0;
                _hadVisibleRep = false;
                // Restore only the full-protein rep — do NOT use showAs("hb") which
                // would also wipe any partial-selection rep the user intentionally changed.
                string fullSel = anim.ToSelectionName();
                APIPython.showSelection(fullSel, "hb");
            }
        } else {
            _visCheckFrame = 0;
        }

        int cur = GetCurrentFrame(anim);
        int tot = GetTotalFrames(anim);
        if (frameLabel) frameLabel.text = $"Frame {cur + 1} / {tot}";

        bool playing = IsCurrentlyPlaying(anim);
        Color playColor = playing ? btnGreen : btnNormal;
        if (playBtnImg)   playBtnImg.color  = playColor;
        if (playBtnLabel) playBtnLabel.text  = playing ? "⏸ PAUSA" : "▶ PLAY";
        UpdateButtonColorBlock(playBtn, playColor);
    }

    // ── Selección de proteína ─────────────────────────────────────────────────

    UnityMolStructure GetCurrentTarget(UnityMolStructureManager sm = null) {
        if (sm == null) sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return null;
        targetStructIdx = Mathf.Clamp(targetStructIdx, 0, sm.loadedStructures.Count - 1);
        return sm.loadedStructures[targetStructIdx];
    }

    public void OnStructPrev() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return;
        StopAll();
        targetStructIdx = (targetStructIdx - 1 + sm.loadedStructures.Count) % sm.loadedStructures.Count;
    }

    public void OnStructNext() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return;
        StopAll();
        targetStructIdx = (targetStructIdx + 1) % sm.loadedStructures.Count;
    }

    void StopAll() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null) return;
        foreach (var s in sm.loadedStructures) {
            if (s.trajPlayer   != null) s.trajPlayer.play   = false;
            if (s.modelsPlayer != null) s.modelsPlayer.play = false;
        }
        smoothPlay   = false;
        manualPlay   = false;
        _morphActive = false;
    }

    public void Pause() => StopAll();

    static void UpdateButtonColorBlock(Button btn, Color normalColor) {
        if (btn == null) return;
        var cb = btn.colors;
        cb.normalColor      = normalColor;
        cb.highlightedColor = Color.Lerp(normalColor, Color.white, 0.25f);
        cb.pressedColor     = Color.Lerp(normalColor, Color.black, 0.30f);
        cb.selectedColor    = normalColor;
        cb.fadeDuration     = 0.1f;
        btn.colors = cb;
    }
}
}
