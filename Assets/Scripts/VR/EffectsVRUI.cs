using UnityEngine;
using UnityEngine.UI;
using UMol.API;

namespace UMol {

public class EffectsVRUI : MonoBehaviour {

    static readonly Color secBg    = new Color(0.08f, 0.08f, 0.12f, 1f);
    static readonly Color lblColor = new Color(0.55f, 0.80f, 1.00f, 1f);
    static readonly Color rowBg    = new Color(0.13f, 0.13f, 0.18f, 1f);

    Font font;

    void Start() {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        GameObject contentGO = GameObject.Find("CanvasMainUIVR/Selection Scroll View/Viewport/Content");
        if (contentGO == null) {
            return;
        }
        BuildSection(contentGO.transform);
    }

    void BuildSection(Transform content) {
        GameObject section = MakeVerticalContainer("EffectsVRSection", content, secBg, 6, 6);

        MakeLabel(section.transform, "── ILUMINACIÓN ──", 13, lblColor, 24f, FontStyle.Bold);

        MakeSliderRow(section.transform, "Intensidad luz",   0f, 3f, 1f,
            v => APIPython.setDirLightIntensity(v));
        MakeSliderRow(section.transform, "Luz ambiente",     0f, 3f, 1f,
            v => APIPython.setAmbientLightIntensity(v));
        MakeSliderRow(section.transform, "Sombras",          0f, 1f, 0.5f,
            v => APIPython.setDirLightShadow(v));
    }

    // ── Builders de UI ───────────────────────────────────────────────────────

    GameObject MakeVerticalContainer(string name, Transform parent, Color bg, int padH, int padV) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = bg;

        VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing             = 3f;
        vlg.padding             = new RectOffset(padH, padH, padV, padV);
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }

    void MakeLabel(Transform parent, string text, int size, Color color, float height,
                   FontStyle style = FontStyle.Normal) {
        GameObject go = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        Text t      = go.AddComponent<Text>();
        t.text      = text;
        t.font      = font;
        t.fontSize  = size;
        t.fontStyle = style;
        t.color     = color;
        t.alignment = TextAnchor.MiddleCenter;
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth   = 1f;
    }

    void MakeSliderRow(Transform parent, string label, float min, float max, float current,
                       System.Action<float> onChange) {
        GameObject row = new GameObject("Row_" + label);
        row.transform.SetParent(parent, false);
        row.AddComponent<Image>().color = rowBg;

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing             = 4f;
        hlg.padding             = new RectOffset(4, 4, 2, 2);
        hlg.childControlHeight  = true;
        hlg.childControlWidth   = false;
        hlg.childForceExpandHeight = true;

        LayoutElement rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 28f;
        rowLe.flexibleWidth   = 1f;

        // Etiqueta
        GameObject lgo = new GameObject("Lbl");
        lgo.transform.SetParent(row.transform, false);
        Text lt      = lgo.AddComponent<Text>();
        lt.text      = label;
        lt.font      = font;
        lt.fontSize  = 11;
        lt.color     = Color.white;
        lt.alignment = TextAnchor.MiddleLeft;
        LayoutElement lle = lgo.AddComponent<LayoutElement>();
        lle.preferredWidth = 90f;

        // Valor
        GameObject vgo = new GameObject("Val");
        vgo.transform.SetParent(row.transform, false);
        Text vt      = vgo.AddComponent<Text>();
        vt.text      = current.ToString("F2");
        vt.font      = font;
        vt.fontSize  = 11;
        vt.color     = Color.white;
        vt.alignment = TextAnchor.MiddleCenter;
        LayoutElement vle = vgo.AddComponent<LayoutElement>();
        vle.preferredWidth = 36f;

        // Slider
        GameObject sliderGO = BuildSlider(row.transform, min, max, current);
        Slider sl = sliderGO.GetComponent<Slider>();
        sl.onValueChanged.AddListener(v => {
            vt.text = v.ToString("F2");
            onChange(v);
        });
        LayoutElement sle = sliderGO.AddComponent<LayoutElement>();
        sle.preferredWidth = 100f;
        sle.flexibleWidth  = 1f;
    }

    static GameObject BuildSlider(Transform parent, float min, float max, float value) {
        GameObject root = new GameObject("Slider");
        root.transform.SetParent(parent, false);
        root.AddComponent<Image>().color = Color.clear;
        Slider sl = root.AddComponent<Slider>();

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
        return root;
    }
}
}
