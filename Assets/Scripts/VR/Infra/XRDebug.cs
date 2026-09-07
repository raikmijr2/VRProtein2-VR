using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.Collections;

/// Añadir a cualquier GameObject en la escena.
/// Comprueba el estado XR en Start y cada segundo durante 10s.
public class XRDebug : MonoBehaviour {

    int prevDisplayCount = -1;

    void Start() {
        LogXRState("START");
        StartCoroutine(CheckPeriodically());
    }

    IEnumerator CheckPeriodically() {
        for (int i = 1; i <= 10; i++) {
            yield return new WaitForSeconds(1f);
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            if (displays.Count != prevDisplayCount) {
                prevDisplayCount = displays.Count;
                LogXRState($"t+{i}s");
            }
        }
    }

    void LogXRState(string when) { }
}
