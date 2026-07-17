using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

namespace UMol {
    public class PythonConsole2 : MonoBehaviour {
        public Button showConsoleButton;
        public Button hideConsoleButton;
        public static List<string> m_previousCommands = new List<string>();

        public string ExecuteCommand(string command, ref bool success) {
            success = false;
            return string.Empty;
        }
        public IEnumerator ExecuteScript(string path) {
            yield break;
        }
        public static void addCommandsToUserPref(List<string> commands) { }
    }
}
