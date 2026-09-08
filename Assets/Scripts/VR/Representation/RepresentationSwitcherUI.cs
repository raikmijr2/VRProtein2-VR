using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UMol {

/// <summary>
/// Panel VR para cambiar la representación de las proteínas cargadas.
/// Añadir a un GameObject vacío en la escena — crea la UI en runtime.
/// Se puede agarrar con el botón Grip del mando.
/// Split across 4 files: this one (fields, Start/Update, panel skeleton,
/// CreateRepButton), .RepSwitching.cs (rep-switch logic + ligand extraction),
/// .Coloring.cs (the COLOREAR sub-panel), .ProteinActions.cs (reset + export).
/// </summary>
public partial class RepresentationSwitcherUI : MonoBehaviour {

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

        GameObject canvasGO = VRUIFactory.CreateWorldSpaceCanvas("RepSwitcherPanel", spawnPosition, new Vector2(panelW, panelH));
        canvasGO.transform.rotation = Quaternion.identity;
        panelRT = canvasGO.GetComponent<RectTransform>();

        // ── Fondo ─────────────────────────────────────────────────────────
        VRUIFactory.CreateAnchoredImage(canvasGO.transform, "Background", bgColor,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── Título ────────────────────────────────────────────────────────
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(canvasGO.transform, false);
        Text titleText = titleGO.AddComponent<Text>();
        titleText.text      = "REPRESENTACIÓN";
        titleText.font      = VRUIFactory.GetFont();
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
        VRUIFactory.CreateAnchoredImage(canvasGO.transform, "Separator", new Color(1f, 1f, 1f, 0.15f),
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
            resetLabel.font      = VRUIFactory.GetFont();
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
            exportBtnLabel.font      = VRUIFactory.GetFont();
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
        modeBtnLabel.font      = VRUIFactory.GetFont();
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
        string capturedCode = repCode;
        return VRUIFactory.CreateTwoLineButton(parent, "Btn_" + repCode, label, desc, pos, size,
            btnNormal, () => OnRepButtonClicked(capturedCode),
            highlightColor: btnHighlight, pressedColor: btnPressed, selectedColor: btnNormal,
            nameFontSize: 24, descFontSize: 16, descColor: new Color(0.8f, 0.9f, 1f, 1f));
    }
}
}
