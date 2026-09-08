using UnityEngine;
using UnityEngine.UI;

namespace UMol {

public partial class AnimationPlayerUI {

    // ── Construcción del panel ────────────────────────────────────────────────

    void BuildPanel() {
        const float W = 420f, H = 460f;

        var canvasGO = VRUIFactory.CreateWorldSpaceCanvas("AnimationPlayerPanel", spawnPosition, new Vector2(W, H));
        canvasGO.transform.rotation = Quaternion.identity;

        VRUIFactory.CreateBackgroundImage(canvasGO.transform, bgColor);

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
        VRUIFactory.AddHoldBehavior(spdDownGO, OnSpeedDown);

        speedLabel = VRUIFactory.CreateCenteredLabel(canvasGO.transform,
            SpeedText(), 90f, spdBtnH, 20, new Vector2(0f, spdY), FontStyle.Bold);

        var spdUpGO = MakeButtonGO(canvasGO.transform, "BtnSpeedUp", "+",
            new Vector2(90f, spdY), new Vector2(spdBtnW, spdBtnH), () => OnSpeedUp());
        VRUIFactory.AddHoldBehavior(spdUpGO, OnSpeedUp);

        const float loopY = spdY - spdBtnH / 2f - 10f - 25f;

        var loopGO = MakeButtonGO(canvasGO.transform, "BtnLoop", "Loop: ON",
            new Vector2(0f, loopY), new Vector2(180f, 50f), () => OnLoopToggle());
        loopBtnImg   = loopGO.GetComponent<Image>();
        loopBtnImg.color = btnOrange;
        loopBtn      = loopGO.GetComponent<Button>();
        loopBtnLabel = loopGO.GetComponentInChildren<Text>();
        UpdateButtonColorBlock(loopBtn, btnOrange);

        const float morphBtnH = 44f;
        const float morphBtnW = 120f;
        float morphY = loopY - 50f / 2f - 12f - morphBtnH / 2f;

        var morphGO = MakeButtonGO(canvasGO.transform, "BtnMorphLinear", "LINEAL",
            new Vector2(-135f, morphY), new Vector2(morphBtnW, morphBtnH), () => OnMorphClick());
        morphBtn      = morphGO.GetComponent<Button>();
        morphBtnImg   = morphGO.GetComponent<Image>();
        morphBtnLabel = morphGO.GetComponentInChildren<Text>();
        var morphLinearColor = new Color(0.45f, 0.18f, 0.65f, 1f);
        morphBtnImg.color = morphLinearColor;
        UpdateButtonColorBlock(morphBtn, morphLinearColor);

        var morphQGO = MakeButtonGO(canvasGO.transform, "BtnMorphRigid", "RÍGIDO",
            new Vector2(0f, morphY), new Vector2(morphBtnW, morphBtnH), () => OnMorphQualityClick());
        morphQualityBtn    = morphQGO.GetComponent<Button>();
        morphQualityBtnImg = morphQGO.GetComponent<Image>();
        var morphRigidColor = new Color(0.10f, 0.55f, 0.55f, 1f);
        morphQualityBtnImg.color = morphRigidColor;
        UpdateButtonColorBlock(morphQualityBtn, morphRigidColor);

        var morphPGO = MakeButtonGO(canvasGO.transform, "BtnMorphPhysical", "FÍSICO",
            new Vector2(135f, morphY), new Vector2(morphBtnW, morphBtnH), () => OnMorphPhysicalClick());
        morphPhysicalBtn    = morphPGO.GetComponent<Button>();
        morphPhysicalBtnImg = morphPGO.GetComponent<Image>();
        var morphPhysicalColor = new Color(0.65f, 0.40f, 0.05f, 1f);
        morphPhysicalBtnImg.color = morphPhysicalColor;
        UpdateButtonColorBlock(morphPhysicalBtn, morphPhysicalColor);
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
        t.text = text; t.font = VRUIFactory.GetFont();
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
        t.text = label; t.font = VRUIFactory.GetFont();
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
        t.text = text; t.font = VRUIFactory.GetFont();
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
}
}
