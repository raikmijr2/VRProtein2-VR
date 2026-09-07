using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UMol;
using UMol.API;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

/// <summary>
/// Panel VR para seleccionar un rango de residuos por número de secuencia.
/// Colorea los residuos seleccionados en naranja dentro del Cartoon existente.
/// Incluye teclado numérico propio, independiente del teclado de UnityMol.
/// </summary>
public class SequenceSelectorUI : MonoBehaviour {

    public Vector3 spawnPosition = new Vector3(-0.5f, 1.4f, 1.2f);

    static readonly Color bg       = new Color(0.06f, 0.10f, 0.08f, 0.95f);
    static readonly Color btnGreen = new Color(0.10f, 0.55f, 0.25f, 1f);
    static readonly Color btnRed   = new Color(0.65f, 0.12f, 0.12f, 1f);

    InputField fromInput;
    InputField toInput;
    Text       statusText;
    Text       rangeLabel;

    int lastStructCount = -1;
    int lastRepCount = -1;
    HashSet<UnityMolAtom> seqHighlightedAtoms = new HashSet<UnityMolAtom>();

    GameObject numKeyboardGO;
    InputField activeInput;

    void Start() => BuildPanel();

    void Update() {
        var sm = UnityMolMain.getStructureManager();
        int count = sm?.loadedStructures.Count ?? 0;
        if (count != lastStructCount) {
            lastStructCount = count;
            RefreshRangeLabel();
        }

        if (seqHighlightedAtoms.Count > 0) {
            var repMgr = UnityMolMain.getRepresentationManager();
            int repCount = repMgr?.representations.Count ?? 0;
            if (repCount != lastRepCount) {
                lastRepCount = repCount;
                SeqRefreshHighlight();
            }
        }

        if (numKeyboardGO == null) return;
        var es = EventSystem.current;
        if (es == null) return;
        var sel = es.currentSelectedGameObject;
        bool fromFocused = fromInput != null && sel == fromInput.gameObject;
        bool toFocused   = toInput   != null && sel == toInput.gameObject;
        if (fromFocused || toFocused) {
            activeInput = fromFocused ? fromInput : toInput;
            if (!numKeyboardGO.activeInHierarchy)
                numKeyboardGO.SetActive(true);
        }
    }

    // ── Lógica ───────────────────────────────────────────────────────────────

    void RefreshRangeLabel() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) {
            if (rangeLabel) rangeLabel.text = "Sin proteína cargada"; return;
        }
        var s = sm.loadedStructures[0];
        int minId = int.MaxValue, maxId = int.MinValue;
        foreach (var a in s.currentModel.allAtoms) {
            if (a.isHET) continue;
            if (a.residue.id < minId) minId = a.residue.id;
            if (a.residue.id > maxId) maxId = a.residue.id;
        }
        if (minId == int.MaxValue) {
            if (rangeLabel) rangeLabel.text = "Sin residuos proteicos"; return;
        }
        if (rangeLabel) rangeLabel.text = $"{s.name}  |  residuos {minId} – {maxId}";
        if (fromInput && string.IsNullOrEmpty(fromInput.text)) fromInput.text = minId.ToString();
        if (toInput   && string.IsNullOrEmpty(toInput.text))   toInput.text   = maxId.ToString();
    }

    void OnSelectClicked() {
        var sm = UnityMolMain.getStructureManager();
        if (sm == null || sm.loadedStructures.Count == 0) {
            SetStatus("No hay proteína cargada.", Color.yellow); return;
        }
        if (!int.TryParse(fromInput?.text, out int fromRes) ||
            !int.TryParse(toInput?.text,   out int toRes)) {
            SetStatus("Introduce números de residuo válidos.", Color.yellow); return;
        }
        if (fromRes > toRes) {
            SetStatus("'Desde' debe ser ≤ 'Hasta'.", Color.yellow); return;
        }

        var selMgr = UnityMolMain.getSelectionManager();
        string query = $"protein and resid {fromRes}:{toRes}";
        var result = APIPython.select(query, selMgr.clickSelectionName,
                                      createSelection: true, addToExisting: false,
                                      silent: true, setAsCurrentSelection: true);

        if (result == null || result.atoms.Count == 0) {
            SetStatus("No hay residuos en ese rango.", Color.yellow); return;
        }

        // Limpiar highlights del controlador A y aplicar amarillo igual que PointerAtomSelection.AddHighlight
        foreach (var pas in Object.FindObjectsOfType<PointerAtomSelection>())
            pas.ResetHighlights();
        SeqClearHighlight();
        SeqApplyHighlight(result.atoms);
        lastRepCount = UnityMolMain.getRepresentationManager()?.representations.Count ?? 0;

        SetStatus($"{result.atoms.Count} átomos seleccionados  |  residuos {fromRes}–{toRes}", Color.green);
    }

    void OnClearClicked() {
        SeqClearHighlight();
        var selMgr = UnityMolMain.getSelectionManager();
        if (selMgr.currentSelection != null && selMgr.currentSelection.isAlterable) {
            APIPython.select("nothing", selMgr.currentSelection.name,
                             createSelection: true, addToExisting: false, silent: true);
        }
        SetStatus("Selección limpiada.", Color.white);
    }

    void SeqApplyHighlight(List<UnityMolAtom> atoms) {
        var repManager = UnityMolMain.getRepresentationManager();
        if (repManager == null) return;
        Color32 yellow = new Color32(255, 217, 0, 255);
        foreach (var a in atoms) seqHighlightedAtoms.Add(a);
        foreach (var rep in repManager.representations)
            rep.SetColors(atoms, yellow);
    }

    void SeqRefreshHighlight() {
        var repManager = UnityMolMain.getRepresentationManager();
        if (repManager == null) return;
        Color32 yellow = new Color32(255, 217, 0, 255);
        var atomList = new List<UnityMolAtom>(seqHighlightedAtoms);
        foreach (var rep in repManager.representations)
            rep.SetColors(atomList, yellow);
    }

    void SeqClearHighlight() {
        if (seqHighlightedAtoms.Count == 0) return;
        var repManager = UnityMolMain.getRepresentationManager();
        if (repManager == null) { seqHighlightedAtoms.Clear(); return; }
        foreach (var rep in repManager.representations)
            rep.ResetColor();
        seqHighlightedAtoms.Clear();
    }

    void SetStatus(string msg, Color col) {
        if (statusText) { statusText.text = msg; statusText.color = col; }
        Debug.Log("[SeqSelector] " + msg);
    }

    // ── Teclado numérico ─────────────────────────────────────────────────────

    void TypeKey(string k) {
        if (activeInput == null) return;
        if (k == "Back") {
            if (activeInput.text.Length > 0)
                activeInput.text = activeInput.text.Remove(activeInput.text.Length - 1);
        } else if (k == "OK") {
            activeInput.DeactivateInputField();
            if (numKeyboardGO) numKeyboardGO.SetActive(false);
            activeInput = null;
        } else {
            activeInput.text += k;
        }
    }

    void BuildNumericKeyboard(Transform canvasParent, float panelW) {
        const float btnS = 66f, pad = 8f;
        const float kW = 3 * btnS + 4 * pad;
        const float kH = 4 * btnS + 5 * pad;

        numKeyboardGO = new GameObject("SeqNumKeyboard");
        numKeyboardGO.AddComponent<Canvas>();
        numKeyboardGO.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        numKeyboardGO.AddComponent<GraphicRaycaster>();
        numKeyboardGO.AddComponent<CanvasRaycastTarget>();

        var rt = (RectTransform)numKeyboardGO.transform;
        rt.SetParent(canvasParent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(kW, kH);
        rt.anchoredPosition = new Vector2(panelW / 2f + 20f + kW / 2f, 0f);

        VRUIFactory.CreateBackgroundImage(numKeyboardGO.transform, bg);

        string[][] rows = {
            new[] { "7", "8", "9" },
            new[] { "4", "5", "6" },
            new[] { "1", "2", "3" },
            new[] { "←", "0", "OK" },
        };
        var cNum = new Color(0.15f, 0.28f, 0.18f, 1f);
        var cDel = new Color(0.50f, 0.20f, 0.10f, 1f);

        for (int r = 0; r < rows.Length; r++) {
            for (int c = 0; c < rows[r].Length; c++) {
                string key = rows[r][c];
                float x = -kW / 2f + pad + c * (btnS + pad) + btnS / 2f;
                float y =  kH / 2f - pad - r * (btnS + pad) - btnS / 2f;
                Color col = key == "OK" ? btnGreen : key == "←" ? cDel : cNum;
                string k  = key == "←" ? "Back" : key;
                int keyFontSize = key.Length > 1 ? 18 : 26;
                var btn = VRUIFactory.CreateButton(numKeyboardGO.transform, key, new Vector2(btnS, btnS), new Vector2(x, y), col,
                    fontSize: keyFontSize, highlightBlend: 0.30f);
                var capturedK = k;
                btn.onClick.AddListener(() => TypeKey(capturedK));
            }
        }

        numKeyboardGO.SetActive(false);
    }

    // ── Panel ─────────────────────────────────────────────────────────────────

    void BuildPanel() {
        const float W = 420f, pad = 12f;
        const float titleH = 50f, rangeLH = 26f, labelH = 20f;
        const float inputH = 42f, btnH = 50f, statusH = 44f;
        float H = pad + titleH + pad + rangeLH + pad
                + labelH + 6f + inputH + pad
                + labelH + 6f + inputH + pad
                + btnH + pad + statusH + pad;

        var go = VRUIFactory.CreateWorldSpaceCanvas("SequenceSelectorPanel", spawnPosition, new Vector2(W, H));

        VRUIFactory.CreateBackgroundImage(go.transform, bg);
        float y = H / 2f - pad;

        VRUIFactory.CreateCenteredLabel(go.transform, "SELECCIÓN SECUENCIA", W, 24 + 6, 24, new Vector2(0, y - titleH / 2f), FontStyle.Bold);
        y -= titleH + pad;

        VRUIFactory.CreateSeparator(go.transform, new Vector2(0, y + rangeLH / 2f), 380f);
        rangeLabel = VRUIFactory.CreateCenteredLabel(go.transform, "Sin proteína cargada", W - pad * 2, rangeLH, 14, new Vector2(0, y - rangeLH / 2f));
        rangeLabel.color = new Color(0.7f, 0.9f, 0.7f, 1f);
        y -= rangeLH + pad;

        VRUIFactory.CreateCenteredLabel(go.transform, "Desde residuo:", W, 16 + 6, 16, new Vector2(0, y - labelH / 2f));
        y -= labelH + 6f;
        fromInput = VRUIFactory.CreateInputField(go.transform, W - pad * 2, inputH, new Vector2(0, y - inputH / 2f), "", "1",
            new Color(0.12f, 0.18f, 0.14f, 1f), new Color(0.5f, 0.7f, 0.5f, 0.8f), 22, InputField.ContentType.IntegerNumber);
        y -= inputH + pad;

        VRUIFactory.CreateCenteredLabel(go.transform, "Hasta residuo:", W, 16 + 6, 16, new Vector2(0, y - labelH / 2f));
        y -= labelH + 6f;
        toInput = VRUIFactory.CreateInputField(go.transform, W - pad * 2, inputH, new Vector2(0, y - inputH / 2f), "", "166",
            new Color(0.12f, 0.18f, 0.14f, 1f), new Color(0.5f, 0.7f, 0.5f, 0.8f), 22, InputField.ContentType.IntegerNumber);
        y -= inputH + pad;

        float halfW = (W - pad * 3f) / 2f;
        var selBtn = VRUIFactory.CreateButton(go.transform, "SELECCIONAR",
            new Vector2(halfW, btnH), new Vector2(-halfW / 2f - pad / 2f, y - btnH / 2f), btnGreen, fontSize: 18);
        selBtn.onClick.AddListener(OnSelectClicked);
        var clrBtn = VRUIFactory.CreateButton(go.transform, "LIMPIAR",
            new Vector2(halfW, btnH), new Vector2(halfW / 2f + pad / 2f, y - btnH / 2f), btnRed, fontSize: 18);
        clrBtn.onClick.AddListener(OnClearClicked);
        y -= btnH + pad;

        statusText = VRUIFactory.CreateCenteredLabel(go.transform, "Elige un rango y pulsa SELECCIONAR.",
            W - pad * 2, statusH, 13, new Vector2(0, y - statusH / 2f));
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.alignment = TextAnchor.UpperCenter;

        BuildNumericKeyboard(go.transform, W);
    }

}
}
