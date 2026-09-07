using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UMol.API;

namespace UMol {

/// <summary>
/// VR panel for loading PRMTOP + DCD trajectories and controlling playback.
/// Drag onto a GameObject in the scene. Paths can be set in Inspector or
/// via ADB push to Application.persistentDataPath on Quest 3.
/// </summary>
public class TrajAnimationUI : MonoBehaviour {

    [Header("Default paths (editable in VR via keyboard or set here)")]
    public string defaultPrmtopPath = "";
    public string defaultDCDFolder  = "";

    [Header("Spawn position in world space")]
    public Vector3 spawnPosition = new Vector3(0f, 1.2f, 1.4f);

    // ── Runtime state ─────────────────────────────────────────────────────────
    UnityMolStructure loadedStruct;
    bool isPlaying = false;
    float framesPerSecond = 5f;
    float timer = 0f;
    bool looping = true;

    // ── UI references ─────────────────────────────────────────────────────────
    InputField prmtopInput;
    InputField dcdFolderInput;
    Text statusText;
    Text frameLabel;
    Slider frameSlider;
    Button playBtn;
    Text  playBtnLabel;

    // ── Colors ────────────────────────────────────────────────────────────────
    static readonly Color bgColor  = new Color(0.06f, 0.08f, 0.14f, 0.95f);
    static readonly Color btnColor = new Color(0.15f, 0.30f, 0.65f, 1f);
    static readonly Color btnGreen = new Color(0.10f, 0.55f, 0.25f, 1f);
    static readonly Color btnRed   = new Color(0.65f, 0.15f, 0.15f, 1f);

    void Start() => BuildPanel();

    void Update() {
        if (!isPlaying || loadedStruct == null) return;
        timer += Time.deltaTime;
        if (timer >= 1f / framesPerSecond) {
            timer = 0f;
            StepFrame(forward: true);
        }
    }

    // ── Panel builder ─────────────────────────────────────────────────────────

    void BuildPanel() {
        float W = 500f, H = 440f, pad = 10f;

        GameObject canvasGO = VRUIFactory.CreateWorldSpaceCanvas("TrajAnimPanel", spawnPosition, new Vector2(W, H));

        VRUIFactory.CreateBackgroundImage(canvasGO.transform, bgColor);

        float y = H / 2f - pad;

        // Title
        y = AddLabel(canvasGO.transform, "TRAYECTORIA PRMTOP + DCD", W, 32, FontStyle.Bold, ref y, pad);

        // PRMTOP path row
        y = AddLabel(canvasGO.transform, "Archivo PRMTOP:", W, 18, FontStyle.Normal, ref y, pad);
        prmtopInput = VRUIFactory.CreateInputField(canvasGO.transform, W - pad * 2, 34, new Vector2(0, y - 17f),
            defaultPrmtopPath, "ruta al archivo...", new Color(0.15f, 0.15f, 0.20f, 1f), null, 14,
            InputField.ContentType.Standard, TextAnchor.MiddleLeft);
        y -= 34 + pad;

        // DCD folder row
        y = AddLabel(canvasGO.transform, "Carpeta DCD:", W, 18, FontStyle.Normal, ref y, pad);
        dcdFolderInput = VRUIFactory.CreateInputField(canvasGO.transform, W - pad * 2, 34, new Vector2(0, y - 17f),
            defaultDCDFolder, "ruta al archivo...", new Color(0.15f, 0.15f, 0.20f, 1f), null, 14,
            InputField.ContentType.Standard, TextAnchor.MiddleLeft);
        y -= 34 + pad;

        // Load button
        var loadBtn = VRUIFactory.CreateButton(canvasGO.transform, "CARGAR", new Vector2(W - pad * 2, 44), new Vector2(0, y - 22), btnGreen, fontSize: 18);
        loadBtn.onClick.AddListener(OnLoadClicked);
        y -= 44 + pad;

        // Status
        statusText = VRUIFactory.CreateCenteredLabel(canvasGO.transform, "Listo.", W - pad * 2, 22, 16, new Vector2(0, y - 11), alignment: TextAnchor.MiddleLeft);
        y -= 22 + pad;

        // Separator
        y -= 6;

        // Frame slider row
        frameSlider = AddSlider(canvasGO.transform, W - pad * 2, 30, new Vector2(0, y - 15));
        frameSlider.onValueChanged.AddListener(OnSliderChanged);
        y -= 30 + 4;

        frameLabel = VRUIFactory.CreateCenteredLabel(canvasGO.transform, "-- / --", W, 18, 16, new Vector2(0, y - 9), alignment: TextAnchor.MiddleLeft);
        y -= 18 + pad;

        // Playback buttons row
        float bW = (W - pad * 4) / 3f;
        float bX = -(W / 2f) + pad + bW / 2f;

        var prevBtn = VRUIFactory.CreateButton(canvasGO.transform, "◀ Prev", new Vector2(bW, 42), new Vector2(bX, y - 21), btnColor, fontSize: 18);
        prevBtn.onClick.AddListener(() => StepFrame(forward: false));
        bX += bW + pad;

        playBtn = VRUIFactory.CreateButton(canvasGO.transform, "▶ Play", new Vector2(bW, 42), new Vector2(bX, y - 21), btnGreen, fontSize: 18);
        playBtnLabel = playBtn.GetComponentInChildren<Text>();
        playBtn.onClick.AddListener(OnPlayPause);
        bX += bW + pad;

        var nextBtn = VRUIFactory.CreateButton(canvasGO.transform, "Next ▶", new Vector2(bW, 42), new Vector2(bX, y - 21), btnColor, fontSize: 18);
        nextBtn.onClick.AddListener(() => StepFrame(forward: true));
        y -= 42 + pad;

        // Speed row
        y = AddLabel(canvasGO.transform, "Velocidad (fps):", W, 16, FontStyle.Normal, ref y, pad);
        var speedSlider = AddSlider(canvasGO.transform, W - pad * 2, 28, new Vector2(0, y - 14));
        speedSlider.minValue = 1f; speedSlider.maxValue = 30f; speedSlider.value = framesPerSecond;
        speedSlider.onValueChanged.AddListener(v => framesPerSecond = v);
        y -= 28 + pad;
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    void OnLoadClicked() {
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

    void OnPlayPause() {
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

    void OnSliderChanged(float val) {
        if (loadedStruct == null) return;
        int frame = Mathf.RoundToInt(val);
        APIPython.setModel(loadedStruct.name, frame);
        UpdateFrameLabel(frame);
    }

    void StepFrame(bool forward) {
        if (loadedStruct == null || loadedStruct.modelFrames == null) return;
        loadedStruct.modelNext(forward, looping);
        UpdateFrameUI();
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

    // ── UI helpers ────────────────────────────────────────────────────────────

    /// <summary>Places a left-aligned label at the current top-down cursor y and advances it.</summary>
    float AddLabel(Transform parent, string text, float w, int fontSize, FontStyle style, ref float y, float pad) {
        float h = fontSize + 6;
        VRUIFactory.CreateCenteredLabel(parent, text, w, h, fontSize, new Vector2(0, y - h / 2f), style, alignment: TextAnchor.MiddleLeft);
        y -= h + pad * 0.5f;
        return y;
    }

    Slider AddSlider(Transform parent, float w, float h, Vector2 pos) {
        var go = new GameObject("Slider"); go.transform.SetParent(parent, false);
        var slider = go.AddComponent<Slider>();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = pos;

        // Background track
        var bg = new GameObject("BG"); bg.transform.SetParent(go.transform, false);
        var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        slider.targetGraphic = bgImg;

        // Fill area
        var fillArea = new GameObject("FillArea"); fillArea.transform.SetParent(go.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>() ?? fillArea.AddComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0, 0.25f); faRt.anchorMax = new Vector2(1, 0.75f);
        faRt.offsetMin = faRt.offsetMax = Vector2.zero;

        var fill = new GameObject("Fill"); fill.transform.SetParent(fillArea.transform, false);
        var fillImg = fill.AddComponent<Image>(); fillImg.color = new Color(0.25f, 0.50f, 0.90f, 1f);
        slider.fillRect = fill.GetComponent<RectTransform>();

        // Handle
        var handleArea = new GameObject("HandleArea"); handleArea.transform.SetParent(go.transform, false);
        var haRt = handleArea.GetComponent<RectTransform>() ?? handleArea.AddComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
        haRt.offsetMin = haRt.offsetMax = Vector2.zero;

        var handle = new GameObject("Handle"); handle.transform.SetParent(handleArea.transform, false);
        var handleImg = handle.AddComponent<Image>(); handleImg.color = Color.white;
        var hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(h * 1.2f, h * 1.2f);
        slider.handleRect = hRt;

        slider.minValue = 0; slider.maxValue = 1; slider.value = 0;
        return slider;
    }
}
}
