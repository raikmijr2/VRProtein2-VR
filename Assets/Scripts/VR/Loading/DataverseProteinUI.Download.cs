using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UMol.API;

namespace UMol {

public partial class DataverseProteinUI {

    public void OnDownloadClicked() {
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
}
}
