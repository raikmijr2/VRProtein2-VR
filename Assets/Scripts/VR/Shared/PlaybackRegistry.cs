using UnityEngine;

namespace UMol {

/// <summary>
/// Pauses every trajectory/animation playback panel in the scene. Used before
/// starting a selection-atom drag (ControllerGrabAndScale) so playback doesn't
/// overwrite the atom positions being dragged. Both call sites this replaces
/// already used the same includeInactive:false default, so this is a safe
/// 1:1 merge (unlike the highlight "clear" strategies, which genuinely differ).
/// </summary>
public static class PlaybackRegistry {

    public static void PauseAll() {
        foreach (var ui in Object.FindObjectsOfType<TrajAnimationUI>())  ui.Pause();
        foreach (var ui in Object.FindObjectsOfType<AnimationPlayerUI>()) ui.Pause();
    }
}
}
