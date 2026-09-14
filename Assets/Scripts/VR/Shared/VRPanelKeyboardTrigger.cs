using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace UMol {

/// <summary>
/// Como el activateKeyboard.cs de UnityMol, pero además mueve el teclado VR
/// compartido (KeyboardUI) junto al panel que se está usando, en vez de
/// dejarlo siempre en su sitio fijo junto al menú nativo de UnityMol.
/// Al perder el foco este campo, el teclado vuelve a su posición original
/// para que siga apareciendo bien colocado si luego se usa un campo propio
/// de UnityMol.
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

    InputField currentInputField;
    bool weMovedIt = false;

    void Update() {
        currentInputField = GetComponent<InputField>();
        if (keyboard == null) {
            keyboard = FindObjectOfType<KeyboardUI>(true);
            if (keyboard == null) return;
        }

        bool isFocusedHere = UnityMolMain.inVR() &&
                             EventSystem.current.currentSelectedGameObject == gameObject &&
                             currentInputField.isFocused;

        if (isFocusedHere) {
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
            weMovedIt = true;
        } else if (weMovedIt) {
            RestoreKeyboard();
            weMovedIt = false;
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
