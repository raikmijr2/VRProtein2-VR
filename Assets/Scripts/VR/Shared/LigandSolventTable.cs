using System.Collections.Generic;

namespace UMol {

/// <summary>
/// Solvent/common-ion residue names excluded when auto-detecting the "real"
/// ligand in a loaded structure (e.g. to decide what gets HyperBall vs Cartoon).
/// Shared by PDBLoaderUI and RepresentationSwitcherUI, which previously each
/// kept their own copy of this exact list.
/// </summary>
public static class LigandSolventTable {

    public static readonly HashSet<string> Residues = new HashSet<string> {
        "HOH", "WAT", "TIP", "TIP3", "SOL", "NA", "CL", "MG", "ZN", "CA",
        "K", "NA+", "CL-", "MG2+", "ZN2+", "CA2+", "FE", "MN", "NI", "CU"
    };
}
}
