using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace UMol {

/// <summary>
/// Reads one or more DCD trajectory files (CHARMM/NAMD/AMBER binary format).
/// Converts Angstrom coordinates to UnityMol space (negate X for handedness).
/// </summary>
public static class DCDReader {

    /// <summary>
    /// Read all DCD files in a folder (sorted numerically) and return all frames.
    /// Also returns the atom count found in the first file.
    /// </summary>
    public static List<Vector3[]> ReadFolder(string folderPath, out int natom) {
        natom = 0;
        if (!Directory.Exists(folderPath)) {
            Debug.LogError("DCD folder not found: " + folderPath);
            return null;
        }

        string[] files = Directory.GetFiles(folderPath, "*.dcd");
        if (files.Length == 0) {
            Debug.LogError("No .dcd files found in: " + folderPath);
            return null;
        }

        // Sort numerically: "1.dcd" < "2.dcd" < "10.dcd" etc.
        Array.Sort(files, (a, b) => {
            string na = Path.GetFileNameWithoutExtension(a);
            string nb = Path.GetFileNameWithoutExtension(b);
            if (int.TryParse(na, out int ia) && int.TryParse(nb, out int ib))
                return ia.CompareTo(ib);
            return string.Compare(a, b, StringComparison.Ordinal);
        });

        var allFrames = new List<Vector3[]>();
        int detectedNatom = 0;

        foreach (string file in files) {
            var frames = ReadFile(file, out int n);
            if (frames == null) continue;
            if (detectedNatom == 0) detectedNatom = n;
            else if (n != detectedNatom) {
                continue;
            }
            allFrames.AddRange(frames);
        }

        natom = detectedNatom;
        return allFrames;
    }

    /// <summary>
    /// Read a single DCD file. Returns list of frames (each frame = Vector3 array of atom positions in Angstroms).
    /// </summary>
    public static List<Vector3[]> ReadFile(string path, out int natom) {
        natom = 0;
        if (!File.Exists(path)) {
            Debug.LogError("DCD file not found: " + path);
            return null;
        }

        try {
            using var br = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read));
            return ReadDCD(br, out natom);
        }
        catch (Exception e) {
            Debug.LogError($"DCD read error in {path}: {e.Message}");
            return null;
        }
    }

    // ── Binary DCD parser ─────────────────────────────────────────────────────

    static List<Vector3[]> ReadDCD(BinaryReader br, out int natom) {
        natom = 0;

        // FORTRAN record 1: header (84 bytes)
        int rec1 = br.ReadInt32();
        bool bigEndian = false;
        if (rec1 != 84) {
            // Try byte-swapping
            int swapped = SwapInt(rec1);
            if (swapped == 84) {
                bigEndian = true;
            } else {
                Debug.LogError($"DCD: unexpected header size {rec1}");
                return null;
            }
        }

        byte[] sig = br.ReadBytes(4);  // "CORD"
        int nframes   = ReadInt(br, bigEndian);
        br.ReadBytes(3 * 4);           // istart, nsavc, nstep
        br.ReadBytes(4 * 4);           // 4 padding
        br.ReadBytes(2 * 4);           // ndegf, namnf
        float dt = ReadFloat(br, bigEndian);
        int hasUC = ReadInt(br, bigEndian); // has unitcell
        br.ReadBytes(7 * 4);           // 7 padding
        int charmmVer = ReadInt(br, bigEndian);
        br.ReadInt32();                // end marker

        // FORTRAN record 2: title block
        int titleRec = ReadMarker(br, bigEndian);
        int ntitle = ReadInt(br, bigEndian);
        br.ReadBytes(ntitle * 80);
        ReadMarker(br, bigEndian);

        // FORTRAN record 3: natom
        ReadMarker(br, bigEndian);
        natom = ReadInt(br, bigEndian);
        ReadMarker(br, bigEndian);

        if (natom <= 0) {
            Debug.LogError("DCD: natom = " + natom);
            return null;
        }

        var frames = new List<Vector3[]>();
        bool hasUnitCell = (hasUC != 0);

        while (br.BaseStream.Position < br.BaseStream.Length) {
            try {
                var frame = ReadFrame(br, natom, bigEndian, hasUnitCell);
                if (frame != null) frames.Add(frame);
            }
            catch (EndOfStreamException) {
                break;
            }
            catch (Exception e) {
                break;
            }
        }

        return frames;
    }

    static Vector3[] ReadFrame(BinaryReader br, int natom, bool bigEndian, bool hasUnitCell) {
        // Optional unit cell block (6 doubles = 48 bytes)
        if (hasUnitCell) {
            int ucLen = ReadMarker(br, bigEndian);
            if (ucLen == 48) {
                br.ReadBytes(48);
                ReadMarker(br, bigEndian);
            } else {
                // Not a unit cell block — rewind not possible, skip
                br.ReadBytes(ucLen);
                ReadMarker(br, bigEndian);
            }
        }

        float[] xs = ReadFloatRecord(br, natom, bigEndian);
        float[] ys = ReadFloatRecord(br, natom, bigEndian);
        float[] zs = ReadFloatRecord(br, natom, bigEndian);

        var pos = new Vector3[natom];
        for (int i = 0; i < natom; i++) {
            // Negate X: DCD is right-handed, Unity is left-handed (same as PDB loader)
            pos[i] = new Vector3(-xs[i], ys[i], zs[i]);
        }
        return pos;
    }

    static float[] ReadFloatRecord(BinaryReader br, int natom, bool bigEndian) {
        int len = ReadMarker(br, bigEndian);
        if (len != natom * 4) {
        }
        float[] arr = new float[natom];
        for (int i = 0; i < natom; i++)
            arr[i] = ReadFloat(br, bigEndian);
        ReadMarker(br, bigEndian);
        return arr;
    }

    // ── Endianness helpers ────────────────────────────────────────────────────

    static int ReadMarker(BinaryReader br, bool bigEndian) => ReadInt(br, bigEndian);

    static int ReadInt(BinaryReader br, bool bigEndian) {
        int v = br.ReadInt32();
        return bigEndian ? SwapInt(v) : v;
    }

    static float ReadFloat(BinaryReader br, bool bigEndian) {
        if (!bigEndian) return br.ReadSingle();
        byte[] b = br.ReadBytes(4);
        Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
    }

    static int SwapInt(int v) {
        return ((v & 0xFF) << 24) | (((v >> 8) & 0xFF) << 16) |
               (((v >> 16) & 0xFF) << 8) | ((v >> 24) & 0xFF);
    }
}
}
