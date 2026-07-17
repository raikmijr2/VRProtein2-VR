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
        float panelH = titleH + rows * btnH + (rows + 1) * pad + resetH + pad + modeH + pad;

        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(panelW, panelH);
        canvasGO.transform.localScale = Vector3.one * 0.003f;

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
            resetRT.offsetMin = new Vector2(pad,      modeH + pad * 2);
            resetRT.offsetMax = new Vector2(-halfGap, modeH + pad * 2 + resetH);
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
            exportRT.offsetMin = new Vector2(halfGap, modeH + pad * 2);
            exportRT.offsetMax = new Vector2(-pad,    modeH + pad * 2 + resetH);
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
        modeRT.offsetMin = new Vector2(pad,  pad);
        modeRT.offsetMax = new Vector2(-pad, pad + modeH);
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
        if (UnityMolMain.getStructureManager().loadedStructures.Count == 0) {
            return;
        }
        currentRep = repCode;

        if (applyToSelection) {
            var selM = UnityMolMain.getSelectionManager();
            if (selM != null && selM.currentSelection != null && selM.currentSelection.Count > 0)
                ApplyRepToSelection(selM.clickSelectionName, repCode);
            else
                APIPython.showAs(repCode);
        } else {
            RestoreExtraction();
            APIPython.showAs(repCode);
        }

        MarkActive(repCode);
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
            // Saltar reps ocultas (showAs las deja en la lista pero hidden) y la propia rep de la selección
            if (!rep.isEnabled) continue;
            if (rep.selection == null) continue;
            if (rep.selection.name == clickSelName) continue;

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

            Image img = buttons[i].GetComponent<Image>();
            bool isActive = repTypes[i].code == repCode;
            img.color = isActive ? btnActive : btnNormal;

            ColorBlock cb = buttons[i].colors;
            cb.normalColor = isActive ? btnActive : btnNormal;
            buttons[i].colors = cb;
        }
    }

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
