using UnityEngine;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {

/// Activa los EventRaycaster del sistema de puntero VIU al arrancar.
/// En Quest 3 con OpenXR el VivePoseTracker puede no disparar el evento de
/// validez a tiempo, dejando los botones del panel sin respuesta.
public class VRPointerInit : MonoBehaviour {

    void Start() {
        // Busca todos los Pointer3DRaycaster aunque estén en GameObjects inactivos
        var raycasters = FindObjectsByType<Pointer3DRaycaster>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var r in raycasters) {
            if (!r.gameObject.activeInHierarchy) {
                r.gameObject.SetActive(true);
            }
        }

        if (raycasters.Length == 0) {
        }
    }
}
}
