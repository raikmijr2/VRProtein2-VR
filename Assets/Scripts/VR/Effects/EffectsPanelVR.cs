using UnityEngine;
using UnityEngine.UI;
using UMol.API;

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
        GameObject canvasGO = VRUIFactory.CreateWorldSpaceCanvas("EffectsPanelVR", spawnPosition, new Vector2(PanelW, panelH));
        canvasGO.transform.rotation = Quaternion.identity;

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

        Slider sl = VRUIFactory.CreateSlider(rowGO.transform, min, max, current,
            new Vector2(0.42f, 0.1f), new Vector2(0.72f, 0.9f));
        sl.onValueChanged.AddListener(v => {
            valText.text = v.ToString("F2");
            onChange(v);
        });

        curY += SliderH + Pad * 0.5f;
    }

    void MakeAnchoredText(Transform parent, string name, string text,
                          int size, FontStyle style, Color color, TextAnchor align,
                          Vector2 anchorMin, Vector2 anchorMax,
                          Vector2 offsetMin, Vector2 offsetMax) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t      = go.AddComponent<Text>();
        t.text      = text;
        t.font      = VRUIFactory.GetFont();
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
        t.font      = VRUIFactory.GetFont();
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

}
}
