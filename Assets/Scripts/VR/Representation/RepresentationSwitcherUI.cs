using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UMol;
using UMol.API;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

/// <summary>
/// Panel VR para cambiar la representación de las proteínas cargadas.
/// Añadir a un GameObject vacío en la escena — crea la UI en runtime.
/// Se puede agarrar con el botón Grip del mando.
/// </summary>
public class RepresentationSwitcherUI : MonoBehaviour {

    [Header("Posición inicial del panel en el mundo")]
    public Vector3 spawnPosition = new Vector3(0f, 1.4f, 1.2f);

    // Representaciones disponibles: (código API, nombre visible)
    static readonly (string code, string label, string desc)[] repTypes = {
        ("c",      "Cartoon",    "Estructura secundaria"),
        ("hb",     "HyperBall",  "Átomos y enlaces"),
        ("s",      "Superficie", "Superficie molecular"),
        ("tube",   "Tubo",       "Esqueleto backbone"),
        ("sphere", "Esferas",    "CPK / VDW"),
        ("l",      "Líneas",     "Wireframe simple"),
    };

    // Colores del panel
    static readonly Color bgColor      = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    static readonly Color btnNormal    = new Color(0.18f, 0.35f, 0.72f, 1f);
    static readonly Color btnHighlight = new Color(0.30f, 0.55f, 1.00f, 1f);
    static readonly Color btnPressed   = new Color(0.08f, 0.18f, 0.45f, 1f);
    static readonly Color btnActive    = new Color(0.10f, 0.60f, 0.30f, 1f); // verde = activo
    static readonly Color btnReset     = new Color(0.65f, 0.12f, 0.12f, 1f); // rojo = reset
    static readonly Color btnExport    = new Color(0.10f, 0.50f, 0.20f, 1f); // verde = export

    string currentRep = "c";
    bool   applyToSelection = false;  // false = toda la proteína, true = solo la selección

    bool   surfaceOverlayActive = false;
    const string surfaceOverlaySel = "surface_overlay";
    static readonly Color btnSurface       = new Color(0.00f, 0.52f, 0.52f, 1f); // teal = overlay activo

    // Residuos de disolvente/iones a excluir del "ligando" (igual que PDBLoaderUI)
    static readonly HashSet<string> ligandSolventResidues = new HashSet<string> {
        "HOH", "WAT", "TIP", "TIP3", "SOL", "NA", "CL", "MG", "ZN", "CA",
        "K", "NA+", "CL-", "MG2+", "ZN2+", "CA2+", "FE", "MN", "NI", "CU"
    };

    // Estado de extracción: representa qué reps fueron modificadas y sus átomos originales
    List<(UnityMolRepresentation rep, List<UnityMolAtom> originalAtoms, bool wasEnabled)> modifiedReps
        = new List<(UnityMolRepresentation, List<UnityMolAtom>, bool)>();
    string extractedRepCode;
    bool   hasExtractedSelection;

    Button[]  buttons;
    Button    modeBtn;
    Image     modeBtnImg;
    Text      modeBtnLabel;
    Text      exportBtnLabel;
    Image     exportBtnImg;

    RectTransform panelRT;
    GameObject    colorSectionGO;
    bool          colorSectionExpanded = false;
    Text          colorToggleBtnLabel;

    const float colorToggleH = 44f;

    static readonly Color btnSelection = new Color(0.80f, 0.67f, 0.00f, 1f); // amarillo = selección activa

    void Start() {
        BuildPanel();
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

    void BuildPanel() {
        // ── Canvas principal ──────────────────────────────────────────────
        GameObject canvasGO = new GameObject("RepSwitcherPanel");
        canvasGO.transform.position = spawnPosition;
        canvasGO.transform.rotation = Quaternion.identity;

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Registrar el Canvas en el sistema de raycasting de VIU
        // Sin esto el rayo del mando no detecta el Canvas
        canvasGO.AddComponent<CanvasRaycastTarget>();

        // Hacer el panel agarrable con Grip
        PointerMoveUI mover = canvasGO.AddComponent<PointerMoveUI>();
        mover.moveParent = false;

        float btnW   = 190f;
        float btnH   = 65f;
        float pad    = 12f;
        int   cols   = 2;
        int   rows   = Mathf.CeilToInt(repTypes.Length / (float)cols);
        float titleH = 55f;
        float modeH  = 44f;  // altura del botón de modo al fondo
        float resetH = 44f;  // altura del botón de reset
        float panelW = cols * btnW + (cols + 1) * pad;
        float panelH = titleH + rows * btnH + (rows + 1) * pad + resetH + pad + modeH + pad + colorToggleH + pad;

        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(panelW, panelH);
        canvasGO.transform.localScale = Vector3.one * 0.003f;
        panelRT = canvasRT;

        // ── Fondo ─────────────────────────────────────────────────────────
        AddImage(canvasGO.transform, "Background", bgColor,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── Título ────────────────────────────────────────────────────────
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(canvasGO.transform, false);
        Text titleText = titleGO.AddComponent<Text>();
        titleText.text      = "REPRESENTACIÓN";
        titleText.font      = GetFont();
        titleText.fontSize  = 26;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color     = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;
        RectTransform titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin    = new Vector2(0f, 1f);
        titleRT.anchorMax    = new Vector2(1f, 1f);
        titleRT.pivot        = new Vector2(0.5f, 1f);
        titleRT.offsetMin    = new Vector2(0, -titleH);
        titleRT.offsetMax    = new Vector2(0, 0);

        // Separador debajo del título
        AddImage(canvasGO.transform, "Separator", new Color(1f, 1f, 1f, 0.15f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(pad, -(titleH)), new Vector2(-pad, -(titleH - 2f)));

        // ── Botones ───────────────────────────────────────────────────────
        buttons = new Button[repTypes.Length];

        for (int i = 0; i < repTypes.Length; i++) {
            int row = i / cols;
            int col = i % cols;

            float x = -panelW / 2f + pad + col * (btnW + pad) + btnW / 2f;
            float y =  panelH / 2f - titleH - pad - row * (btnH + pad) - btnH / 2f;

            var (code, label, desc) = repTypes[i];
            buttons[i] = CreateRepButton(canvasGO.transform, label, desc, code,
                                          new Vector2(x, y), new Vector2(btnW, btnH));
        }

        // ── Botón RESET (mitad izquierda) + EXPORTAR PDB (mitad derecha) ─
        float halfGap = pad * 0.5f;
        {
            var resetGO = new GameObject("BtnReset");
            resetGO.transform.SetParent(canvasGO.transform, false);
            Image resetImg   = resetGO.AddComponent<Image>();
            resetImg.color   = btnReset;
            Button resetBtn  = resetGO.AddComponent<Button>();
            resetBtn.onClick.AddListener(OnResetProtein);
            var resetCB = resetBtn.colors;
            resetCB.normalColor      = btnReset;
            resetCB.highlightedColor = Color.Lerp(btnReset, Color.white, 0.25f);
            resetCB.pressedColor     = Color.Lerp(btnReset, Color.black, 0.35f);
            resetBtn.colors = resetCB;
            var resetRT = resetGO.GetComponent<RectTransform>();
            resetRT.anchorMin = new Vector2(0f,  0f);
            resetRT.anchorMax = new Vector2(0.5f, 0f);
            resetRT.pivot     = new Vector2(0.5f, 0f);
            resetRT.offsetMin = new Vector2(pad,      modeH + pad * 3 + colorToggleH);
            resetRT.offsetMax = new Vector2(-halfGap, modeH + pad * 3 + colorToggleH + resetH);
            var resetLabelGO = new GameObject("Label");
            resetLabelGO.transform.SetParent(resetGO.transform, false);
            Text resetLabel      = resetLabelGO.AddComponent<Text>();
            resetLabel.text      = "Reiniciar";
            resetLabel.font      = GetFont();
            resetLabel.fontSize  = 20;
            resetLabel.fontStyle = FontStyle.Bold;
            resetLabel.color     = Color.white;
            resetLabel.alignment = TextAnchor.MiddleCenter;
            var resetLabelRT = resetLabelGO.GetComponent<RectTransform>();
            resetLabelRT.anchorMin = Vector2.zero;
            resetLabelRT.anchorMax = Vector2.one;
            resetLabelRT.offsetMin = resetLabelRT.offsetMax = Vector2.zero;
        }
        {
            var exportGO = new GameObject("BtnExport");
            exportGO.transform.SetParent(canvasGO.transform, false);
            exportBtnImg         = exportGO.AddComponent<Image>();
            exportBtnImg.color   = btnExport;
            Button exportBtn     = exportGO.AddComponent<Button>();
            exportBtn.onClick.AddListener(OnExportPDB);
            var exportCB = exportBtn.colors;
            exportCB.normalColor      = btnExport;
            exportCB.highlightedColor = Color.Lerp(btnExport, Color.white, 0.25f);
            exportCB.pressedColor     = Color.Lerp(btnExport, Color.black, 0.35f);
            exportBtn.colors = exportCB;
            var exportRT = exportGO.GetComponent<RectTransform>();
            exportRT.anchorMin = new Vector2(0.5f, 0f);
            exportRT.anchorMax = new Vector2(1f,   0f);
            exportRT.pivot     = new Vector2(0.5f, 0f);
            exportRT.offsetMin = new Vector2(halfGap, modeH + pad * 3 + colorToggleH);
            exportRT.offsetMax = new Vector2(-pad,    modeH + pad * 3 + colorToggleH + resetH);
            var exportLabelGO = new GameObject("Label");
            exportLabelGO.transform.SetParent(exportGO.transform, false);
            exportBtnLabel           = exportLabelGO.AddComponent<Text>();
            exportBtnLabel.text      = "Exportar PDB";
            exportBtnLabel.font      = GetFont();
            exportBtnLabel.fontSize  = 20;
            exportBtnLabel.fontStyle = FontStyle.Bold;
            exportBtnLabel.color     = Color.white;
            exportBtnLabel.alignment = TextAnchor.MiddleCenter;
            var exportLabelRT = exportLabelGO.GetComponent<RectTransform>();
            exportLabelRT.anchorMin = Vector2.zero;
            exportLabelRT.anchorMax = Vector2.one;
            exportLabelRT.offsetMin = exportLabelRT.offsetMax = Vector2.zero;
        }

        // ── Botón de modo (Selección / Todo) al fondo ────────────────────
        var modeGO = new GameObject("BtnMode");
        modeGO.transform.SetParent(canvasGO.transform, false);
        modeBtnImg  = modeGO.AddComponent<Image>();
        modeBtnImg.color = btnNormal;
        modeBtn = modeGO.AddComponent<Button>();
        modeBtn.onClick.AddListener(OnModeToggle);
        var modeCB = modeBtn.colors;
        modeCB.normalColor      = btnNormal;
        modeCB.highlightedColor = Color.Lerp(btnNormal, Color.white, 0.25f);
        modeCB.pressedColor     = Color.Lerp(btnNormal, Color.black, 0.30f);
        modeBtn.colors = modeCB;
        var modeRT = modeGO.GetComponent<RectTransform>();
        modeRT.anchorMin = new Vector2(0f, 0f);
        modeRT.anchorMax = new Vector2(1f, 0f);
        modeRT.pivot     = new Vector2(0.5f, 0f);
        modeRT.offsetMin = new Vector2(pad,  pad * 2 + colorToggleH);
        modeRT.offsetMax = new Vector2(-pad, pad * 2 + colorToggleH + modeH);
        var modeLabelGO = new GameObject("Label");
        modeLabelGO.transform.SetParent(modeGO.transform, false);
        modeBtnLabel = modeLabelGO.AddComponent<Text>();
        modeBtnLabel.text      = "Modo: TODO";
        modeBtnLabel.font      = GetFont();
        modeBtnLabel.fontSize  = 20;
        modeBtnLabel.fontStyle = FontStyle.Bold;
        modeBtnLabel.color     = Color.white;
        modeBtnLabel.alignment = TextAnchor.MiddleCenter;
        var modeLabelRT = modeLabelGO.GetComponent<RectTransform>();
        modeLabelRT.anchorMin = Vector2.zero;
        modeLabelRT.anchorMax = Vector2.one;
        modeLabelRT.offsetMin = modeLabelRT.offsetMax = Vector2.zero;

        // Botón para colapsar/expandir el panel de coloreado
        BuildColorToggleButton(canvasGO.transform, panelW, pad);

        // Sub-panel de coloreado (oculto por defecto, flota bajo el panel principal)
        BuildColorSection(canvasGO.transform, panelW, panelH, pad, btnH);

        // Marcar cartoon como activo por defecto
        MarkActive("c");
    }

    Button CreateRepButton(Transform parent, string label, string desc, string repCode,
                           Vector2 pos, Vector2 size) {
        GameObject btnGO = new GameObject("Btn_" + repCode);
        btnGO.transform.SetParent(parent, false);

        Image bg = btnGO.AddComponent<Image>();
        bg.color = btnNormal;

        Button btn = btnGO.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor      = btnNormal;
        cb.highlightedColor = btnHighlight;
        cb.pressedColor     = btnPressed;
        cb.selectedColor    = btnNormal;
        cb.fadeDuration     = 0.1f;
        btn.colors = cb;

        string capturedCode = repCode;
        btn.onClick.AddListener(() => OnRepButtonClicked(capturedCode));

        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        // Nombre de la representación (grande, arriba)
        GameObject nameGO = new GameObject("Name");
        nameGO.transform.SetParent(btnGO.transform, false);
        Text nameText = nameGO.AddComponent<Text>();
        nameText.text      = label;
        nameText.font      = GetFont();
        nameText.fontSize  = 24;
        nameText.fontStyle = FontStyle.Bold;
        nameText.color     = Color.white;
        nameText.alignment = TextAnchor.MiddleCenter;
        RectTransform nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 0.45f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.offsetMin = new Vector2(4, 0);
        nameRT.offsetMax = new Vector2(-4, -4);

        // Descripción (pequeña, abajo)
        GameObject descGO = new GameObject("Desc");
        descGO.transform.SetParent(btnGO.transform, false);
        Text descText = descGO.AddComponent<Text>();
        descText.text      = desc;
        descText.font      = GetFont();
        descText.fontSize  = 16;
        descText.color     = new Color(0.8f, 0.9f, 1f, 1f);
        descText.alignment = TextAnchor.MiddleCenter;
        RectTransform descRT = descGO.GetComponent<RectTransform>();
        descRT.anchorMin = new Vector2(0f, 0f);
        descRT.anchorMax = new Vector2(1f, 0.5f);
        descRT.offsetMin = new Vector2(4, 2);
        descRT.offsetMax = new Vector2(-4, 0);

        return btn;
    }

    void OnRepButtonClicked(string repCode) {
        if (UnityMolMain.getStructureManager().loadedStructures.Count == 0) return;

        // Surface es un overlay transparente independiente — toggle, no cambia la rep principal
        if (repCode == "s") {
            surfaceOverlayActive = !surfaceOverlayActive;
            if (surfaceOverlayActive) ShowSurfaceOverlay();
            else HideSurfaceOverlay();
            UpdateSurfaceBtnVisual();
            return;
        }

        currentRep = repCode;

        if (applyToSelection) {
            var selM = UnityMolMain.getSelectionManager();
            if (selM != null && selM.currentSelection != null && selM.currentSelection.Count > 0)
                ApplyRepToSelection(selM.clickSelectionName, repCode);
            else
                ApplyRepRespectingLigand(repCode);
        } else {
            RestoreExtraction();
            ApplyRepRespectingLigand(repCode);
        }

        // showAs() borra todas las reps — re-mostrar surface overlay si estaba activo
        if (surfaceOverlayActive) ShowSurfaceOverlay();

        MarkActive(repCode);
    }

    void ShowSurfaceOverlay() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return;

        var selMgr = UnityMolMain.getSelectionManager();

        // Construir selección proteica (no-HET) si no existe ya
        if (!selMgr.selections.ContainsKey(surfaceOverlaySel)) {
            var proteinAtoms = new List<UnityMolAtom>();
            foreach (var s in sm.loadedStructures)
                foreach (var a in s.currentModel.allAtoms)
                    if (!a.isHET) proteinAtoms.Add(a);
            if (proteinAtoms.Count == 0) return;
            selMgr.selections[surfaceOverlaySel] = new UnityMolSelection(proteinAtoms, surfaceOverlaySel);
        }

        APIPython.showSelection(surfaceOverlaySel, "s");
        APIPython.showSelection(surfaceOverlaySel, "s"); // garantiza Show() en la rep recién creada
        APIPython.setTransparentSurface(surfaceOverlaySel, 0.35f);
    }

    void HideSurfaceOverlay() {
        APIPython.hideSelection(surfaceOverlaySel, "s");
    }

    void UpdateSurfaceBtnVisual() {
        for (int i = 0; i < repTypes.Length; i++) {
            if (repTypes[i].code != "s" || buttons == null || buttons[i] == null) continue;
            Color col = surfaceOverlayActive ? btnSurface : btnNormal;
            Image img = buttons[i].GetComponent<Image>();
            img.color = col;
            ColorBlock cb = buttons[i].colors;
            cb.normalColor      = col;
            cb.highlightedColor = Color.Lerp(col, Color.white, 0.25f);
            cb.pressedColor     = Color.Lerp(col, Color.black, 0.35f);
            buttons[i].colors = cb;
            break;
        }
    }

    // Aplica repCode a toda la molécula pero preserva HyperBall en átomos HETATM (ligandos).
    // Si no hay ligando, equivale a APIPython.showAs(repCode).
    void ApplyRepRespectingLigand(string repCode) {
        var sm = UnityMolMain.getStructureManager();

        // ¿Alguna estructura tiene ligando real (no disolvente)?
        bool anyLigand = false;
        foreach (var s in sm.loadedStructures) {
            foreach (var a in s.currentModel.allAtoms) {
                if (a.isHET && !ligandSolventResidues.Contains(a.residue.name)) {
                    anyLigand = true;
                    break;
                }
            }
            if (anyLigand) break;
        }

        // Aplicar la rep a todo (limpia representaciones anteriores)
        APIPython.showAs(repCode);

        if (!anyLigand) return;

        // Extraer átomos de ligando a HyperBall en cada estructura
        foreach (var s in sm.loadedStructures) {
            var ligandAtoms = new List<UnityMolAtom>();
            foreach (var a in s.currentModel.allAtoms) {
                if (a.isHET && !ligandSolventResidues.Contains(a.residue.name))
                    ligandAtoms.Add(a);
            }
            if (ligandAtoms.Count == 0) continue;

            string ligSelName = "ligand_" + s.name;
            ExtractAtomsToRep(ligandAtoms, ligSelName, "hb");
        }
    }

    // Extrae targetAtoms de todas las reps activas y les aplica repCode en selName,
    // sin tocar el estado de extracción de usuario (modifiedReps / hasExtractedSelection).
    void ExtractAtomsToRep(List<UnityMolAtom> targetAtoms, string selName, string repCode) {
        var selMgr     = UnityMolMain.getSelectionManager();
        var repManager = UnityMolMain.getRepresentationManager();
        var targetSet  = new HashSet<UnityMolAtom>(targetAtoms);

        var snapshot = new List<UnityMolRepresentation>(repManager.representations);
        foreach (var rep in snapshot) {
            if (!rep.isEnabled || rep.selection == null) continue;
            if (rep.selection.name == selName) continue;

            var repAtoms   = rep.selection.atoms;
            var complement = new List<UnityMolAtom>(repAtoms.Count);
            bool hasOverlap = false;

            foreach (var a in repAtoms) {
                if (targetSet.Contains(a)) hasOverlap = true;
                else complement.Add(a);
            }

            if (!hasOverlap) continue;

            if (complement.Count == 0) {
                rep.Hide();
            } else {
                var complementSel = new UnityMolSelection(complement, "ligcomp_" + rep.selection.name);
                rep.updateWithNewSelection(complementSel);
            }
        }

        // Registrar selección y aplicar la representación del ligando
        var ligSel = new UnityMolSelection(targetAtoms, selName);
        selMgr.selections[selName] = ligSel;
        APIPython.showSelection(selName, repCode);
    }

    void ApplyRepToSelection(string clickSelName, string repCode) {
        RestoreExtraction();

        var selM = UnityMolMain.getSelectionManager();
        if (!selM.selections.ContainsKey(clickSelName)) return;

        var clickSel = selM.selections[clickSelName];
        if (clickSel == null || clickSel.Count == 0) return;

        var selectedSet = new HashSet<UnityMolAtom>(clickSel.atoms);
        var repManager  = UnityMolMain.getRepresentationManager();

        var snapshot = new System.Collections.Generic.List<UnityMolRepresentation>(repManager.representations);
        foreach (var rep in snapshot) {
            // Saltar reps ocultas, la propia rep de la selección y el surface overlay (es independiente)
            if (!rep.isEnabled) continue;
            if (rep.selection == null) continue;
            if (rep.selection.name == clickSelName) continue;
            if (rep.selection.name == surfaceOverlaySel) continue;

            var repAtoms   = rep.selection.atoms;
            var complement = new List<UnityMolAtom>(repAtoms.Count);
            bool hasOverlap = false;

            foreach (var a in repAtoms) {
                if (selectedSet.Contains(a)) hasOverlap = true;
                else complement.Add(a);
            }

            if (!hasOverlap) continue;

            bool wasEnabled = rep.isEnabled;
            modifiedReps.Add((rep, new List<UnityMolAtom>(repAtoms), wasEnabled));

            if (complement.Count == 0) {
                rep.Hide();
            } else {
                var complementSel = new UnityMolSelection(complement, "complement_" + rep.selection.name);
                rep.updateWithNewSelection(complementSel);
            }
        }

        APIPython.showSelection(clickSelName, repCode);
        // showSelection only calls Show() on existing reps; first call creates via AddRepresentation
        // without Show(). Second call guarantees Show() is invoked on the newly-created rep.
        APIPython.showSelection(clickSelName, repCode);
        extractedRepCode     = repCode;
        hasExtractedSelection = true;
    }

    void RestoreExtraction() {
        if (!hasExtractedSelection) return;

        foreach (var (rep, originalAtoms, wasEnabled) in modifiedReps) {
            if (rep == null || rep.selection == null) continue;
            if (originalAtoms == null || originalAtoms.Count == 0) continue;
            var restoreSel = new UnityMolSelection(originalAtoms, "restore_" + rep.selection.name);
            rep.isEnabled = wasEnabled;
            rep.updateWithNewSelection(restoreSel);
        }
        modifiedReps.Clear();

        var selM = UnityMolMain.getSelectionManager();
        if (!string.IsNullOrEmpty(extractedRepCode) && selM != null &&
            selM.selections.ContainsKey(selM.clickSelectionName)) {
            APIPython.deleteRepresentationInSelection(selM.clickSelectionName, extractedRepCode);
        }

        extractedRepCode      = null;
        hasExtractedSelection = false;
    }

    void OnModeToggle() {
        var selM = UnityMolMain.getSelectionManager();
        bool hasSelection = selM != null &&
                            selM.currentSelection != null &&
                            selM.currentSelection.Count > 0;
        if (!applyToSelection && !hasSelection) return; // sin selección no se puede cambiar a modo selección

        applyToSelection = !applyToSelection;
        UpdateModeBtnVisual();
    }

    void UpdateModeBtnVisual() {
        if (modeBtnImg   == null) return;
        Color c = applyToSelection ? btnSelection : btnNormal;
        modeBtnImg.color = c;
        ColorBlock cb = modeBtn.colors;
        cb.normalColor = c;
        modeBtn.colors = cb;
        if (modeBtnLabel != null)
            modeBtnLabel.text = applyToSelection ? "Modo: SELECCIÓN" : "Modo: TODO";
    }

    void OnResetProtein() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return;

        // 1. Restaurar posiciones originales de todos los átomos
        foreach (var s in sm.loadedStructures) {
            var atoms = s.currentModel.allAtoms;
            for (int i = 0; i < atoms.Count; i++)
                atoms[i].position = atoms[i].oriPosition;
            s.currentModel.ComputeCentroid();
        }

        // 2. Eliminar la click-selection del diccionario para que no persistan átomos viejos
        // (clearSelections solo vacía curSelName pero deja el objeto con los átomos en el dict)
        var selMgr = UnityMolMain.getSelectionManager();
        if (selMgr != null && selMgr.selections.ContainsKey(selMgr.clickSelectionName))
            APIPython.deleteSelection(selMgr.clickSelectionName);
        APIPython.clearSelections();

        // 3. Refrescar representaciones y búsqueda espacial
        foreach (var s in sm.loadedStructures) {
            s.updateRepresentations(trajectory: true);
            if (s.spatialSearch != null)
                s.spatialSearch.UpdatePositions(s.currentModel.allAtoms);
        }

        // 4. Limpiar highlights amarillos de los selectores
        var selectors = FindObjectsOfType<PointerAtomSelection>(true);
        foreach (var sel in selectors)
            sel.ResetHighlights();
    }

    void OnExportPDB() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var s in sm.loadedStructures) {
            var atoms = s.currentModel.allAtoms;
            // Construir selección temporal con los átomos del modelo actual
            var sel = new UnityMolSelection(new List<UnityMolAtom>(atoms), "export_" + s.name);
            // Pasar atom.position como overridedPos: Write usa oriPosition por defecto,
            // así exportamos las posiciones modificadas manualmente
            var positions = new Vector3[atoms.Count];
            for (int i = 0; i < atoms.Count; i++)
                positions[i] = atoms[i].position;
            sb.Append(PDBReader.Write(sel, writeModel: false, writeHET: true, overridedPos: positions));
        }

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename  = "vrprotein_" + timestamp + ".pdb";
        string path      = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[VRProtein] PDB guardado en: " + path);

        StartCoroutine(FlashExportFeedback(filename));
    }

    IEnumerator FlashExportFeedback(string filename) {
        if (exportBtnLabel == null || exportBtnImg == null) yield break;
        exportBtnLabel.text  = "¡Guardado!";
        exportBtnImg.color   = new Color(0.05f, 0.70f, 0.30f, 1f);
        yield return new WaitForSeconds(2f);
        exportBtnLabel.text  = "Exportar PDB";
        exportBtnImg.color   = btnExport;
    }

    void MarkActive(string repCode) {
        for (int i = 0; i < repTypes.Length; i++) {
            if (buttons == null || buttons[i] == null) continue;
            if (repTypes[i].code == "s") continue; // surface es toggle, gestiona su propio color

            Image img = buttons[i].GetComponent<Image>();
            bool isActive = repTypes[i].code == repCode;
            img.color = isActive ? btnActive : btnNormal;

            ColorBlock cb = buttons[i].colors;
            cb.normalColor = isActive ? btnActive : btnNormal;
            buttons[i].colors = cb;
        }
    }

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
        colorToggleBtnLabel.font      = GetFont();
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
        var go = new GameObject("CBtn_" + label); go.transform.SetParent(parent, false);
        var btnColor = new Color(0.28f, 0.18f, 0.52f, 1f);
        go.AddComponent<Image>().color = btnColor;
        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor      = btnColor;
        cb.highlightedColor = Color.Lerp(btnColor, Color.white, 0.25f);
        cb.pressedColor     = Color.Lerp(btnColor, Color.black, 0.30f);
        btn.colors = cb;
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        var nGO = new GameObject("N"); nGO.transform.SetParent(go.transform, false);
        var nT  = nGO.AddComponent<Text>();
        nT.text = label; nT.font = GetFont(); nT.fontSize = 20; nT.fontStyle = FontStyle.Bold;
        nT.color = Color.white; nT.alignment = TextAnchor.MiddleCenter;
        var nRT = nGO.GetComponent<RectTransform>();
        nRT.anchorMin = new Vector2(0f, 0.45f); nRT.anchorMax = new Vector2(1f, 1f);
        nRT.offsetMin = new Vector2(4, 0); nRT.offsetMax = new Vector2(-4, -4);

        var dGO = new GameObject("D"); dGO.transform.SetParent(go.transform, false);
        var dT  = dGO.AddComponent<Text>();
        dT.text = desc; dT.font = GetFont(); dT.fontSize = 14;
        dT.color = new Color(0.85f, 0.78f, 1f, 1f); dT.alignment = TextAnchor.MiddleCenter;
        var dRT = dGO.GetComponent<RectTransform>();
        dRT.anchorMin = new Vector2(0f, 0f); dRT.anchorMax = new Vector2(1f, 0.5f);
        dRT.offsetMin = new Vector2(4, 2); dRT.offsetMax = new Vector2(-4, 0);

        return btn;
    }

    void OnColorToggle() {
        colorSectionExpanded = !colorSectionExpanded;
        if (colorSectionGO) colorSectionGO.SetActive(colorSectionExpanded);
        if (colorToggleBtnLabel != null)
            colorToggleBtnLabel.text = colorSectionExpanded ? "COLOREAR  ▲" : "COLOREAR  ▼";
    }

    // Devuelve el conjunto de nombres de selección "toda-la-estructura" que deben omitirse.
    // Después de ApplyRepRespectingLigand, updateWithNewSelection muta esas selecciones
    // in-place añadiendo HOH/MG al complemento, causando errores en el bond-line manager.
    HashSet<string> FullStructureSelNames() {
        var sm = UnityMolMain.getStructureManager();
        var names = new HashSet<string>();
        foreach (var s in sm.loadedStructures)
            names.Add(s.ToSelectionName());
        return names;
    }

    // Aplica colorFunc a todas las representaciones activas (itera todos los tipos de rep conocidos).
    void ColorAllReps(System.Action<string, string> colorFunc) {
        var repMgr    = UnityMolMain.getRepresentationManager();
        var skipNames = FullStructureSelNames();
        var seen      = new HashSet<string>();
        foreach (var rep in repMgr.representations) {
            if (rep.selection == null) continue;
            string sn = rep.selection.name;
            if (skipNames.Contains(sn)) continue;
            if (!seen.Add(sn)) continue;
            foreach (var (code, _, _) in repTypes)
                colorFunc(sn, code);
        }
    }

    void ApplySSColoring() {
        var repMgr    = UnityMolMain.getRepresentationManager();
        var skipNames = FullStructureSelNames();
        var seen      = new HashSet<string>();
        foreach (var rep in repMgr.representations) {
            if (rep.selection == null) continue;
            string sn = rep.selection.name;
            if (skipNames.Contains(sn)) continue;
            if (!seen.Add(sn)) continue;
            APIPython.setCartoonColorSS(sn, "helix", new Color(1.00f, 0.00f, 0.80f)); // magenta (PyMOL)
            APIPython.setCartoonColorSS(sn, "sheet", new Color(1.00f, 1.00f, 0.00f)); // amarillo (PyMOL)
            APIPython.setCartoonColorSS(sn, "coil",  new Color(1.00f, 1.00f, 1.00f)); // blanco (PyMOL)
        }
    }

    void ApplyCPKColoring()             => ColorAllReps((sel, t) => APIPython.colorByAtom(sel, t));
    void ApplyHydrophobicityColoring()  => ColorAllReps((sel, t) => APIPython.colorByHydrophobicity(sel, t));
    void ApplyRainbowColoring()         => ColorAllReps((sel, t) => APIPython.colorByResnum(sel, t));

    // ── Helpers ───────────────────────────────────────────────────────────

    static void AddImage(Transform parent, string name, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
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
