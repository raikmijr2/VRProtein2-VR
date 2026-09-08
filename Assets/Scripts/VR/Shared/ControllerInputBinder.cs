using System;
using System.Collections.Generic;
using HTC.UnityPlugin.Vive;

namespace UMol {

/// <summary>
/// Records ViveInput button bindings made via Bind() so they can all be
/// removed symmetrically with one UnbindAll() call. Replaces the
/// hand-written OnEnable (AddPressDown/AddPressUp) + OnDisable
/// (RemovePressDown/RemovePressUp) pairs that used to be copy-pasted in
/// every VR controller/pointer script - because each unbind here replays
/// exactly what was bound, it's impossible to remove a different button/
/// handler pair than the one that was registered (the bug PointerIMD had).
/// </summary>
public class ControllerInputBinder {

    struct Binding {
        public HandRole role;
        public ControllerButton button;
        public Action down;
        public Action up;
    }

    readonly List<Binding> bindings = new List<Binding>();

    /// <summary>Registers a button's down/up handlers. Pass null for onUp to bind press-down only.</summary>
    public void Bind(HandRole role, ControllerButton button, Action onDown, Action onUp = null) {
        bindings.Add(new Binding { role = role, button = button, down = onDown, up = onUp });
        if (onDown != null) ViveInput.AddPressDown(role, button, onDown);
        if (onUp   != null) ViveInput.AddPressUp(role, button, onUp);
    }

    /// <summary>Removes every binding made via Bind(), in the same role/button/handler combination it was added with.</summary>
    public void UnbindAll() {
        foreach (var b in bindings) {
            if (b.down != null) ViveInput.RemovePressDown(b.role, b.button, b.down);
            if (b.up   != null) ViveInput.RemovePressUp(b.role, b.button, b.up);
        }
        bindings.Clear();
    }
}
}
