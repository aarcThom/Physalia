using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Physalia.GH.Helpers
{
    /// <summary>
    /// General helper methods dealing directly with Grasshopper and grasshopper docs.
    /// </summary>
    public static class GHSystemHelpers
    {
        /// <summary>
        /// Returns all installed Grasshopper Plugins.
        /// </summary>
        /// <returns>A List of installed plugins as strings.</returns>
        public static List<string> GetInstalledPluginNames()
        {
            return Grasshopper.Instances.ComponentServer.Libraries
                .Where(lib => !lib.Name.Equals("Grasshopper", StringComparison.OrdinalIgnoreCase))
                .Select(lib => lib.Name)
                .OrderBy(n => n)
                .ToList();
        }
    }
}
