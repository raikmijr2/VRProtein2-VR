using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

namespace UMol {

/// <summary>
/// Añadir a un botón para que su acción se repita mientras se mantiene pulsado.
/// Usa IPointerDownHandler/UpHandler para compatibilidad con VIU.
/// </summary>
public class HoldButtonHelper : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {

    public System.Action onHold;
    bool _active = false;

    public void OnPointerDown(PointerEventData e) {
        _active = true;
        StartCoroutine(HoldRoutine());
    }

    public void OnPointerUp(PointerEventData e)   { _active = false; }
    public void OnPointerExit(PointerEventData e) { _active = false; }

    IEnumerator HoldRoutine() {
        yield return new WaitForSeconds(0.35f);
        while (_active) {
            onHold?.Invoke();
            yield return new WaitForSeconds(0.10f);
        }
    }
}
}
