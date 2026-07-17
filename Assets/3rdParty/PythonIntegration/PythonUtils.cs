namespace UMol {
    public static class PythonUtils {
        public static bool IsPythonFile(string path) {
            return path != null && path.EndsWith(".py");
        }
    }
}
