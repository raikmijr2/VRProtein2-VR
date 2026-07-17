using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UMol.API;

namespace UMol {

public static class PRMTOPReader {

    // Entry point: load PRMTOP topology.
    // skipSolvent=true filters out water and ions (huge performance win for MD systems).
    // dcdAtomIndices maps structure-atom-index → DCD-file-atom-index, used to filter frames.
    public static UnityMolStructure Load(string prmtopPath, Vector3[] firstFrame,
                                          out int[] dcdAtomIndices, bool skipSolvent = true) {
        dcdAtomIndices = null;
        return LoadInternal(prmtopPath, firstFrame, out dcdAtomIndices, skipSolvent);
    }

    // Backwards-compatible overload (no filtering).
    public static UnityMolStructure Load(string prmtopPath, Vector3[] firstFrame) {
        int[] dummy;
        return LoadInternal(prmtopPath, firstFrame, out dummy, skipSolvent: false);
    }

    static UnityMolStructure LoadInternal(string prmtopPath, Vector3[] firstFrame,
                                           out int[] dcdAtomIndices, bool skipSolvent) {
        dcdAtomIndices = null;
        if (!File.Exists(prmtopPath)) {
            Debug.LogError("PRMTOP file not found: " + prmtopPath);
            return null;
        }

        // Parse sections
        var sections = ReadSections(prmtopPath);

        if (!sections.ContainsKey("POINTERS")) {
            Debug.LogError("PRMTOP: missing POINTERS section");
            return null;
        }

        int[] ptrs = ParseInts(sections["POINTERS"]);
        int natom = ptrs[0];
        int nres  = ptrs[11];
        int nbonh = ptrs[2];
        int mbona = ptrs[3];

        string[] atomNames  = ParseStrings4(Get(sections, "ATOM_NAME"),         natom);
        string[] amberTypes = ParseStrings4(Get(sections, "AMBER_ATOM_TYPE"),    natom);
        string[] resLabels  = ParseStrings4(Get(sections, "RESIDUE_LABEL"),      nres);
        int[]    resPtr     = ParseInts(Get(sections, "RESIDUE_POINTER"));
        int[]    bondsH     = ParseInts(Get(sections, "BONDS_INC_HYDROGEN"));
        int[]    bondsNoH   = ParseInts(Get(sections, "BONDS_WITHOUT_HYDROGEN"));

        // Build residues first so we can classify each atom as solvent or not
        // dcdIdx[i] = index in the DCD file for structure atom i
        var atomList    = new List<UnityMolAtom>();
        var indexMap    = new List<int>();   // structure index → DCD index
        var residues    = new List<UnityMolResidue>();
        int structResId = 0;

        for (int r = 0; r < nres; r++) {
            int startA   = (resPtr.Length > r)     ? resPtr[r] - 1     : 0;
            int endA     = (resPtr.Length > r + 1) ? resPtr[r + 1] - 1 : natom;
            string resName = (resLabels.Length > r) ? resLabels[r].Trim() : "UNK";

            bool isSolvent = skipSolvent && IsSolvent(resName);
            if (isSolvent) continue;

            var ratoms = new List<UnityMolAtom>();
            for (int a = startA; a < endA && a < natom; a++) {
                string aname = atomNames.Length > a ? atomNames[a].Trim() : "X";
                string atype = amberTypes.Length > a ? amberTypes[a].Trim() : "";
                string elem  = ElementFrom(atype, aname);
                Vector3 pos  = (firstFrame != null && a < firstFrame.Length) ? firstFrame[a] : Vector3.zero;
                var atom = new UnityMolAtom(aname, elem, pos, 0f, atomList.Count + 1);
                ratoms.Add(atom);
                atomList.Add(atom);
                indexMap.Add(a);
            }
            if (ratoms.Count == 0) continue;

            var res = new UnityMolResidue(structResId, structResId + 1, ratoms, resName);
            foreach (var a in ratoms) a.SetResidue(res);
            residues.Add(res);
            structResId++;
        }

        dcdAtomIndices = indexMap.ToArray();
        var atoms = atomList.ToArray();

        // Assign chains: protein/ligand → "A", ions → "I"
        var chains = BuildChains(residues);
        foreach (var c in chains)
            foreach (var r in c.residues) r.chain = c;

        var model = new UnityMolModel(chains, "0");
        model.allAtoms.AddRange(atoms);
        foreach (var c in chains) c.model = model;

        string structName = Path.GetFileNameWithoutExtension(prmtopPath);
        UnityMolStructure newStruct = new UnityMolStructure(new List<UnityMolModel> { model }, structName);
        model.structure = newStruct;

        // fillIdAtoms must run before Add() so idInAllAtoms is valid
        model.fillIdAtoms();

        // Build bonds — need reverse map: DCD-atom-index → structure atom (null if solvent)
        var dcdToAtom = new Dictionary<int, UnityMolAtom>(atoms.Length);
        for (int i = 0; i < indexMap.Count; i++)
            dcdToAtom[indexMap[i]] = atoms[i];

        var bonds = new UnityMolBonds();
        AddBondsFromMap(bonds, bondsH,   dcdToAtom);
        AddBondsFromMap(bonds, bondsNoH, dcdToAtom);
        model.bonds = bonds;

        model.ComputeCentroid();
        newStruct.SetStructureMolecularType();

        DSSP.assignSS_DSSP(newStruct);

        // Resolve name conflicts BEFORE creating any Unity objects or selections.
        // If we let AddStructure rename the structure (e.g. "lysine" → "lysine_2") AFTER the GO
        // and selection are already created under "all_lysine", the renamed structure has no GO
        // and SetVRInteractableObject throws, aborting the entire load.
        UnityMolStructureManager sm2 = UnityMolMain.getStructureManager();
        if (sm2.isNameUsed(newStruct.name))
            newStruct.name = sm2.findNewStructureName(newStruct.name);

        // Pre-create the structure GO with the definitive name so AddStructure can find it.
        UnityMolMain.getRepStructureParent(newStruct.ToSelectionName());

        UnityMolSelection sel = newStruct.ToSelection();
        Reader.CreateUnityObjects(newStruct.ToSelectionName(), sel);
        newStruct.surfThread = Reader.StartSurfaceThread(sel);

        UnityMolMain.getStructureManager().AddStructure(newStruct);
        UnityMolMain.getSelectionManager().Add(sel);

        return newStruct;
    }

    // ── Bond helpers ──────────────────────────────────────────────────────────

    static void AddBondsFromMap(UnityMolBonds bonds, int[] arr, Dictionary<int, UnityMolAtom> dcdToAtom) {
        // AMBER encoding: triples (IA-1)*3, (IB-1)*3, type_index
        for (int i = 0; i + 2 < arr.Length; i += 3) {
            int d1 = arr[i]     / 3;
            int d2 = arr[i + 1] / 3;
            if (dcdToAtom.TryGetValue(d1, out var a1) && dcdToAtom.TryGetValue(d2, out var a2))
                bonds.Add(a1, a2);
        }
    }

    // ── Chain builder ─────────────────────────────────────────────────────────

    static bool IsSolvent(string resName) {
        switch (resName.ToUpper()) {
            case "WAT": case "TIP3": case "TIP4": case "HOH": case "SOL":
            case "NA":  case "CL":  case "K":   case "MG":  case "CA":
            case "ZN":  case "NA+": case "CL-":
                return true;
        }
        return false;
    }

    static List<UnityMolChain> BuildChains(List<UnityMolResidue> residues) {
        var protRes = new List<UnityMolResidue>();
        var ionRes  = new List<UnityMolResidue>();

        foreach (var r in residues) {
            string n = r.name.ToUpper();
            if (n == "NA" || n == "CL" || n == "K" || n == "MG" || n == "CA" || n == "ZN")
                ionRes.Add(r);
            else
                protRes.Add(r);
        }

        var chains = new List<UnityMolChain>();
        if (protRes.Count > 0) chains.Add(new UnityMolChain(protRes, "A"));
        if (ionRes.Count  > 0) chains.Add(new UnityMolChain(ionRes,  "I"));
        if (chains.Count == 0) chains.Add(new UnityMolChain(residues, "A"));
        return chains;
    }

    // ── Element detection ─────────────────────────────────────────────────────

    static string ElementFrom(string amberType, string atomName) {
        string t = string.IsNullOrEmpty(amberType) ? atomName : amberType;
        t = t.Trim().TrimStart('0','1','2','3','4','5','6','7','8','9');
        if (t.Length == 0) return "C";
        return char.ToUpper(t[0]) switch {
            'C' => "C", 'N' => "N", 'O' => "O", 'H' => "H",
            'S' => "S", 'P' => "P", 'F' => "F", 'I' => "I",
            'B' => "B", 'Z' => "ZN", _ => "C"
        };
    }

    // ── PRMTOP text parser ────────────────────────────────────────────────────

    static Dictionary<string, string> ReadSections(string path) {
        var sections = new Dictionary<string, string>();
        string currentFlag = null;
        var sb = new System.Text.StringBuilder();

        foreach (string rawLine in File.ReadLines(path)) {
            string line = rawLine.TrimEnd();
            if (line.StartsWith("%FLAG")) {
                if (currentFlag != null) sections[currentFlag] = sb.ToString();
                currentFlag = line.Substring(5).Trim();
                sb.Clear();
            } else if (line.StartsWith("%FORMAT") || line.StartsWith("%VERSION")) {
                // skip
            } else if (currentFlag != null) {
                sb.AppendLine(line);
            }
        }
        if (currentFlag != null) sections[currentFlag] = sb.ToString();
        return sections;
    }

    static int[] ParseInts(string raw) {
        var parts = raw.Split(new char[]{' ','\t','\n','\r'}, System.StringSplitOptions.RemoveEmptyEntries);
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }

    static string Get(Dictionary<string, string> d, string key) {
        d.TryGetValue(key, out string v);
        return v ?? "";
    }

    // PRMTOP 20a4 format: each line has up to 20 4-char strings
    static string[] ParseStrings4(string raw, int count) {
        var result = new List<string>(count);
        foreach (string line in raw.Split('\n')) {
            if (line.Trim().Length == 0) continue;
            string l = line.TrimEnd();
            for (int i = 0; i + 4 <= l.Length && result.Count < count; i += 4)
                result.Add(l.Substring(i, 4));
        }
        return result.ToArray();
    }
}
}
