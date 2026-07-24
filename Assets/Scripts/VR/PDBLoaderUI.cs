using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UMol.API;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

/// <summary>
/// Panel VR para cargar proteínas PDB por código RCSB.
/// Aplica automáticamente Cartoon a la proteína y HyperBall al ligando (HETATM).
/// </summary>
public class PDBLoaderUI : MonoBehaviour {

    public Vector3 spawnPosition = new Vector3(0.4f, 1.4f, 1.2f);
    public string defaultPDBCode = "5P21";

    static readonly Color bg        = new Color(0.06f, 0.08f, 0.14f, 0.95f);
    static readonly Color btnBlue   = new Color(0.15f, 0.30f, 0.65f, 1f);
    static readonly Color btnGreen  = new Color(0.10f, 0.55f, 0.25f, 1f);
    static readonly Color btnOrange = new Color(0.65f, 0.35f, 0.00f, 1f);

    // Residuos a excluir del "ligando" (disolvente, iones comunes)
    static readonly HashSet<string> solventResidues = new HashSet<string> {
        "HOH", "WAT", "TIP", "TIP3", "SOL", "NA", "CL", "MG", "ZN", "CA",
        "K", "NA+", "CL-", "MG2+", "ZN2+", "CA2+", "FE", "MN", "NI", "CU"
    };

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
            } else if (!solventResidues.Contains(a.residue.name)) {
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

        var go = new GameObject("PDBLoaderPanel");
        go.transform.position = spawnPosition;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        go.AddComponent<GraphicRaycaster>();
        go.AddComponent<CanvasRaycastTarget>();
        go.AddComponent<PointerMoveUI>().moveParent = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(W, H);
        go.transform.localScale = Vector3.one * 0.003f;

        // Fondo
        MakeFullImage(go.transform, bg);

        float y = H / 2f - pad;

        // Título
        AddLabel(go.transform, "CARGAR PROTEÍNA PDB", W, 24, FontStyle.Bold, new Vector2(0, y - 14));
        y -= 28 + pad;

        // Separador
        AddSeparator(go.transform, y);
        y -= 4 + pad;

        // Label + input PDB
        AddLabel(go.transform, "Código PDB (ej: 5P21):", W, 16, FontStyle.Normal, new Vector2(0, y - 10));
        y -= 20 + 6;

        pdbInput = AddInputField(go.transform, W - pad * 2, 38, new Vector2(0, y - 19), defaultPDBCode);
        y -= 38 + pad;

        // Botón cargar
        loadBtn = MakeBtn(go.transform, "CARGAR Y VISUALIZAR", new Vector2(W - pad * 2, 50), new Vector2(0, y - 25), btnGreen);
        loadBtn.onClick.AddListener(OnLoadClicked);
        y -= 50 + pad;

        // Status
        statusText = AddTextGO(go.transform, "Listo.", W - pad * 2, 36f, new Vector2(0, y - 18), 13);
        statusText.alignment = TextAnchor.UpperCenter;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        y -= 36 + pad;

        // Botón ir a proteína
        gotoBtn = MakeBtn(go.transform, "IR A PROTEÍNA", new Vector2(W - pad * 2, 40), new Vector2(0, y - 20), btnOrange);
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

    // ── UI helpers ───────────────────────────────────────────────────────────

    static Font GetFont() =>
        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
        Resources.GetBuiltinResource<Font>("Arial.ttf");

    static void MakeFullImage(Transform p, Color c) {
        var go = new GameObject("BG"); go.transform.SetParent(p, false);
        go.AddComponent<Image>().color = c;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static void AddSeparator(Transform p, float y) {
        var go = new GameObject("Sep"); go.transform.SetParent(p, false);
        go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(380f, 2f);
        r.anchoredPosition = new Vector2(0, y);
    }

    static void AddLabel(Transform p, string text, float w, int fs, FontStyle style, Vector2 pos) {
        var go = new GameObject("Lbl"); go.transform.SetParent(p, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont(); t.fontSize = fs; t.fontStyle = style;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w, fs + 6); r.anchoredPosition = pos;
    }

    static Text AddTextGO(Transform p, string text, float w, float h, Vector2 pos, int fs) {
        var go = new GameObject("Txt"); go.transform.SetParent(p, false);
        var t = go.AddComponent<Text>();
        t.text = text; t.font = GetFont(); t.fontSize = fs;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w, h); r.anchoredPosition = pos;
        return t;
    }

    static InputField AddInputField(Transform p, float w, float h, Vector2 pos, string placeholder) {
        var go = new GameObject("Input"); go.transform.SetParent(p, false);
        go.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.22f, 1f);
        var input = go.AddComponent<InputField>();
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w, h); r.anchoredPosition = pos;

        var tGO = new GameObject("Text"); tGO.transform.SetParent(go.transform, false);
        var t = tGO.AddComponent<Text>();
        t.font = GetFont(); t.fontSize = 20; t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        var tr = tGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(6, 0); tr.offsetMax = new Vector2(-6, 0);
        input.textComponent = t;
        input.text = placeholder;

        var phGO = new GameObject("Placeholder"); phGO.transform.SetParent(go.transform, false);
        var ph = phGO.AddComponent<Text>();
        ph.font = GetFont(); ph.fontSize = 18; ph.fontStyle = FontStyle.Italic;
        ph.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        ph.text = "ej: 5P21"; ph.alignment = TextAnchor.MiddleCenter;
        var phr = phGO.GetComponent<RectTransform>();
        phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
        phr.offsetMin = new Vector2(6, 0); phr.offsetMax = new Vector2(-6, 0);
        input.placeholder = ph;

        return input;
    }

    static Button MakeBtn(Transform p, string label, Vector2 size, Vector2 pos, Color c) {
        var go = new GameObject("Btn_" + label); go.transform.SetParent(p, false);
        go.AddComponent<Image>().color = c;
        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = c;
        cb.highlightedColor = Color.Lerp(c, Color.white, 0.25f);
        cb.pressedColor     = Color.Lerp(c, Color.black, 0.30f);
        btn.colors = cb;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size; r.anchoredPosition = pos;
        var tGO = new GameObject("L"); tGO.transform.SetParent(go.transform, false);
        var t = tGO.AddComponent<Text>();
        t.text = label; t.font = GetFont(); t.fontSize = 16; t.fontStyle = FontStyle.Bold;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter;
        var tr = tGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = tr.offsetMax = Vector2.zero;
        return btn;
    }
}
}
