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
/// </summary>
public class PDBLoaderUI : MonoBehaviour {

    public Vector3 spawnPosition = new Vector3(0.4f, 1.4f, 1.2f);
    public string defaultPDBCode = "5P21";

    static readonly Color bg        = new Color(0.06f, 0.08f, 0.14f, 0.95f);
    static readonly Color btnGreen  = new Color(0.10f, 0.55f, 0.25f, 1f);
    static readonly Color btnOrange = new Color(0.65f, 0.35f, 0.00f, 1f);

    InputField pdbInput;
    Text       statusText;
    Button     loadBtn;
    Button     gotoBtn;
    string     lastLoadedName;

    void Start() => BuildPanel();

    // ── Carga ────────────────────────────────────────────────────────────────

    void OnLoadClicked() {
        string code = pdbInput != null ? pdbInput.text.Trim().ToUpper() : defaultPDBCode.ToUpper();
        if (string.IsNullOrEmpty(code)) { SetStatus("Introduce un código PDB.", Color.yellow); return; }
        StartCoroutine(LoadPDB(code));
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

    // ── Panel ────────────────────────────────────────────────────────────────

    void BuildPanel() {
        const float W = 420f, H = 280f, pad = 12f;

        var go = VRUIFactory.CreateWorldSpaceCanvas("PDBLoaderPanel", spawnPosition, new Vector2(W, H));

        // Fondo
        VRUIFactory.CreateBackgroundImage(go.transform, bg);

        float y = H / 2f - pad;

        // Título
        VRUIFactory.CreateCenteredLabel(go.transform, "CARGAR PROTEÍNA PDB", W, 24 + 6, 24, new Vector2(0, y - 14), FontStyle.Bold);
        y -= 28 + pad;

        // Separador
        VRUIFactory.CreateSeparator(go.transform, new Vector2(0, y), 380f);
        y -= 4 + pad;

        // Label + input PDB
        VRUIFactory.CreateCenteredLabel(go.transform, "Código PDB (ej: 5P21):", W, 16 + 6, 16, new Vector2(0, y - 10));
        y -= 20 + 6;

        pdbInput = VRUIFactory.CreateInputField(go.transform, W - pad * 2, 38, new Vector2(0, y - 19), defaultPDBCode, "ej: 5P21");
        y -= 38 + pad;

        // Botón cargar
        loadBtn = VRUIFactory.CreateButton(go.transform, "CARGAR Y VISUALIZAR", new Vector2(W - pad * 2, 50), new Vector2(0, y - 25), btnGreen);
        loadBtn.onClick.AddListener(OnLoadClicked);
        y -= 50 + pad;

        // Status
        statusText = VRUIFactory.CreateCenteredLabel(go.transform, "Listo.", W - pad * 2, 36f, 13, new Vector2(0, y - 18));
        statusText.alignment = TextAnchor.UpperCenter;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        y -= 36 + pad;

        // Botón ir a proteína
        gotoBtn = VRUIFactory.CreateButton(go.transform, "IR A PROTEÍNA", new Vector2(W - pad * 2, 40), new Vector2(0, y - 20), btnOrange);
        gotoBtn.onClick.AddListener(() => {
            if (!string.IsNullOrEmpty(lastLoadedName))
                APIPython.centerOnStructure(lastLoadedName, recordCommand: false);
        });
        gotoBtn.interactable = false;
    }

    void SetStatus(string msg, Color col) {
        if (statusText) { statusText.text = msg; statusText.color = col; }
        Debug.Log("[PDBLoader] " + msg);
    }
}
}
