using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UMol.API;

namespace UMol {

/// <summary>
/// VR panel: downloads PRMTOP + DCD files from the Dataverse repository and plays the trajectory.
/// Dataset: https://dataverse.csuc.cat/dataset.xhtml?persistentId=doi:10.34810/DATA3230
/// Split across 3 files: this one (fields, lifecycle, mode/run glue), .Catalog.cs
/// (fetching/caching the Dataverse file list) and .Download.cs (download + load pipeline).
/// </summary>
public partial class DataverseProteinUI : MonoBehaviour {

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

    // ── Callbacks ─────────────────────────────────────────────────────────────

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

    void SetStatus(string msg, Color col) {
        if (statusText) { statusText.text = msg; statusText.color = col; }
    }

    void SetProgress(int done, int total) {
        if (progressText) progressText.text = total > 0 ? $"Archivos: {done}/{total}" : "";
    }

    // ── Data model ────────────────────────────────────────────────────────────

    struct FileEntry {
        public int    id;
        public string name;
        public string dir;
    }
}
}
