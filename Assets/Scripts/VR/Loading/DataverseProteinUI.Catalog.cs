using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using MiniJSON;

namespace UMol {

public partial class DataverseProteinUI {

    IEnumerator LoadCatalog() {
        SetStatus("Cargando catálogo...", Color.white);

        // Try cache first (valid for 7 days)
        if (File.Exists(CachePath)) {
            FileInfo fi = new FileInfo(CachePath);
            if ((DateTime.Now - fi.LastWriteTime).TotalDays < 7) {
                string cached = null;
                bool readOk = true;
                try { cached = File.ReadAllText(CachePath); }
                catch (Exception e) {
                    readOk = false;
                    Debug.LogWarning("[Dataverse] No se pudo leer la caché: " + e.Message);
                }
                if (readOk) {
                    ParseFileList(cached);
                    if (allFiles.Count > 0) {
                        catalogReady = true;
                        SetStatus($"Catálogo cargado ({allFiles.Count} archivos)", Color.green);
                        yield break;
                    }
                }
            }
        }

        // Fetch from API — try large limit first, paginate if needed
        // Dataverse /api/.../files returns {"status":"OK","data":[...]} (data is a list)
        var collected = new List<object>();
        int offset = 0, limit = 1000;
        bool more = true;

        while (more) {
            string url = $"{BASE_URL}{FILES_API}{Uri.EscapeDataString(DATASET_ID)}&limit={limit}&offset={offset}";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Accept", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) {
                SetStatus("Error al cargar catálogo: " + req.error, Color.red);
                yield break;
            }

            var root = Json.Deserialize(req.downloadHandler.text) as Dictionary<string, object>;
            if (root == null) { SetStatus("JSON inválido", Color.red); yield break; }

            // Dataverse standard: data is a List<object>
            List<object> page = null;
            if (root.ContainsKey("data")) {
                page = root["data"] as List<object>;
                if (page == null) {
                    // Fallback: data might be a dict with "files" key (some versions)
                    var dataDict = root["data"] as Dictionary<string, object>;
                    if (dataDict != null && dataDict.ContainsKey("files"))
                        page = dataDict["files"] as List<object>;
                }
            }

            if (page == null || page.Count == 0) break;
            collected.AddRange(page);
            more = page.Count == limit;  // if we got a full page, there may be more
            offset += limit;

            SetStatus($"Catálogo: {collected.Count} archivos...", Color.white);
            yield return null;
        }

        // Serialize collected entries and cache (flat list under "files" key)
        var cacheRoot = new Dictionary<string, object> { ["files"] = collected };
        string cacheJson = Json.Serialize(cacheRoot);
        try {
            File.WriteAllText(CachePath, cacheJson);
        } catch (Exception e) {
            Debug.LogWarning("[Dataverse] No se pudo escribir la caché: " + e.Message);
        }

        ParseFileList(collected);
        catalogReady = true;
        SetStatus($"Catálogo listo ({allFiles.Count} archivos DCD)", Color.green);
    }

    void ParseFileList(string json) {
        allFiles.Clear();
        var root  = Json.Deserialize(json) as Dictionary<string, object>;
        if (root == null) return;
        List<object> files = null;
        if (root.ContainsKey("files"))
            files = root["files"] as List<object>;
        if (files == null && root.ContainsKey("data"))
            files = root["data"] as List<object>;
        if (files == null) return;
        ParseFileList(files);
    }

    void ParseFileList(List<object> files) {
        allFiles.Clear();
        if (files == null) return;

        foreach (var f in files) {
            var entry = f as Dictionary<string, object>;
            if (entry == null) continue;
            var df  = entry.ContainsKey("dataFile") ? entry["dataFile"] as Dictionary<string, object> : null;
            if (df  == null) continue;
            string dir   = entry.ContainsKey("directoryLabel") ? entry["directoryLabel"] as string ?? "" : "";
            string label = entry.ContainsKey("label") ? entry["label"] as string ?? "" : "";
            int    id    = df.ContainsKey("id") ? Convert.ToInt32(df["id"]) : 0;
            if (id == 0 || !label.EndsWith(".dcd", StringComparison.OrdinalIgnoreCase)) continue;
            allFiles.Add(new FileEntry { id = id, name = label, dir = dir });
        }
    }
}
}
