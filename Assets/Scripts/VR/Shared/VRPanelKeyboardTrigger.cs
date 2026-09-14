using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UMol {

/// <summary>
/// Como el activateKeyboard.cs de UnityMol, pero además mueve el teclado VR
/// compartido (KeyboardUI) junto al panel que se está usando, en vez de
/// dejarlo siempre en su sitio fijo junto al menú nativo de UnityMol.
///
/// No se puede usar "¿sigue teniendo el foco este campo?" para decidir cuándo
/// devolver el teclado a su sitio: al pulsar una tecla, Unity mueve la
/// selección del EventSystem al propio botón del teclado (KeyboardUI.sendKey
/// ni reasigna el foco ni cambia inpF), así que esa comprobación se volvía
/// falsa en cada pulsación. En su lugar, cada instancia solo se considera
/// "dueña" del teclado mientras keyboard.inpF siga apuntando a su propio
/// campo — esa referencia solo cambia cuando otro campo (nuestro o nativo de
/// UnityMol) reclama el teclado de verdad.
/// </summary>
[RequireComponent(typeof(InputField))]
public class VRPanelKeyboardTrigger : MonoBehaviour {

    [Tooltip("Distancia en metros a la derecha del panel donde aparece el teclado")]
    public float sideOffset = 0.55f;

    static KeyboardUI keyboard;
    static Transform   originalParent;
    static Vector3     originalLocalPos;
    static Quaternion  originalLocalRot;
    static bool captured = false;
    static VRPanelKeyboardTrigger currentOwner;

    InputField currentInputField;
    bool lastTappedHere = false;
    float nextHeartbeat = 0f;

    void Update() {
        currentInputField = GetComponent<InputField>();
        if (keyboard == null) {
            keyboard = FindObjectOfType<KeyboardUI>(true);
            if (keyboard == null) return;
        }

        if (currentOwner == this && Time.unscaledTime >= nextHeartbeat) {
            nextHeartbeat = Time.unscaledTime + 0.5f;
            var kt = keyboard.transform;
            Debug.Log("[DIAG-kb] heartbeat parent=" + (kt.parent != null ? kt.parent.name : "null") +
                       " pos=" + kt.position + " active=" + keyboard.gameObject.activeInHierarchy +
                       " inpF=" + (keyboard.inpF != null ? keyboard.inpF.gameObject.name : "null"));
        }

        bool tappedHere = UnityMolMain.inVR() &&
                          EventSystem.current.currentSelectedGameObject == gameObject &&
                          currentInputField.isFocused;

        if (tappedHere != lastTappedHere) {
            var sel = EventSystem.current.currentSelectedGameObject;
            Debug.Log("[DIAG-kb] " + gameObject.name + " tappedHere " + lastTappedHere + " -> " + tappedHere +
                       " | selected=" + (sel != null ? sel.name : "null") +
                       " | isFocused=" + currentInputField.isFocused +
                       " | keyboard.inpF=" + (keyboard.inpF != null ? keyboard.inpF.gameObject.name : "null") +
                       " | currentOwner=" + (currentOwner != null ? currentOwner.gameObject.name : "null"));
            lastTappedHere = tappedHere;
        }

        if (tappedHere) {
            if (!captured) {
                var kt = keyboard.transform;
                originalParent   = kt.parent;
                originalLocalPos = kt.localPosition;
                originalLocalRot = kt.localRotation;
                captured = true;
                Debug.Log("[DIAG-kb] captured original parent=" + (originalParent != null ? originalParent.name : "null"));
            }
            if (!keyboard.gameObject.activeInHierarchy) keyboard.gameObject.SetActive(true);
            keyboard.inpF = currentInputField;
            MoveNextToPanel();
            currentOwner = this;
        } else if (currentOwner == this && keyboard.inpF != currentInputField) {
            // El teclado ha pasado a otro campo (nuestro o nativo) - soltamos la
            // reclamación y lo devolvemos a su sitio original.
            Debug.Log("[DIAG-kb] RESTORE fired on " + gameObject.name + " because keyboard.inpF=" +
                       (keyboard.inpF != null ? keyboard.inpF.gameObject.name : "null") + " != " + gameObject.name);
            RestoreKeyboard();
            currentOwner = null;
        }
    }

    void MoveNextToPanel() {
        var panelCanvas = GetComponentInParent<Canvas>();
        if (panelCanvas == null) return;
        Transform panelT = panelCanvas.transform;
        Transform kt = keyboard.transform;
        kt.SetParent(panelT, worldPositionStays: true);
        kt.rotation = panelT.rotation;
        kt.position = panelT.position + panelT.right * sideOffset;
    }

    static void RestoreKeyboard() {
        if (keyboard == null || !captured) return;
        var kt = keyboard.transform;
        kt.SetParent(originalParent, false);
        kt.localPosition = originalLocalPos;
        kt.localRotation = originalLocalRot;
    }
}
}
