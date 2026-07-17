using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using UMol;
using UMol.API;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

public class AnimationPlayerUI : MonoBehaviour {

    [Header("Posición inicial del panel en el mundo")]
    public Vector3 spawnPosition = new Vector3(0.7f, 1.4f, 1.2f);

    static readonly Color bgColor   = new Color(0.06f, 0.06f, 0.10f, 0.95f);
    static readonly Color btnNormal = new Color(0.18f, 0.35f, 0.72f, 1f);
    static readonly Color btnGreen  = new Color(0.10f, 0.60f, 0.30f, 1f);
    static readonly Color btnOrange = new Color(0.65f, 0.35f, 0.00f, 1f);
    static readonly Color btnGrey   = new Color(0.25f, 0.25f, 0.25f, 1f);

    Text   structLabel;
    Text   structIndexLabel;
    Text   frameLabel;
    Text   speedLabel;
    Text   playBtnLabel;
    Text   loopBtnLabel;
    Image  playBtnImg;
    Image  loopBtnImg;
    Button playBtn;
    Button loopBtn;
    Button structPrevBtn;
    Button structNextBtn;

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

    int _repSkipCounter = 0;
    int _repSkipRate    = 1;

    // Restore-guard: if protein becomes invisible during playback, restore hb rep
    int  _visCheckFrame = 0;
    bool _hadVisibleRep = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start() => BuildPanel();

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

        if (smoothPlay) UpdateSmooth(anim);
        else if (manualPlay && IsXTC(anim) && anim.trajPlayer == null) {
            manualTimer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.1f, speed);
            if (manualTimer >= interval) { manualTimer = 0f; anim.trajNext(true, looping); }
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

        int cur = smoothPlay ? lerpFromFrame : GetCurrentFrame(anim);
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

    void OnStructPrev() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return;
        StopAll();
        targetStructIdx = (targetStructIdx - 1 + sm.loadedStructures.Count) % sm.loadedStructures.Count;
    }

    void OnStructNext() {
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
        smoothPlay = false;
        manualPlay = false;
    }

    public void Pause() => StopAll();

    // ── Interpolación ─────────────────────────────────────────────────────────

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
        if (IsXTC(s))         return s.xdr.CurrentFrame;
        if (s.trajectoryMode) return s.currentFrameId;
        return s.currentModelId;
    }

    int GetTotalFrames(UnityMolStructure s) {
        if (IsXTC(s))                                      return s.xdr.NumberFrames;
        if (s.trajectoryMode && s.modelFrames != null)     return s.modelFrames.Count;
        return s.models != null ? s.models.Count : 1;
    }

    void StepFrame(UnityMolStructure s, bool fwd) {
        if (IsXTC(s)) s.trajNext(fwd, looping);
        else          s.modelNext(fwd, looping);
    }

    // ── Callbacks de botones ──────────────────────────────────────────────────

    void OnPlayPause() {
        var sm = UnityMolMain.getStructureManager();
        var target = GetCurrentTarget(sm);

        if (target == null) {
            return;
        }

        var s = GetAnimatedStructure(sm);

        // Sin animación todavía → generar sintética y arrancar
        if (s == null) {
            if (SyntheticAnimator.GenerateFor(target)) {
                StartSmoothPlay(target);
                return;
            }
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
            if (smoothPlay) StopSmoothPlay(s);
            else            StartSmoothPlay(s);
            return;
        }

    }

    void OnPrev() {
        var s = GetAnimatedStructure();
        if (s == null) return;
        StopSmoothPlay(s); manualPlay = false;
        if (s.trajPlayer   != null) s.trajPlayer.play   = false;
        if (s.modelsPlayer != null) s.modelsPlayer.play = false;
        StepFrame(s, false);
    }

    void OnNext() {
        var s = GetAnimatedStructure();
        if (s == null) return;
        StopSmoothPlay(s); manualPlay = false;
        if (s.trajPlayer   != null) s.trajPlayer.play   = false;
        if (s.modelsPlayer != null) s.modelsPlayer.play = false;
        StepFrame(s, true);
    }

    // ── Hold-to-repeat ────────────────────────────────────────────────────────

    void AddHoldBehavior(GameObject go, System.Action action) {
        var h = go.AddComponent<HoldButtonHelper>();
        h.onHold = action;
    }

    void OnSpeedDown() { speedIdx = Mathf.Max(0, speedIdx - 1);                       ApplySpeed(); if (speedLabel) speedLabel.text = SpeedText(); }
    void OnSpeedUp()   { speedIdx = Mathf.Min(speedSteps.Length - 1, speedIdx + 1);  ApplySpeed(); if (speedLabel) speedLabel.text = SpeedText(); }

    void ApplySpeed() {
        var s = GetAnimatedStructure();
        if (s == null) return;
        if (s.trajPlayer   != null) s.trajPlayer.trajFramerate    = speed;
        if (s.modelsPlayer != null) s.modelsPlayer.modelFramerate = speed;
    }

    void OnLoopToggle() {
        looping = !looping;
        var s = GetAnimatedStructure();
        if (s?.trajPlayer   != null) s.trajPlayer.looping   = looping;
        if (s?.modelsPlayer != null) s.modelsPlayer.looping = looping;
        Color c = looping ? btnOrange : btnNormal;
        if (loopBtnImg)   loopBtnImg.color = c;
        UpdateButtonColorBlock(loopBtn, c);
        if (loopBtnLabel) loopBtnLabel.text = looping ? "Loop: ON" : "Loop: OFF";
    }

    string SpeedText() => speed < 1f ? $"{speed:F2} fps" : $"{speed:F0} fps";

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

    // ── Construcción del panel ────────────────────────────────────────────────

    void BuildPanel() {
        const float W = 420f, H = 380f;

        var canvasGO = new GameObject("AnimationPlayerPanel");
        canvasGO.transform.position = spawnPosition;
        canvasGO.transform.rotation = Quaternion.identity;

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGO.AddComponent<CanvasRaycastTarget>();
        canvasGO.AddComponent<PointerMoveUI>().moveParent = false;

        var canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(W, H);
        canvasGO.transform.localScale = Vector3.one * 0.003f;

        AddFullImage(canvasGO.transform, "Bg", bgColor);

        AddLabelFromTop(canvasGO.transform, "Title",
            "ANIMACIÓN MOLECULAR", 24, FontStyle.Bold,
            0f, 55f, TextAnchor.MiddleCenter);

        AddSeparatorFromTop(canvasGO.transform, "Sep1", 57f);

        // ── Fila de selección de proteína: ◄  NombreProteina (1/2)  ► ──────────
        const float selRowY = 62f, selRowH = 36f;
        const float arrowW  = 40f;

        var prevGO = MakeButtonGO(canvasGO.transform, "BtnStructPrev", "◄",
            Vector2.zero, new Vector2(arrowW, selRowH), () => OnStructPrev());
        structPrevBtn = prevGO.GetComponent<Button>();
        SetAnchoredFromTop(prevGO, selRowY, selRowH, 0f, arrowW);

        var nextGO = MakeButtonGO(canvasGO.transform, "BtnStructNext", "►",
            Vector2.zero, new Vector2(arrowW, selRowH), () => OnStructNext());
        structNextBtn = nextGO.GetComponent<Button>();
        SetAnchoredFromTop(nextGO, selRowY, selRowH, W - arrowW, arrowW);

        // Nombre de la proteína (centro)
        structLabel = AddCustomLabel(canvasGO.transform, "StructLabel",
            "Sin proteína", 15, FontStyle.Bold,
            selRowY, selRowH, arrowW + 4f, W - arrowW * 2 - 8f, TextAnchor.MiddleCenter);

        // Índice (esquina superior derecha del label)
        structIndexLabel = AddCustomLabel(canvasGO.transform, "StructIdx",
            "", 13, FontStyle.Normal,
            selRowY, selRowH, W - arrowW - 50f, 46f, TextAnchor.MiddleRight);

        AddSeparatorFromTop(canvasGO.transform, "Sep2", selRowY + selRowH + 2f);

        frameLabel = AddLabelFromTop(canvasGO.transform, "FrameLabel",
            "", 20, FontStyle.Bold,
            selRowY + selRowH + 6f, 36f, TextAnchor.MiddleCenter);

        AddSeparatorFromTop(canvasGO.transform, "Sep3", selRowY + selRowH + 46f);

        // ── Botones de control ────────────────────────────────────────────────
        const float ctrlBtnW = 118f, ctrlBtnH = 58f;
        const float ctrlY    = -8f;

        MakeButton(canvasGO.transform, "BtnPrev", "◄◄ Ant",
            new Vector2(-134f, ctrlY), new Vector2(ctrlBtnW, ctrlBtnH), () => OnPrev());

        var playGO = MakeButtonGO(canvasGO.transform, "BtnPlay", "▶ PLAY",
            new Vector2(0f, ctrlY), new Vector2(ctrlBtnW, ctrlBtnH), () => OnPlayPause());
        playBtnImg   = playGO.GetComponent<Image>();
        playBtn      = playGO.GetComponent<Button>();
        playBtnLabel = playGO.GetComponentInChildren<Text>();

        MakeButton(canvasGO.transform, "BtnNext", "Sig ►►",
            new Vector2(134f, ctrlY), new Vector2(ctrlBtnW, ctrlBtnH), () => OnNext());

        const float spdBtnW = 52f, spdBtnH = 50f;
        const float spdY    = ctrlY - ctrlBtnH / 2f - 10f - spdBtnH / 2f;

        var spdDownGO = MakeButtonGO(canvasGO.transform, "BtnSpeedDown", "−",
            new Vector2(-90f, spdY), new Vector2(spdBtnW, spdBtnH), () => OnSpeedDown());
        AddHoldBehavior(spdDownGO, OnSpeedDown);

        speedLabel = AddCenteredLabel(canvasGO.transform, "SpeedLabel",
            SpeedText(), 20, FontStyle.Bold,
            new Vector2(0f, spdY), new Vector2(90f, spdBtnH));

        var spdUpGO = MakeButtonGO(canvasGO.transform, "BtnSpeedUp", "+",
            new Vector2(90f, spdY), new Vector2(spdBtnW, spdBtnH), () => OnSpeedUp());
        AddHoldBehavior(spdUpGO, OnSpeedUp);

        const float loopY = spdY - spdBtnH / 2f - 10f - 25f;

        var loopGO = MakeButtonGO(canvasGO.transform, "BtnLoop", "Loop: ON",
            new Vector2(0f, loopY), new Vector2(180f, 50f), () => OnLoopToggle());
        loopBtnImg   = loopGO.GetComponent<Image>();
        loopBtnImg.color = btnOrange;
        loopBtn      = loopGO.GetComponent<Button>();
        loopBtnLabel = loopGO.GetComponentInChildren<Text>();
        UpdateButtonColorBlock(loopBtn, btnOrange);
    }

    // ── Helpers de UI ─────────────────────────────────────────────────────────

    static void SetAnchoredFromTop(GameObject go, float yFromTop, float height,
                                   float xLeft, float width) {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.offsetMin = new Vector2(xLeft,           -(yFromTop + height));
        rt.offsetMax = new Vector2(xLeft + width,   -yFromTop);
    }

    Text AddCustomLabel(Transform parent, string name, string text,
                        int fontSize, FontStyle style,
                        float yFromTop, float height,
                        float xLeft, float width, TextAnchor align) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont();
        t.fontSize = fontSize; t.fontStyle = style;
        t.color = Color.white; t.alignment = align;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        rt.offsetMin = new Vector2(xLeft,         -(yFromTop + height));
        rt.offsetMax = new Vector2(xLeft + width, -yFromTop);
        return t;
    }

    void MakeButton(Transform parent, string name, string label,
                    Vector2 anchoredPos, Vector2 size, System.Action callback) {
        MakeButtonGO(parent, name, label, anchoredPos, size, callback);
    }

    GameObject MakeButtonGO(Transform parent, string name, string label,
                             Vector2 anchoredPos, Vector2 size, System.Action callback) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = btnNormal;
        var btn = go.AddComponent<Button>();
        UpdateButtonColorBlock(btn, btnNormal);
        btn.onClick.AddListener(() => callback());
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        var t = textGO.AddComponent<Text>();
        t.text = label; t.font = GetFont();
        t.fontSize = 20; t.fontStyle = FontStyle.Bold;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        return go;
    }

    Text AddLabelFromTop(Transform parent, string name, string text,
                         int fontSize, FontStyle style,
                         float yFromTop, float height, TextAnchor align,
                         float padH = 0f) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont();
        t.fontSize = fontSize; t.fontStyle = style;
        t.color = Color.white; t.alignment = align;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(padH,  -(yFromTop + height));
        rt.offsetMax = new Vector2(-padH, -yFromTop);
        return t;
    }

    Text AddCenteredLabel(Transform parent, string name, string text,
                          int fontSize, FontStyle style,
                          Vector2 anchoredPos, Vector2 size) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont();
        t.fontSize = fontSize; t.fontStyle = style;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return t;
    }

    static void AddFullImage(Transform parent, string name, Color color) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void AddSeparatorFromTop(Transform parent, string name, float yFromTop) {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(10f,  -(yFromTop + 2f));
        rt.offsetMax = new Vector2(-10f, -yFromTop);
    }

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

    static Font GetFont() {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

/// <summary>
/// Añadir a un botón para que su acción se repita mientras se mantiene pulsado.
/// Usa IPointerDownHandler/UpHandler para compatibilidad con VIU.
/// </summary>
public class HoldButtonHelper : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {

    public System.Action onHold;
    bool _active = false;

    public void OnPointerDown(PointerEventData e) {
        _active = true;
        StartCoroutine(HoldRoutine());
    }

    public void OnPointerUp(PointerEventData e)   { _active = false; }
    public void OnPointerExit(PointerEventData e) { _active = false; }

    IEnumerator HoldRoutine() {
        yield return new WaitForSeconds(0.35f);
        while (_active) {
            onHold?.Invoke();
            yield return new WaitForSeconds(0.10f);
        }
    }
}
}
}
