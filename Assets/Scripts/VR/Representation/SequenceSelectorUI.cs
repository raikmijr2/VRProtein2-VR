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
/// La jerarquía UI (panel + teclado numérico) es un prefab construido en el
/// Editor (ver SequenceSelectorPanel.prefab); este script solo contiene la
/// lógica de negocio y las referencias a sus partes.
/// </summary>
public class SequenceSelectorUI : MonoBehaviour {

    [SerializeField] InputField fromInput;
    [SerializeField] InputField toInput;
    [SerializeField] Text       statusText;
    [SerializeField] Text       rangeLabel;
    [SerializeField] GameObject numKeyboardGO;

    int lastStructCount = -1;
    int lastRepCount = -1;
    HashSet<UnityMolAtom> seqHighlightedAtoms = new HashSet<UnityMolAtom>();

    InputField activeInput;

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

    public void OnSelectClicked() {
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

    public void OnClearClicked() {
        SeqClearHighlight();
        var selMgr = UnityMolMain.getSelectionManager();
        if (selMgr.currentSelection != null && selMgr.currentSelection.isAlterable) {
            APIPython.select("nothing", selMgr.currentSelection.name,
                             createSelection: true, addToExisting: false, silent: true);
        }
        SetStatus("Selección limpiada.", Color.white);
    }

    void SeqApplyHighlight(List<UnityMolAtom> atoms) {
        if (!HighlightService.ApplyHighlight(atoms)) return;
        foreach (var a in atoms) seqHighlightedAtoms.Add(a);
    }

    void SeqRefreshHighlight() {
        HighlightService.ApplyHighlight(new List<UnityMolAtom>(seqHighlightedAtoms));
    }

    void SeqClearHighlight() {
        if (seqHighlightedAtoms.Count == 0) return;
        var repManager = UnityMolMain.getRepresentationManager();
        if (repManager == null) { seqHighlightedAtoms.Clear(); return; }
        foreach (var rep in repManager.representations)
            rep.ResetColor();
        seqHighlightedAtoms.Clear();
    }

    /// <summary>Public entry point so other panels (e.g. RepresentationSwitcherUI's Reiniciar) can clear this panel's own yellow highlight tracking too.</summary>
    public void ResetHighlights() {
        SeqClearHighlight();
    }

    void SetStatus(string msg, Color col) {
        if (statusText) { statusText.text = msg; statusText.color = col; }
        Debug.Log("[SeqSelector] " + msg);
    }

    // ── Teclado numérico ─────────────────────────────────────────────────────

    public void TypeKey(string k) {
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
}
}
