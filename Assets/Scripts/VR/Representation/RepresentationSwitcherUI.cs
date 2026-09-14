using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UMol {

/// <summary>
/// Panel VR para cambiar la representación de las proteínas cargadas.
/// Se puede agarrar con el botón Grip del mando.
/// Split across 4 files: this one (fields, Start/Update, rep-type array),
/// .RepSwitching.cs (rep-switch logic + ligand extraction), .Coloring.cs
/// (the COLOREAR sub-panel), .ProteinActions.cs (reset + export). The UI
/// hierarchy is an Editor-built prefab (see RepSwitcherPanel.prefab); this
/// script only holds the business logic and references to its parts.
/// </summary>
public partial class RepresentationSwitcherUI : MonoBehaviour {

    // Representaciones disponibles: (código API, nombre visible). El ORDEN debe
    // coincidir exactamente con el array "Buttons" asignado en el Inspector —
    // MarkActive/UpdateSurfaceBtnVisual indexan buttons[i] usando repTypes[i].
    static readonly (string code, string label, string desc)[] repTypes = {
        ("c",      "Cartoon",    "Estructura secundaria"),
        ("hb",     "HyperBall",  "Átomos y enlaces"),
        ("s",      "Superficie", "Superficie molecular"),
        ("tube",   "Tubo",       "Esqueleto backbone"),
        ("sphere", "Esferas",    "CPK / VDW"),
        ("l",      "Líneas",     "Wireframe simple"),
    };

    // Colores del panel (siguen usándose en tiempo de ejecución para teñir
    // botones según el estado: activo, seleccionado, overlay, etc.)
    static readonly Color bgColor      = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    static readonly Color btnNormal    = new Color(0.18f, 0.35f, 0.72f, 1f);
    static readonly Color btnHighlight = new Color(0.30f, 0.55f, 1.00f, 1f);
    static readonly Color btnPressed   = new Color(0.08f, 0.18f, 0.45f, 1f);
    static readonly Color btnActive    = new Color(0.10f, 0.60f, 0.30f, 1f); // verde = activo
    static readonly Color btnReset     = new Color(0.65f, 0.12f, 0.12f, 1f); // rojo = reset
    static readonly Color btnExport    = new Color(0.10f, 0.50f, 0.20f, 1f); // verde = export
    static readonly Color btnSelection = new Color(0.80f, 0.67f, 0.00f, 1f); // amarillo = selección activa
    static readonly Color btnSurface   = new Color(0.00f, 0.52f, 0.52f, 1f); // teal = overlay activo

    string currentRep = "c";
    bool   applyToSelection = false;  // false = toda la proteína, true = solo la selección

    bool   surfaceOverlayActive = false;
    const string surfaceOverlaySel = "surface_overlay";

    // Estado de extracción: representa qué reps fueron modificadas y sus átomos originales
    List<(UnityMolRepresentation rep, List<UnityMolAtom> originalAtoms, bool wasEnabled)> modifiedReps
        = new List<(UnityMolRepresentation, List<UnityMolAtom>, bool)>();
    string extractedRepCode;
    bool   hasExtractedSelection;

    // ── UI refs ───────────────────────────────────────────────────────────────

    [SerializeField] Button[] buttons;  // orden debe coincidir con repTypes (ver arriba)
    [SerializeField] Button   modeBtn;
    [SerializeField] Image    modeBtnImg;
    [SerializeField] Text     modeBtnLabel;
    [SerializeField] Text     exportBtnLabel;
    [SerializeField] Image    exportBtnImg;

    [SerializeField] GameObject colorSectionGO;
    [SerializeField] Text       colorToggleBtnLabel;
    bool colorSectionExpanded = false;

    void Start() {
        MarkActive("c");
    }

    void Update() {
        var selM = UnityMolMain.getSelectionManager();
        bool hasSelection = selM != null &&
                            selM.currentSelection != null &&
                            selM.currentSelection.Count > 0;
        if (!hasSelection) {
            if (applyToSelection) {
                applyToSelection = false;
                UpdateModeBtnVisual();
            }
            if (hasExtractedSelection) {
                RestoreExtraction();
            }
        }
    }
}
}
