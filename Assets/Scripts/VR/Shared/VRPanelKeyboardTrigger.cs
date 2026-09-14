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

    void Update() {
        currentInputField = GetComponent<InputField>();
        if (keyboard == null) {
            keyboard = FindObjectOfType<KeyboardUI>(true);
            if (keyboard == null) return;
        }

        bool tappedHere = UnityMolMain.inVR() &&
                          EventSystem.current.currentSelectedGameObject == gameObject &&
                          currentInputField.isFocused;

        if (tappedHere) {
            if (!captured) {
                var kt = keyboard.transform;
                originalParent   = kt.parent;
                originalLocalPos = kt.localPosition;
                originalLocalRot = kt.localRotation;
                captured = true;
            }
            if (!keyboard.gameObject.activeInHierarchy) keyboard.gameObject.SetActive(true);
            keyboard.inpF = currentInputField;
            MoveNextToPanel();
            currentOwner = this;
        } else if (currentOwner == this && keyboard.inpF != currentInputField) {
            // El teclado ha pasado a otro campo (nuestro o nativo) - soltamos la
            // reclamación y lo devolvemos a su sitio original.
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
