using UnityEngine;
using UnityEngine.UI;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

/// <summary>
/// Shared builders for the hand-coded runtime UI used by the VR panels
/// (world-space canvas bootstrap, buttons, labels, input fields...). Extracted
/// from near-identical copies that used to live independently in each panel
/// file (PDBLoaderUI, RepresentationSwitcherUI, AnimationPlayerUI, etc.) —
/// grown incrementally as each panel is migrated onto it.
/// </summary>
public static class VRUIFactory {

    public static Font GetFont() =>
        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
        Resources.GetBuiltinResource<Font>("Arial.ttf");

    /// <summary>
    /// Creates the standard grabbable world-space canvas root every VR panel
    /// starts from: Canvas + CanvasScaler + GraphicRaycaster + CanvasRaycastTarget
    /// (so the VIU controller ray can hit it) + PointerMoveUI (grip-to-grab) at
    /// the project's standard panel scale.
    /// </summary>
    public static GameObject CreateWorldSpaceCanvas(string name, Vector3 spawnPosition, Vector2 size) {
        var go = new GameObject(name);
        go.transform.position = spawnPosition;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<GraphicRaycaster>();
        go.AddComponent<CanvasRaycastTarget>();
        go.AddComponent<PointerMoveUI>().moveParent = false;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        go.transform.localScale = Vector3.one * 0.003f;
        return go;
    }

    /// <summary>Full-rect background image filling its parent.</summary>
    public static Image CreateBackgroundImage(Transform parent, Color color) {
        var go = new GameObject("BG");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return img;
    }

    /// <summary>Thin horizontal divider line, centered in its parent.</summary>
    public static Image CreateSeparator(Transform parent, Vector2 pos, float width, float height = 2f) {
        var go = new GameObject("Sep");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.15f);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = pos;
        return img;
    }

    /// <summary>Centered (anchor 0.5,0.5) text label, white by default.</summary>
    public static Text CreateCenteredLabel(Transform parent, string text, float width, float height,
            int fontSize, Vector2 pos, FontStyle style = FontStyle.Normal,
            Color? color = null, TextAnchor alignment = TextAnchor.MiddleCenter) {
        var go = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont(); t.fontSize = fontSize; t.fontStyle = style;
        t.color = color ?? Color.white; t.alignment = alignment;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = pos;
        return t;
    }

    /// <summary>
    /// Single-line input field with a separate gray placeholder hint.
    /// initialText is what the field shows/edits; placeholderHint is the
    /// hint text shown by Unity's InputField while the field is empty.
    /// backgroundColor/placeholderColor/textFontSize default to match the
    /// original PDBLoaderUI look; pass overrides for a differently-themed
    /// panel (e.g. SequenceSelectorUI's greenish, integer-only inputs).
    /// </summary>
    public static InputField CreateInputField(Transform parent, float width, float height, Vector2 pos,
            string initialText, string placeholderHint,
            Color? backgroundColor = null, Color? placeholderColor = null, int textFontSize = 20,
            InputField.ContentType contentType = InputField.ContentType.Standard,
            TextAnchor textAlignment = TextAnchor.MiddleCenter) {
        var go = new GameObject("Input");
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = backgroundColor ?? new Color(0.15f, 0.15f, 0.22f, 1f);
        var input = go.AddComponent<InputField>();
        input.contentType = contentType;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(width, height);
        r.anchoredPosition = pos;

        var tGO = new GameObject("Text");
        tGO.transform.SetParent(go.transform, false);
        var t = tGO.AddComponent<Text>();
        t.font = GetFont(); t.fontSize = textFontSize; t.color = Color.white;
        t.alignment = textAlignment;
        var tr = tGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(6, 0); tr.offsetMax = new Vector2(-6, 0);
        input.textComponent = t;
        input.text = initialText;

        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(go.transform, false);
        var ph = phGO.AddComponent<Text>();
        ph.font = GetFont(); ph.fontSize = 18; ph.fontStyle = FontStyle.Italic;
        ph.color = placeholderColor ?? new Color(0.6f, 0.6f, 0.6f, 0.8f);
        ph.text = placeholderHint; ph.alignment = textAlignment;
        var phr = phGO.GetComponent<RectTransform>();
        phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
        phr.offsetMin = new Vector2(6, 0); phr.offsetMax = new Vector2(-6, 0);
        input.placeholder = ph;

        return input;
    }

    /// <summary>Solid-color button with a bold centered label, standard hover/press tint.</summary>
    public static Button CreateButton(Transform parent, string label, Vector2 size, Vector2 pos, Color color,
            int fontSize = 16, FontStyle style = FontStyle.Bold, float highlightBlend = 0.25f) {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, highlightBlend);
        cb.pressedColor     = Color.Lerp(color, Color.black, 0.30f);
        btn.colors = cb;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size; r.anchoredPosition = pos;

        var tGO = new GameObject("L");
        tGO.transform.SetParent(go.transform, false);
        var t = tGO.AddComponent<Text>();
        t.text = label; t.font = GetFont(); t.fontSize = fontSize; t.fontStyle = style;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        var tr = tGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = tr.offsetMax = Vector2.zero;
        return btn;
    }
}
}
