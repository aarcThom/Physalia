using System.IO;
using Rhino;

namespace Physalia.GH.RunPython
{
    public static class LanguageHelpers
    {
        /// <summary>
        /// Loads Python3 by writing a temp .py file and loading it into the Rhino Script editor
        /// </summary>
        public static void LoadPython3()
        {
            // write a Python3 script to temp
            string tempScript = Path.Combine(Path.GetTempPath(), "my_script.py");
            File.WriteAllText(tempScript, "#! python 3\nprint('(~~~)~≈≈≈≈')"); // a bad attempt at a physalia

            // run it to start the Rhino's Python3
            RhinoApp.RunScript($"-_ScriptEditor _R \"{tempScript}\"", false);
        }
    }
}