using UnityEngine;
using System.Collections;

namespace UMol {

/// <summary>
/// Menu-button long-press ("bring the main UI closer") vs. short-press
/// ("toggle all canvases") feature. Unrelated to grab/scale - it only shares
/// the Menu button plumbing wired up in the core file's OnEnable/OnDisable.
/// </summary>
public partial class ControllerGrabAndScale {

    // Canvas toggle state (static: shared between both controllers)
    static bool canvasesHidden = false;
    static float lastCanvasToggleTime = -1f;

    private float menustartPressedTime;
    private float minLongPress = 0.45f;//in s

    void menuPressed() {
        menustartPressedTime = Time.realtimeSinceStartup;
    }

    void menuReleased() {
        float diffTime = Time.realtimeSinceStartup - menustartPressedTime;

        if (diffTime > minLongPress) {
            StartCoroutine(bringMenuCloser());
        } else {
            // Short press: toggle all canvases (debounced so ambos mandos no disparen dos veces)
            if (Time.realtimeSinceStartup - lastCanvasToggleTime > 0.2f) {
                lastCanvasToggleTime = Time.realtimeSinceStartup;
                ToggleAllCanvases();
            }
        }
    }

    void ToggleAllCanvases() {
        canvasesHidden = !canvasesHidden;
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        foreach (var c in canvases) {
            // Solo los canvases raíz (sin Canvas en el padre)
            if (c.transform.parent == null || c.transform.parent.GetComponentInParent<Canvas>() == null)
                c.gameObject.SetActive(!canvasesHidden);
        }
    }

    public IEnumerator bringMenuCloser() {
        GameObject mainUIGo = GameObject.Find("CanvasMainUIVR");
        Transform head = Camera.main.transform;

        if (mainUIGo != null && head != null) {
            Vector3 targetPos = head.position + head.forward;
            Vector3 targetRot = head.rotation.eulerAngles;
            int steps = 400;
            for (int i = 1; i < steps / 4; i++) {
                float tt = i / (float)steps;
                mainUIGo.transform.position = Vector3.Lerp(mainUIGo.transform.position, targetPos, tt);

                Vector3 newRot = new Vector3(Mathf.LerpAngle(mainUIGo.transform.eulerAngles.x, targetRot.x, tt),
                                             Mathf.LerpAngle(mainUIGo.transform.eulerAngles.y, targetRot.y, tt),
                                             Mathf.LerpAngle(mainUIGo.transform.eulerAngles.z, targetRot.z, tt));
                mainUIGo.transform.eulerAngles = newRot;
                yield return 0;
            }
        }
    }
}
}
