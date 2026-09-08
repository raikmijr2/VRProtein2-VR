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
using System.Collections.Generic;
using HTC.UnityPlugin.Vive;
using HTC.UnityPlugin.Utility;
using HTC.UnityPlugin.Pointer3D;

/// Grab a molecule (trigger button) or a group of molecules (grip button)
/// Scale using both controllers
/// Split across 2 files: this one (grab/scale/rotate) and .MenuToggle.cs
/// (the unrelated menu-bring-closer / toggle-all-canvases feature bolted
/// onto the same Menu button).

namespace UMol {
[RequireComponent(typeof(ViveRoleSetter))]
public partial class ControllerGrabAndScale : MonoBehaviour {

    public ControllerGrabAndScale otherController;

    [Tooltip("Velocidad a la que el joystick acerca/aleja la molécula agarrada")]
    public float molDepthScrollSpeed = 2.0f;

    [Tooltip("Velocidad de rotación de la selección con el joystick (grados/seg)")]
    public float selectionRotSpeed = 90f;

    public Transform grabbedMolecule = null;
    public Vector3 grabbedCentroid = Vector3.zero;
    public List<Transform> grabbedGroupMolecules = new List<Transform>();
    public bool isGroupGrabbed = false;
    public int grabbedGroupId = -1;
    public bool isScaling = false;
    public bool isScalingGroup = false;
    public bool isClicking = false;
    Transform savedParent = null;
    private bool didScale = false;

    Vector3 initPosScale = Vector3.zero;
    float startMagnitude = 0.0f;
    float sizeScale = 0.1f;
    Vector3 scaleStart = Vector3.one;
    Transform loadedMols;
    LayerMask notUILayer;
    CustomRaycastBurst raycaster;
    UnityMolStructureManager sm;
    PointerMoveUI[] moveUIScripts;
    Pointer3DRaycaster myPointer3D;

    RigidbodyConstraints savedConstraints;

    // Selection grab state
    UnityMolSelectionManager selMgr;
    bool isGrabbingSelection = false;
    UnityMolSelection grabbedSelection = null;
    UnityMolStructure grabbedSelStructure = null;
    Vector3 prevControllerPos;

    public ViveRoleProperty curRole;
    readonly ControllerInputBinder inputBinder = new ControllerInputBinder();

    public bool grabbedUI {
        get {
            if (moveUIScripts != null && moveUIScripts.Length != 0) {
                foreach (var muis in moveUIScripts) {
                    if (muis.grabbedUI)
                        return true;
                }
            }
            return false;
        }
    }



    void Start() {
        sm = UnityMolMain.getStructureManager();
        selMgr = UnityMolMain.getSelectionManager();
        raycaster = UnityMolMain.getCustomRaycast();
        notUILayer = ~ LayerMask.GetMask("UI", "Ignore Raycast");

        moveUIScripts = GameObject.FindObjectsOfType<PointerMoveUI>();
        myPointer3D = GetComponentInChildren<Pointer3DRaycaster>();

        loadedMols = UnityMolMain.getRepresentationParent().transform;

        if (otherController == null) {
            searchOtherController();
        }
    }

    void searchOtherController() {
        GameObject otherGO = curRole.roleValue == (int)HandRole.LeftHand
            ? UnityMolMain.getRightController()
            : UnityMolMain.getLeftController();
        if (otherGO != null) {
            otherController = otherGO.GetComponent<ControllerGrabAndScale>();
        }
    }


    void OnEnable() {

        curRole = GetComponent<ViveRoleSetter>().viveRole;
        // curRole = ViveRoleProperty.New(GetComponent<ViveRoleSetter>().viveRole);

        if (curRole != null) {
            HandRole h = (HandRole)curRole.roleValue;
            inputBinder.Bind(h, ControllerButton.Trigger, triggerClicked, triggerReleased);
            inputBinder.Bind(h, ControllerButton.Grip, gridPressed, triggerReleased);
            inputBinder.Bind(h, ControllerButton.Menu, menuPressed, menuReleased);
        }
    }

    void OnDisable() {
        inputBinder.UnbindAll();
    }

    private void triggerClicked() {

        // Selection grab: if there's an active selection, drag its atoms instead of scaling
        if (selMgr != null && !grabbedUI && !isGroupGrabbed) {
            var sel = selMgr.currentSelection;
            if (sel != null && sel.Count > 0) {
                // Pausar cualquier animación activa para evitar que sobreescriba las posiciones
                foreach (var ui in FindObjectsOfType<TrajAnimationUI>())  ui.Pause();
                foreach (var ui in FindObjectsOfType<AnimationPlayerUI>()) ui.Pause();
                isGrabbingSelection = true;
                isClicking = true;
                grabbedSelection = sel;
                grabbedSelStructure = sel.atoms[0].residue.chain.model.structure;
                prevControllerPos = transform.position;
                return;
            }
        }

        if (isGroupGrabbed) {
            isScaling = false;
            isScalingGroup = false;
            isClicking = false;
            return;
        }

        if (loadedMols == null) {
            loadedMols = UnityMolMain.getRepresentationParent().transform;
        }
        if (loadedMols == null) {
            isScaling = false;
            isScalingGroup = false;
            isClicking = false;
            return;
        }

        isClicking = true;

        if (otherController == null) {
            searchOtherController();
        }
        if (otherController != null) {
            if (otherController.isClicking && //Both controllers are clicking
                    !grabbedUI &&
                    //None grabs anything
                    ((grabbedMolecule == null && otherController.grabbedMolecule == null) ||
                     //I grabbed but the other does not
                     (grabbedMolecule != null && otherController.grabbedMolecule == null) ||
                     //Other grabbed but not me
                     (grabbedMolecule == null && otherController.grabbedMolecule != null) ||
                     //Both grabbed the same
                     (grabbedMolecule == otherController.grabbedMolecule))) {

                isScaling = true;
                isScalingGroup = false;
                initPosScale = otherController.transform.position;
                startMagnitude = sizeScale * (transform.position - initPosScale).magnitude;
                scaleStart = loadedMols.localScale;
            }
        }
    }

    private void gridPressed() {

        // if (isClicking) { //Already being grabbed by the trigger button
        // return;
        // }

        // if (otherController != null && otherController.isClicking
        //         && otherController.isGroupGrabbed) {
        //     otherController.isScaling = false;
        //     return;
        // }

        //Grab a group instead of just a molecule
        isClicking = true;

        if (loadedMols == null) {
            loadedMols = UnityMolMain.getRepresentationParent().transform;
        }

        // No coger molécula si el rayo está sobre un canvas o ya lo está arrastrando
        bool uiPointed = grabbedUI;
        if (!uiPointed && myPointer3D != null) {
            var hit = myPointer3D.FirstRaycastResult();
            if (hit.isValid && hit.gameObject != null)
                uiPointed = hit.gameObject.layer == LayerMask.NameToLayer("UI");
        }

        RigidPose cpose = VivePose.GetPose(curRole);

        //Send 3 rays in the plane of the controller to select a chain
        Vector3 vecDir30 =  Quaternion.AngleAxis(30, transform.right) * cpose.forward;
        Vector3 vecDir60 =  Quaternion.AngleAxis(60, transform.right) * cpose.forward;

        Vector3 p = Vector3.zero;
        bool isExtrAtom = false;
        UnityMolAtom a = null;

        if (!uiPointed) {
            a = raycaster.customRaycastAtomBurst(cpose.pos, cpose.forward, ref p, ref isExtrAtom, true);
            if (a == null)
                a = raycaster.customRaycastAtomBurst(cpose.pos, vecDir30, ref p, ref isExtrAtom, true);
            if (a == null)
                a = raycaster.customRaycastAtomBurst(cpose.pos, vecDir60, ref p, ref isExtrAtom, true);
            if (a == null)
                a = FindAtomByRendererBounds(cpose.pos, cpose.forward);
        }

        if (a != null) {

            Transform atomPar = sm.structureToGameObject[a.residue.chain.model.structure.name].transform;
            grabGroupMolecule(atomPar);
        }

        if (otherController != null) {
            if (otherController.isGroupGrabbed) {
                isScalingGroup = true;
                initPosScale = otherController.transform.position;
                startMagnitude = sizeScale * (transform.position - initPosScale).magnitude;
                scaleStart = loadedMols.localScale;
            }
        }
    }

    private void triggerReleased() {
        ungrabMolecule();
    }

    // Intersecta el rayo con los bounds de la molécula.
    // Primero intenta con los renderers activos/inactivos; si no hay (GPU instancing),
    // calcula los bounds desde las posiciones de los átomos en espacio mundo.
    UnityMolAtom FindAtomByRendererBounds(Vector3 origin, Vector3 direction) {
        Ray ray = new Ray(origin, direction);
        float minDist = float.MaxValue;
        UnityMolAtom result = null;
        foreach (UnityMolStructure s in sm.loadedStructures) {
            if (!sm.structureToGameObject.ContainsKey(s.name)) continue;
            if (s.currentModel == null || s.currentModel.allAtoms.Count == 0) continue;
            Transform molT = sm.structureToGameObject[s.name].transform;
            Bounds molBounds = ComputeMoleculeBounds(s, molT);
            if (molBounds.size == Vector3.zero) continue;
            // Si el mando está dentro de la molécula, agarra directamente
            if (molBounds.Contains(origin))
                return s.currentModel.allAtoms[0];
            float dist;
            if (molBounds.IntersectRay(ray, out dist) && dist < minDist) {
                minDist = dist;
                result = s.currentModel.allAtoms[0];
            }
        }
        return result;
    }

    static bool BoundsValid(Bounds b) {
        return !float.IsNaN(b.center.x) && !float.IsNaN(b.size.x)
            && !float.IsInfinity(b.center.x) && !float.IsInfinity(b.size.x)
            && b.size.sqrMagnitude < 1e18f; // sanity cap: > 1e9 units = corrupt
    }

    Bounds ComputeMoleculeBounds(UnityMolStructure s, Transform molT) {
        // Intenta con renderers primero (rápido); salta los con transform corrupto
        Renderer[] rends = molT.GetComponentsInChildren<Renderer>(true);
        Bounds? accum = null;
        foreach (var r in rends) {
            Bounds b;
            try { b = r.bounds; } catch { continue; }
            if (!BoundsValid(b)) continue;
            if (accum == null) { accum = b; }
            else { var tmp = accum.Value; tmp.Encapsulate(b); accum = tmp; }
        }
        if (accum.HasValue) return accum.Value;
        // Fallback: bounds desde posiciones de átomos en espacio mundo
        var atoms = s.currentModel.allAtoms;
        Vector3 wp0 = molT.TransformPoint(atoms[0].position);
        Vector3 bMin = wp0, bMax = wp0;
        for (int i = 1; i < atoms.Count; i++) {
            Vector3 wp = molT.TransformPoint(atoms[i].position);
            bMin = Vector3.Min(bMin, wp);
            bMax = Vector3.Max(bMax, wp);
        }
        return new Bounds((bMin + bMax) * 0.5f, bMax - bMin);
    }


    private void grabMolecule(Transform toGrab) {

        if (UnityMolMain.getAnnotationManager().drawMode) {
            return;
        }

        grabbedMolecule = toGrab;
        savedParent = grabbedMolecule.parent;
        grabbedMolecule.parent = transform;

        DockingManager dm = UnityMolMain.getDockingManager();
        if (dm.isRunning) {
            Rigidbody rb = toGrab.gameObject.GetComponent<Rigidbody>();
            if (rb != null) {
                savedConstraints = rb.constraints;
                rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ;
            }
        }

        // ViveInput.TriggerHapticPulseEx(curRole.roleType, curRole.roleValue, 500);
    }


    public void ungrabMolecule() {

        if (isGrabbingSelection) {
            isGrabbingSelection = false;
            isClicking = false;
            if (grabbedSelStructure != null && grabbedSelStructure.spatialSearch != null)
                grabbedSelStructure.spatialSearch.UpdatePositions(grabbedSelStructure.currentModel.allAtoms);
            grabbedSelection = null;
            grabbedSelStructure = null;
            return;
        }

        DockingManager dm = UnityMolMain.getDockingManager();

        if (grabbedMolecule != null && savedParent != null) {
            grabbedMolecule.parent = savedParent;

            if (dm.isRunning) {
                Rigidbody rb = grabbedMolecule.gameObject.GetComponent<Rigidbody>();
                if (rb != null) {
                    if (savedConstraints != 0) {
                        rb.constraints = savedConstraints;
                    }
                    else {
                        rb.constraints = RigidbodyConstraints.None;
                    }
                }
            }
            //To release the molecule, make sure that the other controller is still grabbing the object
            if (otherController) {
                if (otherController.grabbedMolecule == null) {
                    grabbedMolecule.parent = loadedMols;
                }
            }
        }
        grabbedMolecule = null;
        savedParent = null;

        if (grabbedGroupMolecules.Count != 0) {
            foreach (Transform t in grabbedGroupMolecules) {
                t.parent = loadedMols;

                if (dm.isRunning) {
                    Rigidbody rb = t.gameObject.GetComponent<Rigidbody>();
                    if (rb != null) {
                        rb.constraints = RigidbodyConstraints.None;
                    }
                }
            }
            grabbedGroupMolecules.Clear();
        }
        isGroupGrabbed = false;

        isClicking = false;
        isScaling = false;
        isScalingGroup = false;

        if (otherController != null) {
            otherController.isScaling = false;
            otherController.isScalingGroup = false;
        }
        raycaster.needsUpdatePos = true;

        if (didScale) {
            raycaster.needsUpdateRadii = true;
        }
        didScale = false;
    }


    private void grabGroupMolecule(Transform toGrab) {

        DockingManager dm = UnityMolMain.getDockingManager();

        UnityMolStructure structure = sm.selectionNameToStructure(toGrab.name);
        grabbedCentroid = Vector3.zero;

        if (structure != null) {
            HashSet<UnityMolStructure> strucOfGroup = API.APIPython.getStructuresOfGroup(structure.groupID);
            foreach (UnityMolStructure s in strucOfGroup) {
                Transform structureParent = sm.GetStructureGameObject(s.name).transform;

                if (dm.isRunning) {
                    Rigidbody rb = structureParent.gameObject.GetComponent<Rigidbody>();
                    if (rb != null) {
                        rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ;
                    }
                }

                grabbedGroupMolecules.Add(structureParent);
                structureParent.parent = transform;
                grabbedCentroid += s.currentModel.centroid;
            }
            grabbedCentroid /= Mathf.Max(1, strucOfGroup.Count);
            isGroupGrabbed = true;
            grabbedGroupId = structure.groupID;

            // ViveInput.TriggerHapticPulseEx(curRole.roleType, curRole.roleValue, 500);
        }

    }

    void Update() {

        if (isScaling && !isGroupGrabbed) {
            //Only one controller is actually scaling => manage other controller grabbed molecule

            if (otherController.isGroupGrabbed) {
                return;
            }
            didScale = true;

            //Compute the current distance difference between controllers
            float magNow = sizeScale * (transform.position - otherController.transform.position).magnitude;
            float diff  = magNow - startMagnitude;
            float scaleVal = Mathf.Max(0.005f, Mathf.Min(0.5f, scaleStart.x + diff));

            //Get all molecules under the LoadedMolecules parent
            List<Transform> savedParents = new List<Transform>(sm.structureToGameObject.Count);
            foreach (GameObject gos in sm.structureToGameObject.Values) {
                savedParents.Add(gos.transform.parent);
                gos.transform.parent = loadedMols;
            }

            API.APIPython.changeGeneralScale(scaleVal);

            int id = 0;
            //Restore parents
            foreach (GameObject gos in sm.structureToGameObject.Values) {
                gos.transform.parent = savedParents[id++];
            }
        }
        if (isScalingGroup && !isScaling) {
            didScale = true;

            //Compute the current distance difference between controllers
            float magNow = sizeScale * (transform.position - otherController.transform.position).magnitude;
            float diff  = magNow - startMagnitude;
            float scaleVal = Mathf.Max(0.005f, Mathf.Min(0.5f, scaleStart.x + diff));

            List<Transform> savedPar = new List<Transform>(grabbedGroupMolecules.Count);

            foreach (GameObject gos in sm.structureToGameObject.Values) {
                savedPar.Add(gos.transform.parent);
                gos.transform.parent = loadedMols;
            }

            API.APIPython.changeGeneralScale(scaleVal);

            int i = 0;
            foreach (GameObject gos in sm.structureToGameObject.Values) {
                gos.transform.parent = savedPar[i++];
            }

        }

        // Selection drag: apply controller delta to atom.position of all selected atoms
        if (isGrabbingSelection && grabbedSelection != null && grabbedSelStructure != null
                && grabbedSelStructure.annotationParent != null) {
            Vector3 deltaWorld = transform.position - prevControllerPos;
            prevControllerPos = transform.position;
            if (deltaWorld.sqrMagnitude > 1e-10f) {
                Vector3 deltaLocal = grabbedSelStructure.annotationParent.InverseTransformVector(deltaWorld);
                var atoms = grabbedSelection.atoms;
                for (int i = 0; i < atoms.Count; i++)
                    atoms[i].position += deltaLocal;
                grabbedSelStructure.updateRepresentations(trajectory: true);
                raycaster.needsUpdatePos = true;
            }
        }

        // Joystick X/Y → rotación unificada:
        //   si hay selección activa → rota los átomos de la selección
        //   si no hay selección    → rota el Transform de la(s) molécula(s) (con o sin grip)
        if (!isGrabbingSelection && !isScaling && !isScalingGroup && !isGroupGrabbed && selMgr != null) {
            float joyX   = ViveInput.GetAxisEx((HandRole)curRole.roleValue, ControllerAxis.JoystickX);
            float joyY_r = ViveInput.GetAxisEx((HandRole)curRole.roleValue, ControllerAxis.JoystickY);
            if (Mathf.Abs(joyX) > 0.1f || Mathf.Abs(joyY_r) > 0.1f) {
                var rotSel   = selMgr.currentSelection;
                bool hasSel  = rotSel != null && rotSel.Count > 0;

                if (hasSel) {
                    // — Rotar átomos de la selección alrededor de su centroide —
                    var rotStruct = rotSel.atoms[0].residue.chain.model.structure;
                    Transform ap  = rotStruct.annotationParent;
                    if (ap != null) {
                        Vector3 centroid = Vector3.zero;
                        var ratoms = rotSel.atoms;
                        for (int i = 0; i < ratoms.Count; i++) centroid += ratoms[i].position;
                        centroid /= ratoms.Count;

                        Vector3 axisH = ap.InverseTransformDirection(Vector3.up).normalized;
                        Vector3 axisV = ap.InverseTransformDirection(Vector3.right).normalized;
                        Quaternion rot = Quaternion.AngleAxis(joyX   * selectionRotSpeed * Time.deltaTime, axisH)
                                       * Quaternion.AngleAxis(-joyY_r * selectionRotSpeed * Time.deltaTime, axisV);

                        for (int i = 0; i < ratoms.Count; i++)
                            ratoms[i].position = centroid + rot * (ratoms[i].position - centroid);

                        rotStruct.updateRepresentations(trajectory: true);
                        raycaster.needsUpdatePos = true;
                    }
                } else {
                    // — Sin selección: rotar el Transform de las moléculas —
                    // Centroide real = annotationParent.TransformPoint(currentModel.centroid)
                    List<Transform> targets = new List<Transform>(sm.loadedStructures.Count);
                    Vector3 worldCentroid = Vector3.zero;
                    int centCount = 0;
                    foreach (var s in sm.loadedStructures) {
                        if (!sm.structureToGameObject.ContainsKey(s.name)) continue;
                        // Con grip: solo las agarradas; sin grip: todas
                        var t = sm.structureToGameObject[s.name].transform;
                        if (isGroupGrabbed && !grabbedGroupMolecules.Contains(t)) continue;
                        targets.Add(t);
                        if (s.annotationParent != null) {
                            worldCentroid += s.annotationParent.TransformPoint(s.currentModel.centroid);
                            centCount++;
                        }
                    }
                    if (centCount > 0) worldCentroid /= centCount;
                    else if (targets.Count > 0) {
                        foreach (var t in targets) worldCentroid += t.position;
                        worldCentroid /= targets.Count;
                    }

                    if (targets.Count > 0) {
                        Quaternion rot = Quaternion.AngleAxis(joyX   * selectionRotSpeed * Time.deltaTime, Vector3.up)
                                       * Quaternion.AngleAxis(-joyY_r * selectionRotSpeed * Time.deltaTime, Vector3.right);

                        foreach (var t in targets) {
                            t.position = worldCentroid + rot * (t.position - worldCentroid);
                            t.rotation = rot * t.rotation;
                        }
                    }
                }
            }
        }

        // Joystick Y → acercar/alejar selección o molécula individual a lo largo del rayo del mando
        if (!isScaling && !isScalingGroup && (grabbedMolecule != null || isGrabbingSelection || isGroupGrabbed)) {
            float joyY = ViveInput.GetAxisEx((HandRole)curRole.roleValue, ControllerAxis.JoystickY);
            if (Mathf.Abs(joyY) > 0.1f) {
                Vector3 dir = transform.forward;
                if (isGrabbingSelection && grabbedSelection != null && grabbedSelStructure != null
                        && grabbedSelStructure.annotationParent != null) {
                    Vector3 deltaLocal = grabbedSelStructure.annotationParent.InverseTransformVector(dir * joyY * molDepthScrollSpeed * Time.deltaTime);
                    var atoms = grabbedSelection.atoms;
                    for (int i = 0; i < atoms.Count; i++)
                        atoms[i].position += deltaLocal;
                    grabbedSelStructure.updateRepresentations(trajectory: true);
                    raycaster.needsUpdatePos = true;
                }
                else if (grabbedMolecule != null) {
                    grabbedMolecule.position += dir * joyY * molDepthScrollSpeed * Time.deltaTime;
                }
                else if (isGroupGrabbed) {
                    Vector3 worldDelta = dir * joyY * molDepthScrollSpeed * Time.deltaTime;
                    foreach (var t in grabbedGroupMolecules)
                        t.position += worldDelta;
                }
            }
        }
    }
}
}
