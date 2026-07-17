/*
    ================================================================================
    Copyright Centre National de la Recherche Scientifique (CNRS)
        Contributors and copyright holders :

        Xavier Martinez, 2017-2022
        Hubert Santuz, 2022-2026
        Marc Baaden, 2010-2026
        unitymol@gmail.com
        https://unity.mol3d.tech/

        This file is part of UnityMol, a general framework whose purpose is to provide
        a prototype for developing molecular graphics and scientific
        visualisation applications based on the Unity3D game engine.
        More details about UnityMol are provided at the following URL: https://unity.mol3d.tech/

        This program is free software: you can redistribute it and/or modify
        it under the terms of the GNU General Public License as published by
        the Free Software Foundation, either version 3 of the License, or
        (at your option) any later version.

        This program is distributed in the hope that it will be useful,
        but WITHOUT ANY WARRANTY; without even the implied warranty of
        MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
        GNU General Public License for more details.

        You should have received a copy of the GNU General Public License
        along with this program. If not, see <https://www.gnu.org/licenses/>.

        To help us with UnityMol development, we ask that you cite
        the research papers listed at https://unity.mol3d.tech/cite-us/.
    ================================================================================
*/
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Text;

using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.Utility;
using HTC.UnityPlugin.Pointer3D;

namespace UMol {
[RequireComponent(typeof(ViveRoleSetter))]
[RequireComponent(typeof(PointerAtomSelection))]
public class PointerHoverAtom : MonoBehaviour {

    GameObject trajExtraGo;
    GameObject haloGo;
    TextMesh textm;
    Transform camTransform;

    bool pressed = false;
    CustomRaycastBurst raycaster;

    float hoverScaleMultiplier = 1.0f;

    public bool pauseHovering = false;
    public UnityMolAtom lastPointedAtom = null;

    ViveRaycaster pointerCaster;  // mismo transform que el rayo visual

    ViveRoleProperty curRole;

    PointerAtomSelection pas;
    GameObject goAtom;

    // HUD elements
    GameObject hudCanvasGO;
    Text hudText;


    void OnEnable() {
        curRole = GetComponent<ViveRoleSetter>().viveRole;

        pas = GetComponent<PointerAtomSelection>();
        if (curRole != null) {
            HandRole h = (HandRole)curRole.roleValue;
            ViveInput.AddPressDown(h, ControllerButton.PadTouch,  buttonPressed);
            ViveInput.AddPressUp  (h, ControllerButton.PadTouch,  buttonReleased);
            ViveInput.AddPressDown(h, ControllerButton.AKeyTouch, buttonPressed);
            ViveInput.AddPressUp  (h, ControllerButton.AKeyTouch, buttonReleased);
        }
    }
    void OnDisable() {
        if (curRole != null) {
            HandRole h = (HandRole)curRole.roleValue;
            ViveInput.RemovePressDown(h, ControllerButton.PadTouch,  buttonPressed);
            ViveInput.RemovePressUp  (h, ControllerButton.PadTouch,  buttonReleased);
            ViveInput.RemovePressDown(h, ControllerButton.AKeyTouch, buttonPressed);
            ViveInput.RemovePressUp  (h, ControllerButton.AKeyTouch, buttonReleased);
        }
    }

    void Start() {
        raycaster = UnityMolMain.getCustomRaycast();

        haloGo = GameObject.Instantiate((GameObject) Resources.Load("Prefabs/SphereOverAtom"));
        textm = haloGo.GetComponentsInChildren<TextMesh>()[0];
        // Disable the sphere's built-in TextMesh — info is shown in HUD instead
        textm.gameObject.SetActive(false);

        haloGo.SetActive(false);

        trajExtraGo = new GameObject("DummyTrajExtractedGo");
        goAtom = new GameObject("HoverAtomGo");
        DontDestroyOnLoad(trajExtraGo);
        DontDestroyOnLoad(haloGo);
        DontDestroyOnLoad(goAtom);

        BuildHUD();
    }

    void BuildHUD() {
        if (hudCanvasGO != null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        hudCanvasGO = new GameObject("AtomHoverHUD_" + gameObject.name);
        hudCanvasGO.transform.SetParent(cam.transform, false);
        hudCanvasGO.transform.localPosition = new Vector3(0f, 0.25f, 0.8f);
        hudCanvasGO.transform.localRotation = Quaternion.identity;
        hudCanvasGO.transform.localScale    = Vector3.one * 0.001f;

        Canvas canvas = hudCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRT = hudCanvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(600f, 90f);

        // Semi-transparent background
        GameObject bgGO = new GameObject("BG");
        bgGO.transform.SetParent(hudCanvasGO.transform, false);
        Image bg = bgGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        // Text label
        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(hudCanvasGO.transform, false);
        hudText = textGO.AddComponent<Text>();
        hudText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        hudText.fontSize     = 20;
        hudText.color        = Color.white;
        hudText.alignment    = TextAnchor.MiddleCenter;
        hudText.supportRichText = true;
        RectTransform textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10f, 4f);
        textRT.offsetMax = new Vector2(-10f, -4f);

        hudCanvasGO.SetActive(false);
    }

    void Update() {
        if (pauseHovering) {
            disableHovering();
            return;
        }
        if (pressed && !pas.isOverUI) {
            showHover();
        }
    }

    public static string formatAtomText(UnityMolAtom a) {
        string nameS = a.residue.chain.model.structure.FormatName(25);
        return nameS + "   |   " +
               a.residue.chain.name + " / " +
               a.residue.name + a.residue.id + " / " +
               "<b>" + a.name + "</b>";
    }

    // Devuelve la posición y dirección del rayo coincidiendo con el rayo visual.
    public void GetPointerRay(out Vector3 origin, out Vector3 dir) {
        FindPointerCasterIfNeeded();
        if (pointerCaster != null) {
            origin = pointerCaster.transform.position;
            dir    = pointerCaster.transform.forward;
        } else {
            RigidPose cp = VivePose.GetPose(curRole);
            origin = cp.pos;
            dir    = cp.forward;
        }
    }

    void FindPointerCasterIfNeeded() {
        if (pointerCaster != null) return;
        foreach (var vr in FindObjectsByType<ViveRaycaster>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            if (vr.viveRole.roleType == curRole.roleType &&
                vr.viveRole.roleValue == curRole.roleValue) {
                pointerCaster = vr;
                return;
            }
        }
    }

    void showHover() {
        // Lazy-init HUD if camera wasn't ready at Start
        if (hudCanvasGO == null) BuildHUD();
        if (hudCanvasGO == null) return;

        FindPointerCasterIfNeeded();

        Vector3 rayOrigin, rayDir;
        GetPointerRay(out rayOrigin, out rayDir);

        Vector3 p = Vector3.zero;
        bool isExtrAtom = false;
        UnityMolAtom a = raycaster.customRaycastAtomBurst(
                             rayOrigin,
                             rayDir,
                             ref p, ref isExtrAtom, true);
        if (a != null) {
            // Recreate sphere if needed
            if (haloGo == null) {
                haloGo = GameObject.Instantiate((GameObject) Resources.Load("Prefabs/SphereOverAtom"));
                var tm = haloGo.GetComponentsInChildren<TextMesh>()[0];
                tm.gameObject.SetActive(false);
                haloGo.SetActive(false);
                DontDestroyOnLoad(haloGo);
            }

            haloGo.SetActive(true);
            haloGo.transform.position = p;
            trajExtraGo.transform.position = p;

            if (camTransform == null)
                camTransform = Camera.main.transform;

            Vector3 lookDir = haloGo.transform.position - camTransform.position;
            if (lookDir.sqrMagnitude > 1e-6f)
                haloGo.transform.rotation = Quaternion.LookRotation(lookDir);

            UnityMolMain.getAnnotationManager().setGOPos(a, goAtom);

            if (!isExtrAtom)
                haloGo.transform.SetParent(goAtom.transform);
            else {
                trajExtraGo.transform.SetParent(goAtom.transform.parent);
                trajExtraGo.transform.localScale = goAtom.transform.localScale;
                haloGo.transform.SetParent(trajExtraGo.transform);
            }

            haloGo.transform.localScale = hoverScaleMultiplier * a.radius * Vector3.one * 1.1f;

            // Update HUD
            hudText.text = formatAtomText(a);
            hudCanvasGO.SetActive(true);

            lastPointedAtom = a;
        } else {
            lastPointedAtom = null;
            hudCanvasGO.SetActive(false);
            if (haloGo != null) haloGo.SetActive(false);
        }
    }

    void disableHovering() {
        if (haloGo != null) {
            haloGo.SetActive(false);
            haloGo.transform.parent = null;
        }
        if (hudCanvasGO != null)
            hudCanvasGO.SetActive(false);
    }

    void buttonReleased() {
        disableHovering();
        pressed = false;
        lastPointedAtom = null;
    }
    void buttonPressed() {
        pressed = true;
    }

    public static string ReplaceFirstOccurrance(string original, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(original)) return "";
        if (string.IsNullOrEmpty(oldValue))  return original;
        int loc = original.IndexOf(oldValue);
        return original.Remove(loc, oldValue.Length).Insert(loc, newValue);
    }
}
}
