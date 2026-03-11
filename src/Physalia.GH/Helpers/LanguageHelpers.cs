// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using Rhino;

namespace Physalia.GH.RunPython
{
    /// <summary>
    /// Provides helper methods for initialising Rhino's embedded Python 3 runtime.
    /// </summary>
    public static class LanguageHelpers
    {
        /// <summary>
        /// Loads Python 3 by writing a temporary script file and running it through the Rhino Script Editor.
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
