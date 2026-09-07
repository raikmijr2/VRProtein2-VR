using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UMol.API;
using MiniJSON;

namespace UMol {

/// <summary>
/// VR panel: downloads PRMTOP + DCD files from the Dataverse repository and plays the trajectory.
/// Dataset: https://dataverse.csuc.cat/dataset.xhtml?persistentId=doi:10.34810/DATA3230
/// </summary>
public class DataverseProteinUI : MonoBehaviour {

    public Vector3 spawnPosition = new Vector3(-0.5f, 1.2f, 1.4f);

    // ── Dataverse constants ───────────────────────────────────────────────────
    const string BASE_URL   = "https://dataverse.csuc.cat";
    const string DATASET_ID = "doi:10.34810/DATA3230";
    const string FILES_API  = "/api/datasets/:persistentId/versions/:latest/files?persistentId=";
    const string DOWNLOAD   = "/api/access/datafile/";

    // Known PRMTOP file IDs (top-level, don't need to search for them)
    const int ID_LYSINE_PRMTOP = 452546;  // lysine.prmtop (unmodified)
    const int ID_ACK28_PRMTOP  = 452545;  // ack28.prmtop  (modified)

    // Cached file list path
    string CachePath => Path.Combine(Application.persistentDataPath, "dataverse_filelist.json");
    string DataPath  => Application.persistentDataPath;

    // ── State ─────────────────────────────────────────────────────────────────
    enum Mode { Unmodified, Modified }
    enum Run  { Run1, Run2 }

    Mode selectedMode = Mode.Unmodified;
    Run  selectedRun  = Run.Run1;
    int  nDCDFiles    = 5;

    List<FileEntry> allFiles = new List<FileEntry>();
    bool catalogReady = false;
    string lastLoadedStructName = null;

    // ── UI refs ───────────────────────────────────────────────────────────────
    Text   statusText;
    Text   progressText;
    Button downloadBtn;
    Button gotoBtn;
    Button[] modeButtons = new Button[2];
    Button[] runButtons  = new Button[2];
    Text     dcdCountLabel;

    static readonly Color bg        = new Color(0.06f, 0.08f, 0.14f, 0.95f);
    static readonly Color btnBlue   = new Color(0.15f, 0.30f, 0.65f, 1f);
    static readonly Color btnActive = new Color(0.10f, 0.55f, 0.25f, 1f);
    static readonly Color btnRed    = new Color(0.60f, 0.15f, 0.15f, 1f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start() {
        BuildPanel();
        StartCoroutine(LoadCatalog());
    }

    // ── Catalog ───────────────────────────────────────────────────────────────

    IEnumerator LoadCatalog() {
        SetStatus("Cargando catálogo...", Color.white);

        // Try cache first (valid for 7 days)
        if (File.Exists(CachePath)) {
            FileInfo fi = new FileInfo(CachePath);
            if ((DateTime.Now - fi.LastWriteTime).TotalDays < 7) {
                string cached = File.ReadAllText(CachePath);
                ParseFileList(cached);
                if (allFiles.Count > 0) {
                    catalogReady = true;
                    SetStatus($"Catálogo cargado ({allFiles.Count} archivos)", Color.green);
                    yield break;
                }
            }
        }

        // Fetch from API — try large limit first, paginate if needed
        // Dataverse /api/.../files returns {"status":"OK","data":[...]} (data is a list)
        var collected = new List<object>();
        int offset = 0, limit = 1000;
        bool more = true;

        while (more) {
            string url = $"{BASE_URL}{FILES_API}{Uri.EscapeDataString(DATASET_ID)}&limit={limit}&offset={offset}";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Accept", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) {
                SetStatus("Error al cargar catálogo: " + req.error, Color.red);
                yield break;
            }

            var root = Json.Deserialize(req.downloadHandler.text) as Dictionary<string, object>;
            if (root == null) { SetStatus("JSON inválido", Color.red); yield break; }

            // Dataverse standard: data is a List<object>
            List<object> page = null;
            if (root.ContainsKey("data")) {
                page = root["data"] as List<object>;
                if (page == null) {
                    // Fallback: data might be a dict with "files" key (some versions)
                    var dataDict = root["data"] as Dictionary<string, object>;
                    if (dataDict != null && dataDict.ContainsKey("files"))
                        page = dataDict["files"] as List<object>;
                }
            }

            if (page == null || page.Count == 0) break;
            collected.AddRange(page);
            more = page.Count == limit;  // if we got a full page, there may be more
            offset += limit;

            SetStatus($"Catálogo: {collected.Count} archivos...", Color.white);
            yield return null;
        }

        // Serialize collected entries and cache (flat list under "files" key)
        var cacheRoot = new Dictionary<string, object> { ["files"] = collected };
        string cacheJson = Json.Serialize(cacheRoot);
        File.WriteAllText(CachePath, cacheJson);

        ParseFileList(collected);
        catalogReady = true;
        SetStatus($"Catálogo listo ({allFiles.Count} archivos DCD)", Color.green);
    }

    void ParseFileList(string json) {
        allFiles.Clear();
        var root  = Json.Deserialize(json) as Dictionary<string, object>;
        if (root == null) return;
        List<object> files = null;
        if (root.ContainsKey("files"))
            files = root["files"] as List<object>;
        if (files == null && root.ContainsKey("data"))
            files = root["data"] as List<object>;
        if (files == null) return;
        ParseFileList(files);
    }

    void ParseFileList(List<object> files) {
        allFiles.Clear();
        if (files == null) return;

        foreach (var f in files) {
            var entry = f as Dictionary<string, object>;
            if (entry == null) continue;
            var df  = entry.ContainsKey("dataFile") ? entry["dataFile"] as Dictionary<string, object> : null;
            if (df  == null) continue;
            string dir   = entry.ContainsKey("directoryLabel") ? entry["directoryLabel"] as string ?? "" : "";
            string label = entry.ContainsKey("label") ? entry["label"] as string ?? "" : "";
            int    id    = df.ContainsKey("id") ? Convert.ToInt32(df["id"]) : 0;
            if (id == 0 || !label.EndsWith(".dcd", StringComparison.OrdinalIgnoreCase)) continue;
            allFiles.Add(new FileEntry { id = id, name = label, dir = dir });
        }
    }

    // ── Download + Visualize ──────────────────────────────────────────────────

    void OnDownloadClicked() {
        if (!catalogReady) { SetStatus("Esperando catálogo...", Color.yellow); return; }
        StartCoroutine(DownloadAndVisualize());
    }

    IEnumerator DownloadAndVisualize() {
        downloadBtn.interactable = false;

        // 1. Determine which PRMTOP and which DCD folder to use
        int    prmtopId   = (selectedMode == Mode.Unmodified) ? ID_LYSINE_PRMTOP : ID_ACK28_PRMTOP;
        string prmtopName = (selectedMode == Mode.Unmodified) ? "lysine.prmtop"  : "ack28.prmtop";
        string dirFilter  = BuildDirFilter();

        // 2. Filter + sort DCD files — keyword matching so minor name differences don't break it
        var filtered = new List<FileEntry>();
        foreach (var f in allFiles)
            if (MatchesSelection(f.dir, f.name))
                filtered.Add(f);

        filtered.Sort((a, b) => ExtractNumber(a.name).CompareTo(ExtractNumber(b.name)));

        if (filtered.Count == 0) {
            // Build a short list of unique dirs for the VR status text
            var dirs = new System.Collections.Generic.HashSet<string>();
            foreach (var f in allFiles) if (!string.IsNullOrEmpty(f.dir)) dirs.Add(f.dir);
            string hint = "";
            int n = 0;
            foreach (var d in dirs) { hint += "\n" + d; if (++n >= 5) { hint += "\n…"; break; } }
            SetStatus($"Sin DCDs para {dirFilter}\nDirs en catálogo:{hint}", Color.red);
            downloadBtn.interactable = true;
            yield break;
        }

        var toDownload = filtered.GetRange(0, Mathf.Min(nDCDFiles, filtered.Count));
        SetStatus($"Descargando {toDownload.Count + 1} archivos...", Color.white);

        // 3. Download PRMTOP if needed
        string prmtopPath = Path.Combine(DataPath, prmtopName);
        if (!File.Exists(prmtopPath)) {
            SetProgress(0, toDownload.Count + 1);
            yield return DownloadFile(prmtopId, prmtopPath);
            if (!File.Exists(prmtopPath)) {
                SetStatus("Error descargando PRMTOP", Color.red);
                downloadBtn.interactable = true;
                yield break;
            }
        }

        // 4. Download DCD files
        string dcdFolder = Path.Combine(DataPath, dirFilter);
        Directory.CreateDirectory(dcdFolder);

        for (int i = 0; i < toDownload.Count; i++) {
            SetProgress(i + 1, toDownload.Count + 1);
            string dcdPath = Path.Combine(dcdFolder, toDownload[i].name);
            if (!File.Exists(dcdPath))
                yield return DownloadFile(toDownload[i].id, dcdPath);
        }

        SetProgress(toDownload.Count + 1, toDownload.Count + 1);
        SetStatus("Descarga completa. Cargando proteína...", Color.green);
        yield return null;

        // 5. Load and visualize
        yield return LoadAndVisualize(prmtopPath, dcdFolder);
        downloadBtn.interactable = true;
    }

    IEnumerator DownloadFile(int fileId, string savePath) {
        string url = $"{BASE_URL}{DOWNLOAD}{fileId}";
        using var req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerFile(savePath);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
            Debug.LogError($"Download failed {url}: {req.error}");
    }

    IEnumerator LoadAndVisualize(string prmtopPath, string dcdFolder) {
        int prevStructCount = UnityMolMain.getStructureManager().loadedStructures.Count;

        // [1] Read DCD on background thread
        int natom = 0;
        List<Vector3[]> frames = null;
        string dcdError = null;
        SetStatus("Leyendo frames DCD...", Color.white);
        var dcdTask = System.Threading.Tasks.Task.Run(() => {
            try { frames = DCDReader.ReadFolder(dcdFolder, out natom); }
            catch (Exception e) { dcdError = e.Message; }
        });
        while (!dcdTask.IsCompleted) yield return null;

        if (dcdError != null) { SetStatus("Error DCD: " + dcdError, Color.red); downloadBtn.interactable = true; yield break; }
        if (frames == null || frames.Count == 0) { SetStatus("Sin frames DCD en " + dcdFolder, Color.red); downloadBtn.interactable = true; yield break; }

        SetStatus($"DCD: {frames.Count} frames × {natom} átomos. Cargando PRMTOP...", Color.white);
        yield return null;

        // [2] Load PRMTOP (uses Unity API, must be on main thread)
        // Disable surface pre-computation: EDTSurfLib (native DLL) crashes on ack28 geometry.
        // Trajectory proteins are shown as HyperBall anyway, so pre-computed surfaces are unused.
        bool prevDisableSurf = UnityMolMain.disableSurfaceThread;
        UnityMolMain.disableSurfaceThread = true;
        UnityMolStructure s = null;
        int[] dcdIndices = null;
        try { s = PRMTOPReader.Load(prmtopPath, frames[0], out dcdIndices); }
        catch (Exception e) {
            UnityMolMain.disableSurfaceThread = prevDisableSurf;
            SetStatus("Error PRMTOP: " + e.Message, Color.red); downloadBtn.interactable = true; yield break;
        }
        UnityMolMain.disableSurfaceThread = prevDisableSurf;
        if (s == null) { SetStatus("Error: estructura nula tras cargar PRMTOP.", Color.red); downloadBtn.interactable = true; yield break; }

        int proteinAtoms = dcdIndices != null ? dcdIndices.Length : natom;

        if (proteinAtoms == 0) {
            SetStatus($"Error: 0 átomos proteína en {Path.GetFileName(prmtopPath)}.\nTodos se filtraron como solvente.", Color.red);
            downloadBtn.interactable = true; yield break;
        }

        // [3] Filter frames on background thread to keep only protein atoms
        int solventAtoms = natom - proteinAtoms;
        SetStatus($"Filtrando {frames.Count} frames ({solventAtoms} átomos solvente)...", Color.white);
        yield return null;

        List<Vector3[]> filteredFrames = null;
        if (dcdIndices != null && dcdIndices.Length < natom) {
            var ci = dcdIndices;
            var cf = frames;
            Exception filterEx = null;
            var filterTask = System.Threading.Tasks.Task.Run(() => {
                try { filteredFrames = FilterFrames(cf, ci); }
                catch (Exception e) { filterEx = e; }
            });
            while (!filterTask.IsCompleted) yield return null;

            if (filterEx != null) {
                filteredFrames = null;
            }
        } else {
            filteredFrames = frames;
        }

        // Free raw DCD frames ASAP — can be hundreds of MB; keep only filtered (tiny) version
        frames = null;
        System.GC.Collect();
        yield return null;

        // [3b] Sanitize and center coords to avoid Unity renderer crashes from extreme positions
        // MAX_ABS_COORD: anything beyond 1 million Å is certainly a PBC artifact or corruption
        const float MAX_ABS_COORD = 1e6f;
        if (filteredFrames != null && filteredFrames.Count > 0 && filteredFrames[0].Length > 0) {

            // Pass 1: sanitize ALL frames in background (remove NaN/Inf AND extreme coords)
            {
                var sanitizeTask = System.Threading.Tasks.Task.Run(() => {
                    foreach (var frame in filteredFrames)
                        for (int i = 0; i < frame.Length; i++) {
                            var p = frame[i];
                            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                                float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z) ||
                                p.x > MAX_ABS_COORD || p.x < -MAX_ABS_COORD ||
                                p.y > MAX_ABS_COORD || p.y < -MAX_ABS_COORD ||
                                p.z > MAX_ABS_COORD || p.z < -MAX_ABS_COORD)
                                frame[i] = Vector3.zero;
                        }
                });
                while (!sanitizeTask.IsCompleted) yield return null;
            }

            // Pass 2: compute centroid from sanitized frame 0 and shift all frames if needed
            var f0 = filteredFrames[0];
            Vector3 c0 = Vector3.zero;
            foreach (var p in f0) c0 += p;
            c0 /= f0.Length;

            if (c0.sqrMagnitude > 200f * 200f) {
                SetStatus($"Centrando coordenadas ({c0.magnitude:F0} Å)...", Color.white);
                var shiftVec = c0;
                var shiftTask = System.Threading.Tasks.Task.Run(() => {
                    foreach (var frame in filteredFrames)
                        for (int i = 0; i < frame.Length; i++)
                            frame[i] -= shiftVec;
                });
                while (!shiftTask.IsCompleted) yield return null;
            }
        } else if (filteredFrames == null) {
            // Static case: sanitize and center initial atom positions directly
            var atoms = s.currentModel.allAtoms;
            Vector3 initCentroid = Vector3.zero;
            int validCount = 0;
            foreach (var a in atoms) {
                var p = a.position;
                bool extreme = p.x > MAX_ABS_COORD || p.x < -MAX_ABS_COORD ||
                               p.y > MAX_ABS_COORD || p.y < -MAX_ABS_COORD ||
                               p.z > MAX_ABS_COORD || p.z < -MAX_ABS_COORD;
                if (!IsValidPos(p) || extreme) { a.position = Vector3.zero; }
                else { initCentroid += p; validCount++; }
            }
            if (validCount > 0) {
                initCentroid /= validCount;
                if (initCentroid.sqrMagnitude > 200f * 200f) {
                    foreach (var a in atoms) a.position -= initCentroid;
                }
            }
        }

        // [4] Set up trajectory if we have filtered frames
        if (filteredFrames != null && filteredFrames.Count > 0) {
            s.modelFrames    = filteredFrames;
            s.trajectoryMode = true;
            s.createModelPlayer();
            APIPython.setModel(s.name, 0);  // updates atom positions to frame 0
        } else {
        }

        // [5] Create hyperball representation for THIS structure only
        string selName = s.ToSelectionName();
        APIPython.showSelection(selName, "hb");
        yield return null;

        // Verify a rep was actually created
        int repCount = s.representations != null ? s.representations.Count : 0;
        if (repCount == 0) {
            var selM2 = UnityMolMain.getSelectionManager();
            if (selM2.selections.TryGetValue(selName, out var directSel)) {
                RepType hbType = APIPython.getRepType("hb");
                UnityMolMain.getRepresentationManager()
                    .AddRepresentation(directSel, hbType.atomType, hbType.bondType);
                yield return null;
                repCount = s.representations != null ? s.representations.Count : 0;
            }
        }

        // Center camera on the newly loaded structure
        APIPython.centerOnStructure(s.name, recordCommand: false);

        lastLoadedStructName = s.name;
        if (gotoBtn != null) gotoBtn.interactable = true;

        string repNote = repCount == 0 ? " ⚠ sin rep visible — pulsa IR A PROTEINA" : "";
        SetStatus($"✓ {s.name} | {filteredFrames?.Count ?? 0} frames | {proteinAtoms} át.{repNote}", Color.green);
    }

    static List<Vector3[]> FilterFrames(List<Vector3[]> frames, int[] indices) {
        var result = new List<Vector3[]>(frames.Count);
        foreach (var frame in frames) {
            var filtered = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                filtered[i] = frame[indices[i]];
            result.Add(filtered);
        }
        return result;
    }

    static bool IsValidPos(Vector3 p) =>
        !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
        !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Human-readable label for status messages
    string BuildDirFilter() {
        string m = selectedMode == Mode.Unmodified ? "UNMODIFIED" : "MODIFIED";
        string r = selectedRun  == Run.Run1        ? "RUN_1"      : "RUN_2";
        return $"{m}+{r}";
    }

    // Flexible keyword match: checks mode and run independently so the middle part
    // of the directory name (LYSINE / ACK28 / etc.) does not matter.
    bool MatchesSelection(string dir, string fileName) {
        string s = (dir + " " + fileName).ToUpperInvariant();

        bool modeOk = selectedMode == Mode.Unmodified
            ? s.Contains("UNMODIFIED")
            : s.Contains("MODIFIED") && !s.Contains("UNMODIFIED");

        bool runOk = selectedRun == Run.Run1
            ? s.Contains("RUN_1") || s.Contains("RUN1")
            : s.Contains("RUN_2") || s.Contains("RUN2");

        return modeOk && runOk && (s.EndsWith(".DCD") || fileName.ToUpperInvariant().EndsWith(".DCD"));
    }

    static int ExtractNumber(string name) {
        // Extracts the trailing frame number from names like "lysine_prod_101.dcd"
        string stem = Path.GetFileNameWithoutExtension(name);
        int last_ = stem.LastIndexOf('_');
        if (last_ >= 0 && int.TryParse(stem.Substring(last_ + 1), out int n)) return n;
        return 0;
    }

    void SetStatus(string msg, Color col) {
        if (statusText) { statusText.text = msg; statusText.color = col; }
    }

    void SetProgress(int done, int total) {
        if (progressText) progressText.text = total > 0 ? $"Archivos: {done}/{total}" : "";
    }

    // ── Panel builder ─────────────────────────────────────────────────────────

    void BuildPanel() {
        float W = 500f, H = 640f, pad = 12f;
        var go = VRUIFactory.CreateWorldSpaceCanvas("DataversePanelRoot", spawnPosition, new Vector2(W, H));

        // BG
        VRUIFactory.CreateBackgroundImage(go.transform, bg);

        float y = H / 2f - pad;

        // Title
        VRUIFactory.CreateCenteredLabel(go.transform, "SIMULACION MOLECULAR", W, 26 + 6, 26, new Vector2(0, y - 13), FontStyle.Bold);
        y -= 26 + pad;

        // Separator
        y -= 4;

        // Mode row
        VRUIFactory.CreateCenteredLabel(go.transform, "Tipo de lisina:", W, 18 + 6, 18, new Vector2(0, y - 9), FontStyle.Bold);
        y -= 18 + 6;
        float bW2 = W / 2f - pad * 1.5f;
        modeButtons[0] = VRUIFactory.CreateButton(go.transform, "Sin Modificar", new Vector2(bW2, 44), new Vector2(-bW2 / 2f - pad / 2f, y - 22), btnActive, fontSize: 17);
        modeButtons[0].onClick.AddListener(() => SelectMode(Mode.Unmodified));
        modeButtons[1] = VRUIFactory.CreateButton(go.transform, "Modificada",    new Vector2(bW2, 44), new Vector2( bW2 / 2f + pad / 2f, y - 22), btnBlue, fontSize: 17);
        modeButtons[1].onClick.AddListener(() => SelectMode(Mode.Modified));
        y -= 44 + pad;

        // Run row
        VRUIFactory.CreateCenteredLabel(go.transform, "Simulacion:", W, 18 + 6, 18, new Vector2(0, y - 9), FontStyle.Bold);
        y -= 18 + 6;
        runButtons[0] = VRUIFactory.CreateButton(go.transform, "Run 1", new Vector2(bW2, 44), new Vector2(-bW2 / 2f - pad / 2f, y - 22), btnActive, fontSize: 17);
        runButtons[0].onClick.AddListener(() => SelectRun(Run.Run1));
        runButtons[1] = VRUIFactory.CreateButton(go.transform, "Run 2", new Vector2(bW2, 44), new Vector2( bW2 / 2f + pad / 2f, y - 22), btnBlue, fontSize: 17);
        runButtons[1].onClick.AddListener(() => SelectRun(Run.Run2));
        y -= 44 + pad;

        // DCD count row: [ - ]  [ 5 ]  [ + ]
        VRUIFactory.CreateCenteredLabel(go.transform, "Archivos DCD (aprox. 100 frames c/u):", W, 16 + 6, 16, new Vector2(0, y - 8));
        y -= 16 + 6;
        float btnS = 50f;
        var btnMinus = VRUIFactory.CreateButton(go.transform, "-", new Vector2(btnS, 44), new Vector2(-80f, y - 22), btnBlue, fontSize: 17);
        btnMinus.onClick.AddListener(() => { nDCDFiles = Mathf.Max(1, nDCDFiles - 1); UpdateDCDLabel(); });
        VRUIFactory.AddHoldBehavior(btnMinus.gameObject, () => { nDCDFiles = Mathf.Max(1, nDCDFiles - 1); UpdateDCDLabel(); });
        dcdCountLabel = VRUIFactory.CreateCenteredLabel(go.transform, "5", 80f, 44f, 22, new Vector2(0f, y - 22));
        var btnPlus  = VRUIFactory.CreateButton(go.transform, "+", new Vector2(btnS, 44), new Vector2( 80f, y - 22), btnBlue, fontSize: 17);
        btnPlus.onClick.AddListener(() => { nDCDFiles = Mathf.Min(50, nDCDFiles + 1); UpdateDCDLabel(); });
        VRUIFactory.AddHoldBehavior(btnPlus.gameObject, () => { nDCDFiles = Mathf.Min(50, nDCDFiles + 1); UpdateDCDLabel(); });
        y -= 44 + pad;

        // Progress text
        progressText = VRUIFactory.CreateCenteredLabel(go.transform, "", W - pad * 2, 24f, 15, new Vector2(0, y - 12));
        y -= 24 + 4;

        // Status (taller to fit multi-line error messages with dir names)
        statusText = VRUIFactory.CreateCenteredLabel(go.transform, "Iniciando...", W - pad * 2, 80f, 14, new Vector2(0, y - 40));
        statusText.alignment = TextAnchor.UpperCenter;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow   = VerticalWrapMode.Overflow;
        y -= 80 + pad;

        // Download button
        downloadBtn = VRUIFactory.CreateButton(go.transform, "DESCARGAR Y VISUALIZAR", new Vector2(W - pad * 2, 56), new Vector2(0, y - 28), btnBlue, fontSize: 17);
        downloadBtn.onClick.AddListener(OnDownloadClicked);
        y -= 56 + pad;

        // Go-to button (re-centers camera on the last loaded structure)
        gotoBtn = VRUIFactory.CreateButton(go.transform, "IR A PROTEINA", new Vector2(W - pad * 2, 44), new Vector2(0, y - 22), btnRed, fontSize: 17);
        gotoBtn.onClick.AddListener(OnGotoClicked);
        gotoBtn.interactable = false;
    }

    void OnGotoClicked() {
        if (string.IsNullOrEmpty(lastLoadedStructName)) return;
        var sm = UnityMolMain.getStructureManager();
        if (sm.GetStructure(lastLoadedStructName) == null) return;
        APIPython.centerOnStructure(lastLoadedStructName, recordCommand: false);
        SetStatus($"Centrando en {lastLoadedStructName}", Color.cyan);
    }

    void UpdateDCDLabel() {
        if (dcdCountLabel) dcdCountLabel.text = nDCDFiles.ToString();
    }

    void SelectMode(Mode m) {
        selectedMode = m;
        if (modeButtons[0]) SetBtnColor(modeButtons[0], m == Mode.Unmodified ? btnActive : btnBlue);
        if (modeButtons[1]) SetBtnColor(modeButtons[1], m == Mode.Modified   ? btnActive : btnBlue);
    }

    void SelectRun(Run r) {
        selectedRun = r;
        if (runButtons[0]) SetBtnColor(runButtons[0], r == Run.Run1 ? btnActive : btnBlue);
        if (runButtons[1]) SetBtnColor(runButtons[1], r == Run.Run2 ? btnActive : btnBlue);
    }

    static void SetBtnColor(Button b, Color c) {
        b.GetComponent<Image>().color = c;
        var cb = b.colors; cb.normalColor = c; b.colors = cb;
    }

    // ── Data model ────────────────────────────────────────────────────────────

    struct FileEntry {
        public int    id;
        public string name;
        public string dir;
    }
}
}
