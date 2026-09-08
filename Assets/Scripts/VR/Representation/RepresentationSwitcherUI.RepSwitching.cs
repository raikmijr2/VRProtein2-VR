using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UMol.API;

namespace UMol {

public partial class RepresentationSwitcherUI {

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
                if (a.isHET && !LigandSolventTable.Residues.Contains(a.residue.name)) {
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
                if (a.isHET && !LigandSolventTable.Residues.Contains(a.residue.name))
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

        var snapshot = new List<UnityMolRepresentation>(repManager.representations);
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
}
}
