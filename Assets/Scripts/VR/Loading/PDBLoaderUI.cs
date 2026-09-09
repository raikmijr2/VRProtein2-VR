using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UMol.API;

namespace UMol {

/// <summary>
/// Panel VR para cargar proteínas PDB por código RCSB.
/// Aplica automáticamente Cartoon a la proteína y HyperBall al ligando (HETATM).
/// La jerarquía UI es un prefab construido en el Editor (ver PDBLoaderPanel.prefab);
/// este script solo contiene la lógica de negocio y las referencias a sus partes.
/// </summary>
public class PDBLoaderUI : MonoBehaviour {

    public string defaultPDBCode = "5P21";

    [SerializeField] InputField pdbInput;
    [SerializeField] Text       statusText;
    [SerializeField] Button     loadBtn;
    [SerializeField] Button     gotoBtn;

    string lastLoadedName;

    void Start() {
        if (pdbInput != null) pdbInput.text = defaultPDBCode;
        if (gotoBtn  != null) gotoBtn.interactable = false;
    }

    // ── Carga ────────────────────────────────────────────────────────────────

    public void OnLoadClicked() {
        string code = pdbInput != null ? pdbInput.text.Trim().ToUpper() : defaultPDBCode.ToUpper();
        if (string.IsNullOrEmpty(code)) { SetStatus("Introduce un código PDB.", Color.yellow); return; }
        StartCoroutine(LoadPDB(code));
    }

    public void OnGotoClicked() {
        if (!string.IsNullOrEmpty(lastLoadedName))
            APIPython.centerOnStructure(lastLoadedName, recordCommand: false);
    }

    IEnumerator LoadPDB(string code) {
        if (loadBtn) loadBtn.interactable = false;
        SetStatus($"Descargando {code}...", Color.white);

        // Intentar primero PDB plano (más compatible con proteínas con lligando)
        string url  = $"https://files.rcsb.org/download/{code}.pdb";
        string path = Path.Combine(Application.persistentDataPath, code + ".pdb");

        using (var req = UnityWebRequest.Get(url)) {
            req.downloadHandler = new DownloadHandlerFile(path);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) {
                SetStatus($"Error al descargar {code}: {req.error}", Color.red);
                if (loadBtn) loadBtn.interactable = true;
                yield break;
            }
        }

        SetStatus($"Cargando {code}...", Color.white);
        yield return null;

        // Cargar sin representación por defecto para controlarla nosotros
        UnityMolStructure s = null;
        try {
            s = APIPython.load(path, readHetm: true, showDefaultRep: false, center: true);
        } catch (System.Exception e) {
            SetStatus($"Error cargando {code}: {e.Message}", Color.red);
            if (loadBtn) loadBtn.interactable = true;
            yield break;
        }

        if (s == null) {
            SetStatus($"No se pudo cargar {code}.", Color.red);
            if (loadBtn) loadBtn.interactable = true;
            yield break;
        }

        yield return null;
        ApplyDefaultRepresentations(s);

        lastLoadedName = s.name;
        if (gotoBtn) gotoBtn.interactable = true;
        if (loadBtn) loadBtn.interactable = true;
    }

    // ── Representaciones automáticas ─────────────────────────────────────────

    void ApplyDefaultRepresentations(UnityMolStructure s) {
        var atoms = s.currentModel.allAtoms;
        var selMgr = UnityMolMain.getSelectionManager();

        // Separar proteína y ligando
        var proteinAtoms = new List<UnityMolAtom>();
        var ligandAtoms  = new List<UnityMolAtom>();

        foreach (var a in atoms) {
            if (!a.isHET) {
                proteinAtoms.Add(a);
            } else if (!LigandSolventTable.Residues.Contains(a.residue.name)) {
                ligandAtoms.Add(a);
            }
        }

        // Proteína → Cartoon (ribbon)
        if (proteinAtoms.Count > 0) {
            string protSelName = "protein_" + s.name;
            var protSel = new UnityMolSelection(proteinAtoms, protSelName);
            selMgr.selections[protSelName] = protSel;
            APIPython.showSelection(protSelName, "c");
        }

        // Ligando → HyperBall (ball & stick)
        if (ligandAtoms.Count > 0) {
            string ligSelName = "ligand_" + s.name;
            var ligSel = new UnityMolSelection(ligandAtoms, ligSelName);
            selMgr.selections[ligSelName] = ligSel;
            APIPython.showSelection(ligSelName, "hb");

            // Info sobre el ligando detectado
            var ligResidues = new HashSet<string>();
            foreach (var a in ligandAtoms) ligResidues.Add(a.residue.name);
            string ligNames = string.Join(", ", ligResidues);
            SetStatus($"✓ {s.name} | Proteína: {proteinAtoms.Count} át. | Ligando: {ligandAtoms.Count} át. ({ligNames})", Color.green);
        } else {
            SetStatus($"✓ {s.name} | {proteinAtoms.Count} át. | Sin ligando HETATM.", Color.green);
        }
    }

    void SetStatus(string msg, Color col) {
        if (statusText) { statusText.text = msg; statusText.color = col; }
        Debug.Log("[PDBLoader] " + msg);
    }
}
}
