using UnityEngine;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UMol.API;

namespace UMol {

public partial class RepresentationSwitcherUI {

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

        // BUG FIX: File.WriteAllText no tenía try/catch — un fallo de escritura (permisos,
        // disco lleno en el headset) tiraba una excepción sin capturar y el usuario no veía
        // ningún feedback de que la exportación había fallado.
        try {
            File.WriteAllText(path, sb.ToString());
        } catch (System.Exception e) {
            Debug.LogError("[VRProtein] Error al exportar PDB: " + e.Message);
            StartCoroutine(FlashExportError());
            return;
        }

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

    IEnumerator FlashExportError() {
        if (exportBtnLabel == null || exportBtnImg == null) yield break;
        exportBtnLabel.text  = "Error al guardar";
        exportBtnImg.color   = new Color(0.70f, 0.10f, 0.10f, 1f);
        yield return new WaitForSeconds(2.5f);
        exportBtnLabel.text  = "Exportar PDB";
        exportBtnImg.color   = btnExport;
    }
}
}
