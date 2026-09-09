using System.Collections.Generic;
using UnityEngine;

namespace UMol {

/// <summary>
/// Shared "tint these atoms yellow across every active representation" logic,
/// used by PointerAtomSelection and SequenceSelectorUI. Only the apply side is
/// shared - each caller's "clear" strategy is genuinely different (one resets
/// color per atom, the other wipes all coloring on the representation) and
/// stays local; unifying that too would change behavior, not just de-duplicate it.
/// </summary>
public static class HighlightService {

    public static readonly Color32 HighlightColor = new Color32(255, 217, 0, 255);

    /// <summary>Colors the given atoms yellow on every active representation. Returns false (no-op) if there's no representation manager or the list is empty.</summary>
    public static bool ApplyHighlight(List<UnityMolAtom> atoms) {
        if (atoms == null || atoms.Count == 0) return false;
        var repManager = UnityMolMain.getRepresentationManager();
        if (repManager == null) return false;

        foreach (var rep in repManager.representations)
            rep.SetColors(atoms, HighlightColor);
        return true;
    }
}
}
