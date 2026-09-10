using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UMol.API;

namespace UMol {

/// <summary>
/// VR panel for loading PRMTOP + DCD trajectories and controlling playback.
/// The UI hierarchy is an Editor-built prefab (see TrajAnimPanel.prefab);
/// this script only holds the business logic and references to its parts.
/// </summary>
public class TrajAnimationUI : MonoBehaviour {

    [Header("Default paths (editable in VR via keyboard or set here)")]
    public string defaultPrmtopPath = "";
    public string defaultDCDFolder  = "";

    // ── Runtime state ─────────────────────────────────────────────────────────
    UnityMolStructure loadedStruct;
    bool isPlaying = false;
    float framesPerSecond = 5f;
    float timer = 0f;
    bool looping = true;

    // ── UI references ─────────────────────────────────────────────────────────
    [SerializeField] InputField prmtopInput;
    [SerializeField] InputField dcdFolderInput;
    [SerializeField] Text statusText;
    [SerializeField] Text frameLabel;
    [SerializeField] Slider frameSlider;
    [SerializeField] Button playBtn;
    [SerializeField] Text   playBtnLabel;

    void Start() {
        if (prmtopInput)    prmtopInput.text    = defaultPrmtopPath;
        if (dcdFolderInput) dcdFolderInput.text = defaultDCDFolder;
    }

    void Update() {
        if (!isPlaying || loadedStruct == null) return;
        timer += Time.deltaTime;
        if (timer >= 1f / framesPerSecond) {
            timer = 0f;
            StepFrame(forward: true);
        }
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    public void OnLoadClicked() {
        string prmtop = prmtopInput.text.Trim();
        string dcdDir = dcdFolderInput.text.Trim();

        if (string.IsNullOrEmpty(prmtop) || string.IsNullOrEmpty(dcdDir)) {
            SetStatus("Introduce las rutas antes de cargar.", Color.yellow);
            return;
        }

        // Resolve relative paths from persistentDataPath
        if (!Path.IsPathRooted(prmtop))
            prmtop = Path.Combine(Application.persistentDataPath, prmtop);
        if (!Path.IsPathRooted(dcdDir))
            dcdDir = Path.Combine(Application.persistentDataPath, dcdDir);

        SetStatus("Cargando DCD...", Color.white);
        StartCoroutine(LoadRoutine(prmtop, dcdDir));
    }

    IEnumerator LoadRoutine(string prmtop, string dcdDir) {
        yield return null; // let UI update

        // Read DCD frames first to get natom + initial positions
        int natom = 0;
        List<Vector3[]> frames = null;
        try {
            frames = DCDReader.ReadFolder(dcdDir, out natom);
        } catch (System.Exception e) {
            SetStatus("Error DCD: " + e.Message, Color.red);
            yield break;
        }

        if (frames == null || frames.Count == 0) {
            SetStatus("No se encontraron frames DCD.", Color.red);
            yield break;
        }

        SetStatus($"DCD: {frames.Count} frames, {natom} átomos. Cargando PRMTOP...", Color.white);
        yield return null;

        // Load structure from PRMTOP using first frame as initial positions
        UnityMolStructure s = null;
        try {
            s = PRMTOPReader.Load(prmtop, frames[0]);
        } catch (System.Exception e) {
            SetStatus("Error PRMTOP: " + e.Message, Color.red);
            yield break;
        }

        if (s == null) {
            SetStatus("No se pudo cargar el PRMTOP.", Color.red);
            yield break;
        }

        // Attach trajectory frames
        s.modelFrames    = frames;
        s.trajectoryMode = true;
        s.createModelPlayer();
        APIPython.setModel(s.name, 0);

        loadedStruct = s;
        SetStatus($"Cargado: {s.name} | {frames.Count} frames | {natom} átomos", Color.green);

        // Default representation (showSelection only affects this structure, not others)
        APIPython.showSelection(s.ToSelectionName(), "hb");

        UpdateFrameUI();
    }

    public void OnPlayPause() {
        isPlaying = !isPlaying;
        timer = 0f;
        if (playBtnLabel != null)
            playBtnLabel.text = isPlaying ? "⏸ Pausa" : "▶ Play";
    }

    public void Pause() {
        if (!isPlaying) return;
        isPlaying = false;
        timer = 0f;
        if (playBtnLabel != null)
            playBtnLabel.text = "▶ Play";
    }

    public void OnSliderChanged(float val) {
        if (loadedStruct == null) return;
        int frame = Mathf.RoundToInt(val);
        APIPython.setModel(loadedStruct.name, frame);
        UpdateFrameLabel(frame);
    }

    public void StepFrame(bool forward) {
        if (loadedStruct == null || loadedStruct.modelFrames == null) return;
        loadedStruct.modelNext(forward, looping);
        UpdateFrameUI();
    }

    public void SetSpeed(float v) {
        framesPerSecond = v;
    }

    // ── Frame UI ──────────────────────────────────────────────────────────────

    void UpdateFrameUI() {
        if (loadedStruct?.modelFrames == null) return;
        int cur   = loadedStruct.currentFrameId;
        int total = loadedStruct.modelFrames.Count;
        UpdateFrameLabel(cur);
        if (frameSlider != null) {
            frameSlider.minValue = 0;
            frameSlider.maxValue = total - 1;
            frameSlider.SetValueWithoutNotify(cur);
        }
    }

    void UpdateFrameLabel(int cur) {
        if (frameLabel == null || loadedStruct?.modelFrames == null) return;
        frameLabel.text = $"Frame {cur + 1} / {loadedStruct.modelFrames.Count}";
    }

    void SetStatus(string msg, Color col) {
        if (statusText == null) return;
        statusText.text  = msg;
        statusText.color = col;
        Debug.Log("[TrajAnim] " + msg);
    }
}
}
