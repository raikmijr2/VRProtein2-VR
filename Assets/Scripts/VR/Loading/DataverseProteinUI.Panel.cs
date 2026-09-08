using UnityEngine;
using UnityEngine.UI;

namespace UMol {

public partial class DataverseProteinUI {

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
}
}
