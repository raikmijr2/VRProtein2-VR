using UnityEditor;
using UnityEngine;
using HTC.UnityPlugin.VRModuleManagement;

/// Crea VRModuleSettings.asset en Resources con InitializeOnStartup=true.
/// Ejecutar una sola vez: Tools → Fix VIU Initialize on Startup
public static class FixVIUSettings {

    [MenuItem("Tools/Fix VIU Initialize on Startup")]
    static void Fix() {
        const string path = "Assets/Resources/VRModuleSettings.asset";

        var existing = AssetDatabase.LoadAssetAtPath<VRModuleSettings>(path);
        if (existing == null) {
            var asset = ScriptableObject.CreateInstance<VRModuleSettings>();
            AssetDatabase.CreateAsset(asset, path);
            existing = asset;
        }

        var so = new SerializedObject(existing);
        var prop = so.FindProperty("m_initializeOnStartup");
        if (prop != null) {
            prop.boolValue = false;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            Debug.Log("VRModuleSettings: InitializeOnStartup = false guardado en " + path);
        } else {
            Debug.LogError("No se encontró m_initializeOnStartup en VRModuleSettings");
        }
    }
}
