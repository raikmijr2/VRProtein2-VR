using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UMol.API;

namespace UMol {

public partial class RepresentationSwitcherUI {

    // ── Panel de coloreado ────────────────────────────────────────────────

    void BuildColorToggleButton(Transform parent, float panelW, float pad) {
        var go = new GameObject("BtnColorToggle");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        var btnColor = new Color(0.28f, 0.18f, 0.52f, 1f);
        img.color = btnColor;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(OnColorToggle);
        var cb = btn.colors;
        cb.normalColor      = btnColor;
        cb.highlightedColor = Color.Lerp(btnColor, Color.white, 0.25f);
        cb.pressedColor     = Color.Lerp(btnColor, Color.black, 0.30f);
        btn.colors = cb;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, pad + colorToggleH);
        var lGO = new GameObject("Label"); lGO.transform.SetParent(go.transform, false);
        colorToggleBtnLabel = lGO.AddComponent<Text>();
        colorToggleBtnLabel.text      = "COLOREAR  ▼";
        colorToggleBtnLabel.font      = VRUIFactory.GetFont();
        colorToggleBtnLabel.fontSize  = 20;
        colorToggleBtnLabel.fontStyle = FontStyle.Bold;
        colorToggleBtnLabel.color     = Color.white;
        colorToggleBtnLabel.alignment = TextAnchor.MiddleCenter;
        var lRT = lGO.GetComponent<RectTransform>();
        lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
        lRT.offsetMin = lRT.offsetMax = Vector2.zero;
    }

    void BuildColorSection(Transform canvasParent, float panelW, float panelH, float pad, float btnH) {
        float btnW    = (panelW - pad * 3f) / 2f;
        float sectH   = 2f * btnH + 3f * pad;
        float sectY   = -panelH / 2f - pad - sectH / 2f;

        colorSectionGO = new GameObject("ColorSection");
        colorSectionGO.transform.SetParent(canvasParent, false);
        var sectRT = colorSectionGO.AddComponent<RectTransform>();
        sectRT.anchoredPosition = new Vector2(0f, sectY);
        sectRT.sizeDelta        = new Vector2(panelW, sectH);

        // Fondo del sub-panel
        var bgGO = new GameObject("BG"); bgGO.transform.SetParent(colorSectionGO.transform, false);
        bgGO.AddComponent<Image>().color = bgColor;
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        // 4 botones de coloreado en rejilla 2×2
        (string label, string desc, System.Action action)[] colorBtns = {
            ("Estruct. 2ria", "Helix/Lámina/Coil",   ApplySSColoring),
            ("CPK Átomos",   "Color por elemento",    ApplyCPKColoring),
            ("Hidrofob.",     "Por tipo de residuo",       ApplyHydrophobicityColoring),
            ("Rainbow",       "Por núm. de residuo",  ApplyRainbowColoring),
        };

        for (int i = 0; i < colorBtns.Length; i++) {
            int row = i / 2;
            int col = i % 2;
            float x = -panelW / 2f + pad + col * (btnW + pad) + btnW / 2f;
            float y = sectH / 2f - pad - row * (btnH + pad) - btnH / 2f;
            var (lbl, dsc, act) = colorBtns[i];
            var btn = CreateColorButton(colorSectionGO.transform, lbl, dsc, x, y, btnW, btnH);
            var capturedAct = act;
            btn.onClick.AddListener(() => capturedAct());
        }

        colorSectionGO.SetActive(false);
    }

    Button CreateColorButton(Transform parent, string label, string desc,
                             float x, float y, float w, float h) {
        var btnColor = new Color(0.28f, 0.18f, 0.52f, 1f);
        return VRUIFactory.CreateTwoLineButton(parent, "CBtn_" + label, label, desc,
            new Vector2(x, y), new Vector2(w, h), btnColor, onClick: null);
    }

    void OnColorToggle() {
        colorSectionExpanded = !colorSectionExpanded;
        if (colorSectionGO) colorSectionGO.SetActive(colorSectionExpanded);
        if (colorToggleBtnLabel != null)
            colorToggleBtnLabel.text = colorSectionExpanded ? "COLOREAR  ▲" : "COLOREAR  ▼";
    }

    // Aplica colorFunc a todas las representaciones activas (itera todos los tipos de rep conocidos).
    //
    // BUG FIX: esto antes se saltaba la representación "toda la estructura" (nombre de
    // selección == s.ToSelectionName(), ej. "all_5p21") usando una lista de exclusión
    // (FullStructureSelNames) copiada de ExtractAtomsToRep, donde SÍ hace falta para evitar
    // un crash en el bond-line manager al llamar a rep.updateWithNewSelection() sobre esa
    // selección. Ninguna función de coloreado (colorByAtom/colorByHydrophobicity/
    // colorByResnum/setCartoonColorSS, verificado leyendo las 4 en Assets/Scripts/API/
    // APIPython.cs) llama a updateWithNewSelection — solo leen la selección existente y
    // recolorean sus átomos — así que la exclusión no protegía nada aquí. Efecto real: en
    // cuanto el usuario cambiaba de representación (lo que deja la rep principal con nombre
    // "all_<estructura>" vía APIPython.showAs), COLOREAR dejaba de tener ningún efecto sobre
    // la proteína entera — solo coloreaba el ligando extraído, si lo había.
    void ColorAllReps(System.Action<string, string> colorFunc) {
        var repMgr = UnityMolMain.getRepresentationManager();
        var seen   = new HashSet<string>();
        foreach (var rep in repMgr.representations) {
            if (rep.selection == null) continue;
            string sn = rep.selection.name;
            if (!seen.Add(sn)) continue;
            foreach (var (code, _, _) in repTypes)
                colorFunc(sn, code);
        }
    }

    void ApplySSColoring() {
        var repMgr = UnityMolMain.getRepresentationManager();
        var seen   = new HashSet<string>();
        foreach (var rep in repMgr.representations) {
            if (rep.selection == null) continue;
            string sn = rep.selection.name;
            if (!seen.Add(sn)) continue;
            APIPython.setCartoonColorSS(sn, "helix", new Color(1.00f, 0.00f, 0.80f)); // magenta (PyMOL)
            APIPython.setCartoonColorSS(sn, "sheet", new Color(1.00f, 1.00f, 0.00f)); // amarillo (PyMOL)
            APIPython.setCartoonColorSS(sn, "coil",  new Color(1.00f, 1.00f, 1.00f)); // blanco (PyMOL)
        }
    }

    void ApplyCPKColoring()             => ColorAllReps((sel, t) => APIPython.colorByAtom(sel, t));
    void ApplyHydrophobicityColoring()  => ColorAllReps((sel, t) => APIPython.colorByHydrophobicity(sel, t));
    void ApplyRainbowColoring()         => ColorAllReps((sel, t) => APIPython.colorByResnum(sel, t));
}
}
