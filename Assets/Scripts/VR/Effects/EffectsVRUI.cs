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
        font = VRUIFactory.GetFont();

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
        Slider sl = VRUIFactory.CreateSlider(row.transform, min, max, current);
        sl.onValueChanged.AddListener(v => {
            vt.text = v.ToString("F2");
            onChange(v);
        });
        LayoutElement sle = sl.gameObject.AddComponent<LayoutElement>();
        sle.preferredWidth = 100f;
        sle.flexibleWidth  = 1f;
    }
}
}
