using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UMol.API;

namespace UMol {

public partial class RepresentationSwitcherUI {

    // ── Panel de coloreado ────────────────────────────────────────────────

    public void OnColorToggle() {
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

    public void ApplySSColoring() {
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

    public void ApplyCPKColoring()             => ColorAllReps((sel, t) => APIPython.colorByAtom(sel, t));
    public void ApplyHydrophobicityColoring()  => ColorAllReps((sel, t) => APIPython.colorByHydrophobicity(sel, t));
    public void ApplyRainbowColoring()         => ColorAllReps((sel, t) => APIPython.colorByResnum(sel, t));
}
}
