using System.IO;
using Rhino;

namespace Physalia.GH.RunPython
{
    public static class Python3Lang
    {
        /// <summary>
        /// Loads Python3 by writing a temp .py file and loading it into the Rhino Script editor
        /// </summary>
        public static void LoadPython3()
        {
            string tempScript = Path.Combine(Path.GetTempPath(), "my_script.py");
            File.WriteAllText(tempScript, "#! python 3\nimport sys\nprint(sys.version)");
            RhinoApp.RunScript($"-_ScriptEditor _R \"{tempScript}\"", false);
        }
    }
}