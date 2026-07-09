// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace Physalia.GH.Generation;

/// <summary>
/// A floating parameter on the canvas that references live geometry in the Rhino model: its unique
/// name (the parameter's nickname), the geometry type it carries, and the live parameter itself
/// (whose output is the geometry source). A generated graph wires FROM such a parameter instead of
/// recreating the geometry. Produced by <see cref="CanvasRhinoReferences.Collect"/>.
/// </summary>
/// <param name="Name">The unique name the model references (the parameter's nickname).</param>
/// <param name="TypeName">The geometry type the input carries (e.g. <c>Curve</c>, <c>Point</c>).</param>
/// <param name="LiveOutput">The live parameter on the canvas (its own output is the geometry source).</param>
public sealed record ReferencedRhinoGeometry(string Name, string TypeName, IGH_Param LiveOutput);

/// <summary>
/// Detects, from the Rhino/Grasshopper side, which canvas parameters reference live Rhino document
/// objects — the single source of that fact for the canvas-state export annotation, the patch
/// modify-guard, the legacy reference splice, the Rhino Geometry tool's alias dedup, and the chat
/// window's Referenced Rhino Geometry page. Detection reads the parameters themselves (goo with
/// <see cref="IGH_GeometricGoo.IsReferencedGeometry"/> set), so it covers ANY referenced parameter
/// — ones placed by the Rhino Geometry tool and ones the user referenced by hand alike — with no
/// registry to keep in sync.
/// </summary>
internal static class CanvasRhinoReferences
{
    /// <summary>
    /// Collects every floating parameter in <paramref name="doc"/> whose data references a live
    /// Rhino object, as name + type + the live parameter. Parameters with blank nicknames are
    /// skipped and names are unique (first wins on a collision), matching how generated graphs
    /// resolve references by name.
    /// </summary>
    /// <param name="doc">The Grasshopper document to scan, or null.</param>
    /// <returns>The Rhino-referenced parameters.</returns>
    internal static IReadOnlyList<ReferencedRhinoGeometry> Collect(GH_Document? doc)
    {
        var result = new List<ReferencedRhinoGeometry>();
        if (doc is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IGH_Param param in doc.Objects.OfType<IGH_Param>())
        {
            if (string.IsNullOrWhiteSpace(param.NickName)
                || !HasReferencedGoo(param)
                || !seen.Add(param.NickName))
            {
                continue;
            }

            result.Add(new ReferencedRhinoGeometry(param.NickName, ComponentSignatureProvider.SafeTypeName(param), param));
        }

        return result;
    }

    /// <summary>
    /// Returns whether <paramref name="obj"/> is a floating parameter referencing live Rhino
    /// geometry. Used by the patch modify-guard: rebuilding such a parameter would sever its link
    /// to the Rhino object (the GhJSON round-trip bakes values, never reference ids).
    /// </summary>
    /// <param name="obj">The canvas object to test.</param>
    /// <returns>true when the object is a Rhino-referenced parameter.</returns>
    internal static bool IsRhinoReferenced(IGH_DocumentObject obj) =>
        obj is IGH_Param param && HasReferencedGoo(param);

    // Reads the parameter's data for referenced geometry. Persistent data is the source of truth
    // and is available before the first solve, but only through the typed generic base — the two
    // types the Rhino Geometry tool creates are read directly; every other parameter type falls
    // back to volatile data (populated once the param has solved, which self-heals on the next
    // canvas-state re-export if a fresh load is caught early).
    private static bool HasReferencedGoo(IGH_Param param)
    {
        IEnumerable<IGH_Goo> data = param switch
        {
            Param_Point point => point.PersistentData.AllData(true),
            Param_Curve curve => curve.PersistentData.AllData(true),
            _ => param.VolatileData.AllData(true),
        };

        return data.OfType<IGH_GeometricGoo>().Any(g => g.IsReferencedGeometry);
    }
}
