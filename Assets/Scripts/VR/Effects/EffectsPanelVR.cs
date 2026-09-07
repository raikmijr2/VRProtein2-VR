using UnityEngine;
using UnityEngine.UI;
using UMol.API;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

public class EffectsPanelVR : MonoBehaviour {

    [Header("Posición inicial en el mundo")]
    public Vector3 spawnPosition = new Vector3(0.7f, 1.4f, 1.2f);

    static readonly Color bgColor      = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    static readonly Color lblColor     = new Color(0.55f, 0.80f, 1.00f, 1f);
    static readonly Color rowBg        = new Color(0.13f, 0.13f, 0.20f, 1f);

    const float PanelW  = 400f;
    const float TitleH  = 55f;
    const float SecLblH = 28f;
    const float SliderH = 34f;
    const float Pad     = 10f;

    void Start() {
        BuildPanel();
    }

    void BuildPanel() {
        float panelH = TitleH
                     + (SecLblH + SliderH * 3 + Pad * 3)
                     + Pad * 2;

        // ── Canvas ────────────────────────────────────────────────────────
        GameObject canvasGO = new GameObject("EffectsPanelVR");
        canvasGO.transform.position = spawnPosition;
        canvasGO.transform.rotation = Quaternion.identity;

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGO.AddComponent<CanvasRaycastTarget>();

        PointerMoveUI mover = canvasGO.AddComponent<PointerMoveUI>();
        mover.moveParent = false;

        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(PanelW, panelH);
        canvasGO.transform.localScale = Vector3.one * 0.003f;

        // ── Fondo ─────────────────────────────────────────────────────────
        AddImage(canvasGO.transform, "Background", bgColor,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── Título ────────────────────────────────────────────────────────
        MakeAnchoredText(canvasGO.transform, "Title", "ILUMINACIÓN",
            26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -TitleH), new Vector2(0f, 0f));

        AddImage(canvasGO.transform, "Sep", new Color(1f, 1f, 1f, 0.15f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(Pad, -TitleH), new Vector2(-Pad, -(TitleH - 2f)));

        float curY = TitleH + Pad;

        MakeFloatLabel(canvasGO.transform, "CONTROLES DE LUZ", ref curY);

        MakeSliderRow(canvasGO.transform, "Intensidad luz",   0f, 3f, 1f,   ref curY,
            v => APIPython.setDirLightIntensity(v));
        MakeSliderRow(canvasGO.transform, "Luz ambiente",     0f, 3f, 1f,   ref curY,
            v => APIPython.setAmbientLightIntensity(v));
        MakeSliderRow(canvasGO.transform, "Sombras",          0f, 1f, 0.5f, ref curY,
            v => APIPython.setDirLightShadow(v));
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    void MakeFloatLabel(Transform parent, string text, ref float curY) {
        MakeAnchoredText(parent, "Sec_" + text, text,
            16, FontStyle.Bold, lblColor, TextAnchor.MiddleLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(Pad, -(curY + SecLblH)),
            new Vector2(-Pad, -curY));
        curY += SecLblH + Pad * 0.5f;
    }

    void MakeSliderRow(Transform parent, string label, float min, float max, float current,
                       ref float curY, System.Action<float> onChange) {
        GameObject rowGO = new GameObject("Row_" + label);
        rowGO.transform.SetParent(parent, false);
        rowGO.AddComponent<Image>().color = rowBg;

        RectTransform rowRT = rowGO.GetComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.offsetMin = new Vector2(Pad, -(curY + SliderH));
        rowRT.offsetMax = new Vector2(-Pad, -curY);

        Text valText = MakeChildText(rowGO.transform, "Val",
            current.ToString("F2"), 15, FontStyle.Normal,
            new Color(0.8f, 1f, 0.8f, 1f), TextAnchor.MiddleCenter,
            new Vector2(0.72f, 0f), new Vector2(1f, 1f));

        MakeChildText(rowGO.transform, "Lbl", label, 16, FontStyle.Normal,
            Color.white, TextAnchor.MiddleLeft,
            new Vector2(0f, 0f), new Vector2(0.42f, 1f));

        Slider sl = BuildSlider(rowGO.transform, min, max, current,
            new Vector2(0.42f, 0.1f), new Vector2(0.72f, 0.9f));
        sl.onValueChanged.AddListener(v => {
            valText.text = v.ToString("F2");
            onChange(v);
        });

        curY += SliderH + Pad * 0.5f;
    }

    static Slider BuildSlider(Transform parent, float min, float max, float value,
                               Vector2 anchorMin, Vector2 anchorMax) {
        GameObject root = new GameObject("Slider");
        root.transform.SetParent(parent, false);
        root.AddComponent<Image>().color = Color.clear;
        Slider sl = root.AddComponent<Slider>();
        RectTransform rootRT = root.GetComponent<RectTransform>();
        rootRT.anchorMin = anchorMin;
        rootRT.anchorMax = anchorMax;
        rootRT.offsetMin = rootRT.offsetMax = Vector2.zero;

        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(root.transform, false);
        bg.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.3f);
        bgRT.anchorMax = new Vector2(1f, 0.7f);
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(root.transform, false);
        fillArea.AddComponent<Image>().color = Color.clear;
        RectTransform fillAreaRT = fillArea.GetComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0f, 0.3f);
        fillAreaRT.anchorMax = new Vector2(1f, 0.7f);
        fillAreaRT.offsetMin = new Vector2(5f, 0f);
        fillAreaRT.offsetMax = new Vector2(-5f, 0f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        fill.AddComponent<Image>().color = new Color(0.25f, 0.55f, 1f, 1f);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = new Vector2(0f, 1f);
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = new Vector2(10f, 0f);

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(root.transform, false);
        handleArea.AddComponent<Image>().color = Color.clear;
        RectTransform handleAreaRT = handleArea.GetComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = new Vector2(10f, 0f);
        handleAreaRT.offsetMax = new Vector2(-10f, 0f);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        handle.AddComponent<Image>().color = new Color(0.6f, 0.85f, 1f, 1f);
        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(18f, 0f);
        handleRT.anchorMin = Vector2.zero;
        handleRT.anchorMax = new Vector2(0f, 1f);

        sl.fillRect   = fillRT;
        sl.handleRect = handleRT;
        sl.direction  = Slider.Direction.LeftToRight;
        sl.minValue   = min;
        sl.maxValue   = max;
        sl.value      = value;
        return sl;
    }

    void MakeAnchoredText(Transform parent, string name, string text,
                          int size, FontStyle style, Color color, TextAnchor align,
                          Vector2 anchorMin, Vector2 anchorMax,
                          Vector2 offsetMin, Vector2 offsetMax) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t      = go.AddComponent<Text>();
        t.text      = text;
        t.font      = GetFont();
        t.fontSize  = size;
        t.fontStyle = style;
        t.color     = color;
        t.alignment = align;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    Text MakeChildText(Transform parent, string name, string text,
                       int size, FontStyle style, Color color, TextAnchor align,
                       Vector2 anchorMin, Vector2 anchorMax) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t      = go.AddComponent<Text>();
        t.text      = text;
        t.font      = GetFont();
        t.fontSize  = size;
        t.fontStyle = style;
        t.color     = color;
        t.alignment = align;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(4f, 2f);
        rt.offsetMax = new Vector2(-4f, -2f);
        return t;
    }

    static void AddImage(Transform parent, string name, Color color,
                         Vector2 anchorMin, Vector2 anchorMax,
                         Vector2 offsetMin, Vector2 offsetMax) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    static Font GetFont() {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }
}
}
